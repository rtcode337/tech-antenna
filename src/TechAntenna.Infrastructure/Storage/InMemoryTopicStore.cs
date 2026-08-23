using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;

namespace TechAntenna.Infrastructure.Storage;

/// <summary>語彙(トピック)のメモリ上の実装。接続文字列なしのお試し起動とテストで使う。</summary>
public class InMemoryTopicStore : ITopicStore
{
    readonly object _gate = new();
    readonly Dictionary<string, Topic> _byKey = new(StringComparer.Ordinal);

    public Task UpsertAsync(
        IReadOnlyList<Topic> topics,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            // 今回現れなかったトピックは件数と話題度だけ 0 にする(行は消さない)
            var seen = topics.Select(topic => topic.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var missing in _byKey.Values.Where(topic => !seen.Contains(topic.Key)))
            {
                Reset(missing);
            }

            foreach (var topic in topics)
            {
                if (!_byKey.TryGetValue(topic.Key, out var stored))
                {
                    stored = new Topic { Key = topic.Key };
                    _byKey[topic.Key] = stored;
                }

                Apply(stored, topic, updatedAt);
            }
        }

        return Task.CompletedTask;
    }

    public Task SaveAsync(Topic topic, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_byKey.TryGetValue(topic.Key, out var stored))
            {
                stored = new Topic { Key = topic.Key };
                _byKey[topic.Key] = stored;
            }

            Apply(stored, topic, updatedAt);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Topic>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<Topic>>(_byKey.Values
                .OrderByDescending(topic => topic.IsSelected)
                .ThenByDescending(topic => topic.SubtreeTrendScore)
                .ThenBy(topic => topic.Key, StringComparer.Ordinal)
                .ToList());
        }
    }

    public Task<Topic?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_byKey.GetValueOrDefault(key));
        }
    }

    public Task<int> RemoveAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var removed = 0;
            foreach (var key in keys)
            {
                // 選択済みは消さない(収集キーワードごと失われるため)
                if (_byKey.TryGetValue(key, out var topic) && !topic.IsSelected)
                {
                    _byKey.Remove(key);
                    removed++;
                }
            }

            return Task.FromResult(removed);
        }
    }

    public Task UpdateSelectionAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        var selected = TagNormalizer.Normalize(keys).ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            foreach (var topic in _byKey.Values)
            {
                topic.IsSelected = selected.Contains(topic.Key);
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> SetSelectedAsync(
        string key, bool selected, CancellationToken cancellationToken = default)
    {
        // 画面から来るのは正規化済みのキーだが、念のため同じ規則を通してから当てる
        // (`Normalize` はカンマで語を分けるので、1 語に落ちないものは受け付けない)
        var normalized = TagNormalizer.Normalize([key]);
        if (normalized.Count != 1)
        {
            return Task.FromResult(false);
        }

        lock (_gate)
        {
            if (!_byKey.TryGetValue(normalized[0], out var topic))
            {
                return Task.FromResult(false);
            }

            topic.IsSelected = selected;

            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<SelectedTopic>> GetSelectedAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<SelectedTopic>>(Ordered()
                .Select(topic => new SelectedTopic(
                    topic.Key,
                    topic.Display is { Length: > 0 } display ? display : topic.Key,
                    topic.English))
                .ToList());
        }
    }

    public Task<int> UpdateOrderAsync(
        IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        var wanted = TagNormalizer.Normalize(keys);

        lock (_gate)
        {
            // 渡された順が先。**渡されなかった選択済みは、いまの並びのまま後ろへ** ——
            // 画面には本のあるトピックしか出ないので、出ていない行は渡ってこない
            var ordered = wanted
                .Where(_byKey.ContainsKey)
                .Concat(Ordered().Select(topic => topic.Key))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var order = 0;
            foreach (var key in ordered)
            {
                if (_byKey.TryGetValue(key, out var topic) && topic.IsSelected)
                {
                    topic.SortOrder = ++order;
                }
            }

            return Task.FromResult(order);
        }
    }

    /// <summary>選択済みを画面の並び(<see cref="Topic.SortOrder"/>)で。未指定は後ろへ。</summary>
    /// <remarks>ロックの中から呼ぶこと。</remarks>
    IEnumerable<Topic> Ordered() => _byKey.Values
        .Where(topic => topic.IsSelected)
        // 0(未指定)を後ろへ回す。指定済みの間に割り込ませない
        .OrderBy(topic => topic.SortOrder == 0)
        .ThenBy(topic => topic.SortOrder)
        .ThenBy(topic => topic.Key, StringComparer.Ordinal);

    internal static void Reset(Topic topic)
    {
        topic.TrendScore = 0;
        topic.SubtreeTrendScore = 0;
        topic.ArticleCount = 0;
        topic.EventCount = 0;
        topic.BookCount = 0;
    }

    /// <summary>
    /// 更新内容を1行に写す(EF 版と同じ規則にするため共有する)。
    /// <b>選択(<see cref="Topic.IsSelected"/>)と並び(<see cref="Topic.SortOrder"/>)は
    /// 写さない</b> —— どちらも画面で人が決めたもので、収集や整備のたびに巻き戻したくない。
    /// </summary>
    internal static void Apply(Topic stored, Topic topic, DateTimeOffset updatedAt)
    {
        stored.Display = topic.Display;
        stored.Parent = topic.Parent;
        stored.English = topic.English;
        stored.Description = topic.Description;
        stored.DecidedBy = topic.DecidedBy;
        stored.TrendScore = topic.TrendScore;
        stored.SubtreeTrendScore = topic.SubtreeTrendScore;
        stored.ArticleCount = topic.ArticleCount;
        stored.EventCount = topic.EventCount;
        stored.BookCount = topic.BookCount;
        stored.UpdatedAt = updatedAt;
    }
}

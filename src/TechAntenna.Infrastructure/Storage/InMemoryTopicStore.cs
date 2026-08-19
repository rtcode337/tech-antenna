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
            return Task.FromResult<IReadOnlyList<SelectedTopic>>(_byKey.Values
                .Where(topic => topic.IsSelected)
                .OrderBy(topic => topic.Key, StringComparer.Ordinal)
                .Select(topic => new SelectedTopic(
                    topic.Key,
                    topic.Display is { Length: > 0 } display ? display : topic.Key,
                    topic.English))
                .ToList());
        }
    }

    internal static void Reset(Topic topic)
    {
        topic.TrendScore = 0;
        topic.SubtreeTrendScore = 0;
        topic.ArticleCount = 0;
        topic.EventCount = 0;
        topic.BookCount = 0;
    }

    /// <summary>更新内容を1行に写す(EF 版と同じ規則にするため共有する)。選択は触らない。</summary>
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

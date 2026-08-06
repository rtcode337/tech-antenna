using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;

namespace TechAntenna.Infrastructure.Storage;

/// <summary>接続文字列が無い開発時用のメモリ上トピックストア。</summary>
public class InMemoryTopicStore : ITopicStore
{
    readonly object _gate = new();
    readonly Dictionary<string, StoredTopic> _byTag = new(StringComparer.Ordinal);

    public Task UpsertAsync(
        IReadOnlyList<TopicUpdate> topics,
        DateTimeOffset collectedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            // 今回現れなかったトピックは話題度だけ 0 にする(行は消さない)
            var seen = topics.Select(topic => topic.Tag).ToHashSet(StringComparer.Ordinal);
            foreach (var stored in _byTag.Values.Where(stored => !seen.Contains(stored.Tag)))
            {
                stored.TrendScore = 0;
                stored.SubtreeTrendScore = 0;
                stored.SourceCount = 0;
                // 収集済みの件数も落とす —— 別名がまとまってタグが消えたときに古い件数が残るため
                stored.ArticleCount = 0;
                stored.EventCount = 0;
                stored.BookCount = 0;
            }

            foreach (var topic in topics)
            {
                if (!_byTag.TryGetValue(topic.Tag, out var stored))
                {
                    stored = new StoredTopic { Tag = topic.Tag };
                    _byTag[topic.Tag] = stored;
                }

                Apply(stored, topic, collectedAt);
            }
        }

        return Task.CompletedTask;
    }

    public Task<int> RemoveAsync(IReadOnlyList<string> tags, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var removed = 0;
            foreach (var tag in tags)
            {
                // 選択済みは消さない(収集キーワードごと失われるため)
                if (_byTag.TryGetValue(tag, out var stored) && !stored.IsSelected)
                {
                    _byTag.Remove(tag);
                    removed++;
                }
            }

            return Task.FromResult(removed);
        }
    }

    public Task<IReadOnlyList<StoredTopic>> GetTopicsAsync(int count, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<StoredTopic> result = _byTag.Values
                // 選択済みは話題度が 0 でも押し出されないよう先頭に固定する
                .OrderByDescending(topic => topic.IsSelected)
                .ThenByDescending(topic => topic.SubtreeTrendScore)
                .ThenBy(topic => topic.Tag, StringComparer.Ordinal)
                .Take(count)
                .ToList();

            return Task.FromResult(result);
        }
    }

    public Task UpdateSelectionAsync(IReadOnlyList<string> tags, CancellationToken cancellationToken = default)
    {
        var selected = TagNormalizer.Normalize(tags).ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            foreach (var topic in _byTag.Values)
            {
                topic.IsSelected = selected.Contains(topic.Tag);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SelectedTopic>> GetSelectedAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<SelectedTopic> result = _byTag.Values
                .Where(topic => topic.IsSelected)
                .OrderBy(topic => topic.Tag, StringComparer.Ordinal)
                .Select(topic => new SelectedTopic(
                    topic.Tag, topic.Display is { Length: > 0 } display ? display : topic.Tag))
                .ToList();

            return Task.FromResult(result);
        }
    }

    /// <summary>更新内容を1行に写す(EF 版と同じ規則にするため共有する)。</summary>
    internal static void Apply(StoredTopic stored, TopicUpdate topic, DateTimeOffset collectedAt)
    {
        stored.Display = topic.Display;
        stored.Parent = topic.Parent;
        stored.TrendScore = topic.TrendScore;
        stored.SubtreeTrendScore = topic.SubtreeTrendScore;
        stored.SourceCount = topic.SourceCount;
        stored.ArticleCount = topic.ArticleCount;
        stored.EventCount = topic.EventCount;
        stored.BookCount = topic.BookCount;
        stored.CollectedAt = collectedAt;
    }
}

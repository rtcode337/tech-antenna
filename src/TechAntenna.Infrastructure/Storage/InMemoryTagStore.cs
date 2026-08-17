using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;

namespace TechAntenna.Infrastructure.Storage;

/// <summary>タグと仕分け状態のメモリ上の実装。接続文字列なしのお試し起動とテストで使う。</summary>
public class InMemoryTagStore : ITagStore
{
    readonly object _gate = new();
    readonly Dictionary<string, Tag> _byKey = new(StringComparer.Ordinal);

    public Task ObserveAsync(
        IReadOnlyList<TagObservation> observations,
        DateTimeOffset seenAt,
        bool resetMissing = false,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (resetMissing)
            {
                var seen = observations.Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
                foreach (var tag in _byKey.Values.Where(tag => !seen.Contains(tag.Key)))
                {
                    Reset(tag);
                }
            }

            foreach (var observation in observations)
            {
                if (!_byKey.TryGetValue(observation.Key, out var tag))
                {
                    tag = new Tag { Key = observation.Key, FirstSeenAt = seenAt };
                    _byKey[observation.Key] = tag;
                }

                Apply(tag, observation, seenAt);
            }
        }

        return Task.CompletedTask;
    }

    public Task DecideAsync(
        IReadOnlyList<TagDecision> decisions,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            foreach (var decision in decisions)
            {
                if (_byKey.TryGetValue(decision.Key, out var tag))
                {
                    Apply(tag, decision, decidedAt);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<Tag>>(_byKey.Values
                .OrderBy(tag => tag.Key, StringComparer.Ordinal)
                .ToList());
        }
    }

    public Task<IReadOnlyList<Tag>> GetPendingAsync(
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<Tag>>(_byKey.Values
                .Where(tag => IsPending(tag, now))
                .OrderByDescending(tag => tag.TotalCount + tag.TrendScore)
                .ThenBy(tag => tag.Key, StringComparer.Ordinal)
                .ToList());
        }
    }

    public Task<int> RemoveAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(keys.Count(key => _byKey.Remove(key)));
        }
    }

    /// <summary>
    /// まだ聞いてよい状態か(未仕分け、または**紐づくデータがある**保留の期限切れ)。
    /// EF 版と規則をそろえる —— ずれると「画面に出ている仕分け待ちの数」と
    /// 「実際に LLM へ流れる語」が食い違う。
    /// </summary>
    internal static bool IsPending(Tag tag, DateTimeOffset now) =>
        tag.Status == TagStatus.Pending
        || (tag.Status == TagStatus.Unresolved && tag.RetryAfter <= now && tag.TotalCount > 0);

    internal static void Reset(Tag tag)
    {
        tag.ArticleCount = 0;
        tag.EventCount = 0;
        tag.BookCount = 0;
        tag.TrendScore = 0;
        tag.SourceCount = 0;
    }

    internal static void Apply(Tag tag, TagObservation observation, DateTimeOffset seenAt)
    {
        tag.ArticleCount = observation.ArticleCount;
        tag.EventCount = observation.EventCount;
        tag.BookCount = observation.BookCount;
        tag.TrendScore = observation.TrendScore;
        tag.SourceCount = observation.SourceCount;
        tag.LastSeenAt = seenAt;
        if (tag.FirstSeenAt == default)
        {
            tag.FirstSeenAt = seenAt;
        }
    }

    internal static void Apply(Tag tag, TagDecision decision, DateTimeOffset decidedAt)
    {
        tag.Status = decision.Status;
        tag.TopicKey = decision.TopicKey;
        tag.DecidedBy = decision.DecidedBy;
        tag.DecidedAt = decidedAt;
        tag.RetryAfter = decision.RetryAfter;
    }
}

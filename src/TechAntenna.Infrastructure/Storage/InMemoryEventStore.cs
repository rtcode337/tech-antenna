using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Storage;

/// <summary>
/// メモリ上のイベントストア。DB 接続なしで動かすときのつなぎで、
/// プロセスを再起動すると消える。
/// </summary>
public class InMemoryEventStore : IEventStore
{
    readonly object _gate = new();
    readonly Dictionary<Uri, TechEvent> _byUrl = [];

    public Task<int> AddRangeAsync(IEnumerable<TechEvent> events, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var added = 0;
            foreach (var techEvent in events)
            {
                if (_byUrl.TryAdd(techEvent.Url, techEvent))
                {
                    added++;
                    continue;
                }

                // 既存のイベントは主催者と参加者数、購読・面掃きで入った理由だけ取り込む
                // (EfEventStore と同じ規則)
                var existing = _byUrl[techEvent.Url];
                existing.Organizer = techEvent.Organizer ?? existing.Organizer;
                existing.ParticipantCount = techEvent.ParticipantCount ?? existing.ParticipantCount;
                existing.PickedBy = techEvent.PickedBy ?? existing.PickedBy;
            }

            return Task.FromResult(added);
        }
    }

    public Task<IReadOnlyList<TechEvent>> GetUpcomingAsync(DateTimeOffset from, int count, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<TechEvent> result = _byUrl.Values
                .Where(e => e.StartsAt >= from)
                .OrderBy(e => e.StartsAt)
                .Take(count)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<TechEvent>> GetInRangeAsync(
        DateTimeOffset from, DateTimeOffset to, int count, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<TechEvent> result = _byUrl.Values
                .Where(e => e.StartsAt >= from && e.StartsAt < to)
                .OrderBy(e => e.StartsAt)
                .Take(count)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<int> UpdateMentionCountsAsync(
        IReadOnlyList<(Guid EventId, int Count)> counts, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var byId = _byUrl.Values.ToDictionary(e => e.Id);
            var updated = 0;
            foreach (var (eventId, count) in counts)
            {
                if (!byId.TryGetValue(eventId, out var techEvent) || techEvent.MentionCount == count)
                {
                    continue;
                }

                techEvent.MentionCount = count;
                updated++;
            }

            return Task.FromResult(updated);
        }
    }

    public Task<IReadOnlyList<OrganizerGroup>> GetOrganizerGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<OrganizerGroup> result = _byUrl.Values
                .Where(e => !string.IsNullOrWhiteSpace(e.Organizer))
                .GroupBy(e => (e.Organizer!, e.SourceName))
                .Select(g => new OrganizerGroup(
                    g.Key.Item1, g.Key.SourceName, g.First().Url, g.Count()))
                .OrderByDescending(g => g.Count)
                .ThenBy(g => g.Organizer, StringComparer.Ordinal)
                .ToList();

            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<OrganizerCount>> GetOrganizerCountsAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<OrganizerCount> result = _byUrl.Values
                .Where(e => !string.IsNullOrWhiteSpace(e.Organizer))
                .GroupBy(e => e.Organizer!, StringComparer.Ordinal)
                .Select(g => new OrganizerCount(g.Key, g.Count()))
                .OrderByDescending(o => o.Count)
                .ThenBy(o => o.Organizer, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<TechEvent>> GetByTagAsync(string tag, int count, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<TechEvent> result = _byUrl.Values
                .Where(e => e.Tags.Contains(tag))
                .OrderBy(e => e.StartsAt)
                .Take(count)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<TagCount>> GetTagCountsAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<TagCount> result = _byUrl.Values
                .SelectMany(e => e.Tags)
                .GroupBy(tag => tag, StringComparer.Ordinal)
                .Select(g => new TagCount(g.Key, g.Count()))
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<int> RenormalizeTagsAsync(TopicCatalog catalog, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var updated = 0;
            foreach (var techEvent in _byUrl.Values)
            {
                var tags = catalog.Normalize(techEvent.RawTags);
                if (techEvent.Tags.SequenceEqual(tags, StringComparer.Ordinal))
                {
                    continue;
                }

                techEvent.Tags = tags;
                updated++;
            }

            return Task.FromResult(updated);
        }
    }
}

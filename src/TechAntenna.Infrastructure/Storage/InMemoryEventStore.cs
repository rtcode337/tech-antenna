using TechAntenna.Core.Abstractions;
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
                }
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
}

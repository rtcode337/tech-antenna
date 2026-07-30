using Microsoft.EntityFrameworkCore;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Persistence;

/// <summary>PostgreSQL に保存するイベントストア。</summary>
public class EfEventStore(IDbContextFactory<TechAntennaDbContext> contextFactory) : IEventStore
{
    public async Task<int> AddRangeAsync(IEnumerable<TechEvent> events, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var incoming = events.DistinctBy(e => e.Url).ToList();
        var urls = incoming.Select(e => e.Url).ToList();
        var existingUrls = await db.Events
            .Where(e => urls.Contains(e.Url))
            .Select(e => e.Url)
            .ToListAsync(cancellationToken);

        var newEvents = incoming.Where(e => !existingUrls.Contains(e.Url)).ToList();
        db.Events.AddRange(newEvents);
        await db.SaveChangesAsync(cancellationToken);
        return newEvents.Count;
    }

    public async Task<IReadOnlyList<TechEvent>> GetUpcomingAsync(DateTimeOffset from, int count, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Events
            .Where(e => e.StartsAt >= from)
            .OrderBy(e => e.StartsAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }
}

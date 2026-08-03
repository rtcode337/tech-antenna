using Microsoft.EntityFrameworkCore;
using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
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

    // タグ関連が生 SQL である理由は EfArticleStore を参照
    public async Task<IReadOnlyList<TechEvent>> GetByTagAsync(string tag, int count, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Events
            .FromSql($"""SELECT * FROM "Events" WHERE "Tags" @> ARRAY[{tag}]::text[]""")
            .OrderBy(e => e.StartsAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TagCount>> GetTagCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Database
            .SqlQuery<TagCount>(
                $"""SELECT unnest("Tags") AS "Tag", COUNT(*)::int AS "Count" FROM "Events" GROUP BY 1""")
            .ToListAsync(cancellationToken);
    }

    public async Task<int> RenormalizeTagsAsync(TopicCatalog catalog, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // 全件を読み直す。個人運用の規模(数千件)を前提にページングはしていない
        var updated = 0;
        foreach (var techEvent in await db.Events.ToListAsync(cancellationToken))
        {
            var tags = catalog.Normalize(techEvent.RawTags);
            if (techEvent.Tags.SequenceEqual(tags, StringComparer.Ordinal))
            {
                continue;
            }

            techEvent.Tags = tags;
            updated++;
        }

        if (updated > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return updated;
    }
}

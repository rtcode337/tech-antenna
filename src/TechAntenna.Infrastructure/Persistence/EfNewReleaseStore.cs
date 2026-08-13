using Microsoft.EntityFrameworkCore;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Persistence;

/// <summary>PostgreSQL に保存する新刊ストア(出版のテーマを数えるための観測)。</summary>
public class EfNewReleaseStore(IDbContextFactory<TechAntennaDbContext> contextFactory)
    : INewReleaseStore
{
    public async Task<int> AddRangeAsync(
        IEnumerable<NewRelease> releases, CancellationToken cancellationToken = default)
    {
        var incoming = releases
            .GroupBy(release => release.Url)
            .Select(group => group.Last())
            .ToList();

        if (incoming.Count == 0)
        {
            return 0;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var urls = incoming.Select(release => release.Url).ToList();
        var stored = await db.NewReleases
            .Where(release => urls.Contains(release.Url))
            .ToDictionaryAsync(release => release.Url, cancellationToken);

        var added = 0;
        foreach (var release in incoming)
        {
            if (stored.TryGetValue(release.Url, out var existing))
            {
                // **観測なので上書きする**(読ませるための行ではないので、最新の見え方に揃える)
                existing.Tags = release.Tags;
                existing.RawTags = release.RawTags;
                continue;
            }

            db.NewReleases.Add(release);
            added++;
        }

        await db.SaveChangesAsync(cancellationToken);

        return added;
    }

    public async Task<IReadOnlyList<NewRelease>> GetPublishedSinceAsync(
        DateOnly since, int count, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.NewReleases
            .Where(release => release.PublishedOn != null && release.PublishedOn >= since)
            .OrderByDescending(release => release.PublishedOn)
            .ThenByDescending(release => release.CollectedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }
}

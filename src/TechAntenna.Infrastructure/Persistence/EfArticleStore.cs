using Microsoft.EntityFrameworkCore;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Persistence;

/// <summary>PostgreSQL に保存する記事ストア。</summary>
public class EfArticleStore(IDbContextFactory<TechAntennaDbContext> contextFactory) : IArticleStore
{
    public async Task<int> AddRangeAsync(IEnumerable<Article> articles, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var incoming = articles.DistinctBy(a => a.Url).ToList();
        var urls = incoming.Select(a => a.Url).ToList();
        var existingUrls = await db.Articles
            .Where(a => urls.Contains(a.Url))
            .Select(a => a.Url)
            .ToListAsync(cancellationToken);

        var newArticles = incoming.Where(a => !existingUrls.Contains(a.Url)).ToList();
        db.Articles.AddRange(newArticles);
        await db.SaveChangesAsync(cancellationToken);
        return newArticles.Count;
    }

    public async Task<IReadOnlyList<Article>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Articles
            .OrderByDescending(a => a.PublishedAt ?? a.CollectedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Article>> GetUnsummarizedAsync(int count, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Articles
            .Where(a => a.Summary == null)
            .OrderByDescending(a => a.PublishedAt ?? a.CollectedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateSummaryAsync(Guid articleId, string summary, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        await db.Articles
            .Where(a => a.Id == articleId)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.Summary, summary), cancellationToken);
    }
}

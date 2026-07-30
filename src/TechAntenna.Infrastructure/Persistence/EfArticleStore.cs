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

    // Tags には値変換をかけているため LINQ からは翻訳できず、タグごとの件数集計には
    // unnest が要る。そのためタグ関連の2つだけ生 SQL で書いている(tag は
    // FormattableString 越しにパラメーター化される)。イベント・書籍のストアも同様。
    public async Task<IReadOnlyList<Article>> GetByTagAsync(string tag, int count, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Articles
            .FromSql($"""SELECT * FROM "Articles" WHERE "Tags" @> ARRAY[{tag}]::text[]""")
            .OrderByDescending(a => a.PublishedAt ?? a.CollectedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TagCount>> GetTagCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Database
            .SqlQuery<TagCount>(
                $"""SELECT unnest("Tags") AS "Tag", COUNT(*)::int AS "Count" FROM "Articles" GROUP BY 1""")
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

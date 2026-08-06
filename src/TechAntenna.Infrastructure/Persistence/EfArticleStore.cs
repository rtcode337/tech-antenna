using Microsoft.EntityFrameworkCore;
using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
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

    public async Task<IReadOnlyList<Article>> GetRecentAsync(
        int count, ArticleKind? kind = null, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Articles
            .Where(a => kind == null || a.Kind == kind)
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
            // 論文は本文を取り込んでいないので要約しない
            .Where(a => a.Summary == null && a.Kind != ArticleKind.Paper)
            .OrderByDescending(a => a.PublishedAt ?? a.CollectedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Article>> GetUntranslatedPapersAsync(
        int count, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Articles
            .Where(a => a.Kind == ArticleKind.Paper && a.TitleJa == null)
            .OrderByDescending(a => a.PublishedAt ?? a.CollectedAt)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateTitleJaAsync(Guid articleId, string titleJa, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        await db.Articles
            .Where(a => a.Id == articleId)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.TitleJa, titleJa), cancellationToken);
    }

    public async Task<int> UpdateBookmarkCountsAsync(
        IReadOnlyList<(Guid ArticleId, int Count)> counts, CancellationToken cancellationToken = default)
    {
        if (counts.Count == 0)
        {
            return 0;
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var ids = counts.Select(pair => pair.ArticleId).ToList();
        var articles = await db.Articles
            .Where(a => ids.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        var updated = 0;
        foreach (var (articleId, count) in counts)
        {
            if (articles.TryGetValue(articleId, out var article) && article.BookmarkCount != count)
            {
                article.BookmarkCount = count;
                updated++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return updated;
    }

    public async Task UpdateSummaryAsync(Guid articleId, string summary, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        await db.Articles
            .Where(a => a.Id == articleId)
            .ExecuteUpdateAsync(set => set.SetProperty(a => a.Summary, summary), cancellationToken);
    }

    public async Task<int> RenormalizeTagsAsync(TopicCatalog catalog, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // 全件を読み直す。個人運用の規模(数千件)を前提にページングはしていない
        var updated = 0;
        foreach (var article in await db.Articles.ToListAsync(cancellationToken))
        {
            // 収集時と同じ規則で作り直す(生タグ + タイトルから見つけたトピック)。
            // タイトルの分も入れないと、この規則を入れる前に集めた記事がタグ無しのまま残る
            var tags = catalog.Normalize(article.RawTags.Concat(catalog.FindIn(article.Title)));
            if (article.Tags.SequenceEqual(tags, StringComparer.Ordinal))
            {
                continue;
            }

            article.Tags = tags;
            updated++;
        }

        if (updated > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return updated;
    }
}

using Microsoft.EntityFrameworkCore;
using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Persistence;

/// <summary>PostgreSQL に保存する書籍ストア。</summary>
public class EfBookStore(IDbContextFactory<TechAntennaDbContext> contextFactory) : IBookStore
{
    public async Task<int> AddRangeAsync(IEnumerable<Book> books, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var incoming = BookMerge.Coalesce(books);
        var keys = incoming.Select(BookKey.For).ToList();

        // 重複判定キーはドメインモデルに持たせず、EF のシャドウプロパティとして列に持つ。
        // タグを足すために、キーだけでなく行そのものを読む(追跡された実体を書き換える)
        var existing = await db.Books
            .Where(b => keys.Contains(EF.Property<string>(b, TechAntennaDbContext.BookDedupKey)))
            .ToListAsync(cancellationToken);
        // 列の値は BookKey.For で書いているので、読み直した行からも同じキーを作れる
        var byKey = existing.ToDictionary(BookKey.For, StringComparer.Ordinal);

        var added = 0;
        foreach (var book in incoming)
        {
            var key = BookKey.For(book);
            if (byKey.TryGetValue(key, out var stored))
            {
                // 既にある本は書誌情報を上書きせず、別のトピックで見つかったぶんのタグだけ足す
                BookMerge.MergeTags(stored, book);
                continue;
            }

            var entry = db.Books.Add(book);
            entry.Property(TechAntennaDbContext.BookDedupKey).CurrentValue = key;
            added++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return added;
    }

    public async Task<IReadOnlyList<Book>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Books
            .OrderByDescending(b => b.CollectedAt)
            .ThenByDescending(b => b.PublishedOn)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    // タグ関連が生 SQL である理由は EfArticleStore を参照
    public async Task<IReadOnlyList<Book>> GetByTagAsync(string tag, int count, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Books
            .FromSql($"""SELECT * FROM "Books" WHERE "Tags" @> ARRAY[{tag}]::text[]""")
            .OrderByDescending(b => b.CollectedAt)
            .ThenByDescending(b => b.PublishedOn)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TagCount>> GetTagCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Database
            .SqlQuery<TagCount>(
                $"""SELECT unnest("Tags") AS "Tag", COUNT(*)::int AS "Count" FROM "Books" GROUP BY 1""")
            .ToListAsync(cancellationToken);
    }

    public async Task<int> RenormalizeTagsAsync(TopicCatalog catalog, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // 全件を読み直す。個人運用の規模(数千件)を前提にページングはしていない
        var updated = 0;
        foreach (var book in await db.Books.ToListAsync(cancellationToken))
        {
            var tags = catalog.Normalize(book.RawTags);
            if (book.Tags.SequenceEqual(tags, StringComparer.Ordinal))
            {
                continue;
            }

            book.Tags = tags;
            updated++;
        }

        if (updated > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return updated;
    }
}

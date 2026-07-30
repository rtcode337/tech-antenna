using Microsoft.EntityFrameworkCore;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Persistence;

/// <summary>PostgreSQL に保存する書籍ストア。</summary>
public class EfBookStore(IDbContextFactory<TechAntennaDbContext> contextFactory) : IBookStore
{
    public async Task<int> AddRangeAsync(IEnumerable<Book> books, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var incoming = books.DistinctBy(BookKey.For).ToList();
        var keys = incoming.Select(BookKey.For).ToList();

        // 重複判定キーはドメインモデルに持たせず、EF のシャドウプロパティとして列に持つ
        var existingKeys = await db.Books
            .Select(b => EF.Property<string>(b, TechAntennaDbContext.BookDedupKey))
            .Where(key => keys.Contains(key))
            .ToListAsync(cancellationToken);

        var added = 0;
        foreach (var book in incoming)
        {
            var key = BookKey.For(book);
            if (existingKeys.Contains(key))
            {
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
}

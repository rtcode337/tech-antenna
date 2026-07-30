using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Storage;

/// <summary>
/// メモリ上の書籍ストア。DB 接続なしで動かすときのつなぎで、
/// プロセスを再起動すると消える。
/// </summary>
public class InMemoryBookStore : IBookStore
{
    readonly object _gate = new();
    readonly Dictionary<string, Book> _byKey = [];

    public Task<int> AddRangeAsync(IEnumerable<Book> books, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var added = 0;
            foreach (var book in books)
            {
                if (_byKey.TryAdd(BookKey.For(book), book))
                {
                    added++;
                }
            }

            return Task.FromResult(added);
        }
    }

    public Task<IReadOnlyList<Book>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<Book> result = _byKey.Values
                .OrderByDescending(b => b.CollectedAt)
                .ThenByDescending(b => b.PublishedOn)
                .Take(count)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<Book>> GetByTagAsync(string tag, int count, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<Book> result = _byKey.Values
                .Where(b => b.Tags.Contains(tag))
                .OrderByDescending(b => b.CollectedAt)
                .ThenByDescending(b => b.PublishedOn)
                .Take(count)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<TagCount>> GetTagCountsAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<TagCount> result = _byKey.Values
                .SelectMany(b => b.Tags)
                .GroupBy(tag => tag, StringComparer.Ordinal)
                .Select(g => new TagCount(g.Key, g.Count()))
                .ToList();
            return Task.FromResult(result);
        }
    }
}

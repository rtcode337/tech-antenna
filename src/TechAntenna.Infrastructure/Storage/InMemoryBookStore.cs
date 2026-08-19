using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
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
            foreach (var book in BookMerge.Coalesce(books))
            {
                var key = BookKey.For(book);
                if (_byKey.TryGetValue(key, out var stored))
                {
                    // 既にある本は書誌情報を上書きせず、タグ・レビュー・欠けていた書影だけ取り込む
                    BookMerge.Merge(stored, book);
                    continue;
                }

                _byKey[key] = book;
                added++;
            }

            return Task.FromResult(added);
        }
    }

    public Task<bool?> ToggleReadAsync(
        Guid id, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var book = _byKey.Values.FirstOrDefault(b => b.Id == id);
            if (book is null)
            {
                return Task.FromResult<bool?>(null);
            }

            book.ReadAt = book.IsRead ? null : now;

            return Task.FromResult<bool?>(book.IsRead);
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

    public Task<int> RenormalizeTagsAsync(TopicCatalog catalog, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var updated = 0;
            foreach (var book in _byKey.Values)
            {
                // 記事と同じ規則(生タグ + タイトルから見つけたトピック)。**推薦(定番)で
            // 拾った本は RawTags が空**(検索キーワードが無い)なので、タイトルの分が
            // 無いとタグがずっと空のまま —— トピックの詳細にも /books の分類にも乗らない
            var tags = catalog.Normalize(book.RawTags.Concat(catalog.FindIn(book.Title)));
                if (book.Tags.SequenceEqual(tags, StringComparer.Ordinal))
                {
                    continue;
                }

                book.Tags = tags;
                updated++;
            }

            return Task.FromResult(updated);
        }
    }
}

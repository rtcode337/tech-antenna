using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Storage;

/// <summary>
/// メモリ上の記事ストア。DB(PostgreSQL + EF Core)導入までのつなぎで、
/// プロセスを再起動すると消える。
/// </summary>
public class InMemoryArticleStore : IArticleStore
{
    readonly object _gate = new();
    readonly Dictionary<Uri, Article> _byUrl = [];

    public Task<int> AddRangeAsync(IEnumerable<Article> articles, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var added = 0;
            foreach (var article in articles)
            {
                if (_byUrl.TryAdd(article.Url, article))
                {
                    added++;
                }
            }

            return Task.FromResult(added);
        }
    }

    public Task<IReadOnlyList<Article>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<Article> result = _byUrl.Values
                .OrderByDescending(a => a.PublishedAt ?? a.CollectedAt)
                .Take(count)
                .ToList();
            return Task.FromResult(result);
        }
    }
}

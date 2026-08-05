using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
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

    public Task<IReadOnlyList<Article>> GetRecentAsync(
        int count, ArticleKind? kind = null, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<Article> result = _byUrl.Values
                .Where(a => kind is null || a.Kind == kind)
                .OrderByDescending(a => a.PublishedAt ?? a.CollectedAt)
                .Take(count)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<Article>> GetUnsummarizedAsync(int count, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<Article> result = _byUrl.Values
                // 論文は本文を取り込んでいないので要約しない
                .Where(a => a.Summary is null && a.Kind != ArticleKind.Paper)
                .OrderByDescending(a => a.PublishedAt ?? a.CollectedAt)
                .Take(count)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<Article>> GetByTagAsync(string tag, int count, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<Article> result = _byUrl.Values
                .Where(a => a.Tags.Contains(tag))
                .OrderByDescending(a => a.PublishedAt ?? a.CollectedAt)
                .Take(count)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<TagCount>> GetTagCountsAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<TagCount> result = _byUrl.Values
                .SelectMany(a => a.Tags)
                .GroupBy(tag => tag, StringComparer.Ordinal)
                .Select(g => new TagCount(g.Key, g.Count()))
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task<IReadOnlyList<Article>> GetUntranslatedPapersAsync(
        int count, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<Article> result = _byUrl.Values
                .Where(a => a.Kind == ArticleKind.Paper && a.TitleJa is null)
                .OrderByDescending(a => a.PublishedAt ?? a.CollectedAt)
                .Take(count)
                .ToList();
            return Task.FromResult(result);
        }
    }

    public Task UpdateTitleJaAsync(Guid articleId, string titleJa, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var article = _byUrl.Values.FirstOrDefault(a => a.Id == articleId);
            if (article is not null)
            {
                article.TitleJa = titleJa;
            }

            return Task.CompletedTask;
        }
    }

    public Task UpdateSummaryAsync(Guid articleId, string summary, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var article = _byUrl.Values.FirstOrDefault(a => a.Id == articleId);
            if (article is not null)
            {
                article.Summary = summary;
            }

            return Task.CompletedTask;
        }
    }

    public Task<int> RenormalizeTagsAsync(TopicCatalog catalog, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var updated = 0;
            foreach (var article in _byUrl.Values)
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

            return Task.FromResult(updated);
        }
    }
}

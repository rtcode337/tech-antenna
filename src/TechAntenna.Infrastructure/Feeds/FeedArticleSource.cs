using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Feeds;

/// <summary>1本の RSS / Atom フィードを読む記事ソース。フィードごとに1インスタンス登録する。</summary>
public class FeedArticleSource(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    string name,
    Uri feedUrl) : IArticleSource
{
    /// <summary>使用する名前付き HttpClient。User-Agent 等はホスト側の登録で設定する。</summary>
    public const string HttpClientName = "feeds";

    public string Name => name;

    public async Task<IReadOnlyList<Article>> FetchAsync(CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);
        var xml = await client.GetStringAsync(feedUrl, cancellationToken);

        var collectedAt = timeProvider.GetUtcNow();
        return FeedParser.Parse(xml)
            .Select(entry => new Article
            {
                Title = entry.Title,
                Url = entry.Url,
                SourceName = name,
                PublishedAt = entry.PublishedAt,
                CollectedAt = collectedAt,
                Tags = TagNormalizer.Normalize(entry.Tags),
            })
            .ToList();
    }
}

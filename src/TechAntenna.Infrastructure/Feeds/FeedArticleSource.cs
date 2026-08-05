using TechAntenna.Core;
using TechAntenna.Core.Topics;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Feeds;

/// <summary>1本の RSS / Atom フィードを読む記事ソース。フィードごとに1インスタンス登録する。</summary>
public class FeedArticleSource(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    string name,
    Uri feedUrl,
    TopicCatalog? catalog = null,
    ArticleKind kind = ArticleKind.Article) : IArticleSource
{
    /// <summary>使用する名前付き HttpClient。User-Agent 等はホスト側の登録で設定する。</summary>
    public const string HttpClientName = "feeds";

    public string Name => name;

    public async Task<IReadOnlyList<Article>> FetchAsync(CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);
        var xml = await client.GetStringAsync(feedUrl, cancellationToken);

        var topics = catalog ?? TopicCatalog.Empty;
        var collectedAt = timeProvider.GetUtcNow();

        return FeedParser.Parse(xml)
            .Select(entry =>
            {
                // 収集元のタグに、**タイトルから見つけたトピック**を足す。
                // Zenn の RSS も Qiita の Atom も category を持たず、ニュースサイトも同様なので、
                // 収集元のタグだけに頼るとタグ無しで保存され、トピック横断にも強調にも乗らない
                var rawTags = entry.Tags.Concat(topics.FindIn(entry.Title))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new Article
                {
                    Title = entry.Title,
                    Url = entry.Url,
                    SourceName = name,
                    Kind = kind,
                    ContentSnippet = entry.Summary,
                    PublishedAt = entry.PublishedAt,
                    CollectedAt = collectedAt,
                    Tags = topics.Normalize(rawTags),
                    RawTags = rawTags,
                };
            })
            .ToList();
    }
}

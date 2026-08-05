using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;

namespace TechAntenna.Infrastructure.Feeds;

/// <summary>
/// arXiv の API で論文を集める。**選択中のトピックを検索語にして1つずつ問い合わせ**、
/// 検索に使ったキーワードをそのままタグにする(connpass・書籍と同じやり方)。
/// arXiv 側の分類(`cs.CL` 等)はタグにしない —— トピック横断の語彙と噛み合わないため。
///
/// 取り込むのは**タイトル・URL・投稿日だけ**。abstract は著者の文章なので取り込まない
/// (書籍で書誌事実だけを取り込むのと同じ方針)。本文が無いので要約ジョブの対象からも
/// 外してある(<see cref="ArticleKind.Paper"/>)。
///
/// **リクエストの間隔は 3 秒以上空ける。** arXiv の API 利用条件が求めている下限で、
/// 無料で公開されている学術インフラなので守ること。
/// </summary>
public class ArxivPaperSource(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    ITopicStore topicStore,
    TopicCatalog? catalog = null,
    int maxResults = 20,
    TimeSpan? delayBetweenKeywords = null) : IArticleSource
{
    public const string HttpClientName = "arxiv";

    // http:// は 301 を返すので https で叩く
    const string EndpointFormat =
        "https://export.arxiv.org/api/query?search_query=all:%22{0}%22"
        + "&start=0&max_results={1}&sortBy=submittedDate&sortOrder=descending";

    readonly TimeSpan _delay = delayBetweenKeywords ?? TimeSpan.FromSeconds(3);

    public string Name => "arXiv";

    public async Task<IReadOnlyList<Article>> FetchAsync(CancellationToken cancellationToken = default)
    {
        var topics = catalog ?? TopicCatalog.Empty;

        // **検索は英語表記、タグは正式表記。** arXiv は英語の索引なので `生成AI` をそのまま
        // 投げると 0 件になる(実測)。カタログの英語別名(`generative ai`)で引いて、
        // 付けるタグは他の収集元と揃うよう正式表記のままにする
        var keywords = (await topicStore.GetSelectedAsync(cancellationToken))
            .Select(topic => (Query: topics.EnglishTermOf(topic.Tag), Tag: topic.Display))
            .ToList();

        if (keywords.Count == 0)
        {
            return [];
        }

        using var client = httpClientFactory.CreateClient(HttpClientName);
        var found = new Dictionary<Uri, (FeedEntry Entry, List<string> Keywords)>();

        for (var i = 0; i < keywords.Count; i++)
        {
            var keyword = keywords[i].Tag;
            var requestUri = string.Format(
                EndpointFormat, Uri.EscapeDataString(keywords[i].Query), maxResults);
            var xml = await client.GetStringAsync(requestUri, cancellationToken);

            foreach (var entry in FeedParser.Parse(xml))
            {
                if (found.TryGetValue(entry.Url, out var already))
                {
                    // 同じ論文が複数のトピックで見つかったらタグを足す(捨てると片方に出ない)
                    if (!already.Keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase))
                    {
                        already.Keywords.Add(keyword);
                    }

                    continue;
                }

                found[entry.Url] = (entry, [keyword]);
            }

            // 最後のキーワードの後は待たない(手動実行で無駄に待たせないため)
            if (i < keywords.Count - 1 && _delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }
        }

        var collectedAt = timeProvider.GetUtcNow();

        return found.Values
            .Select(item => new Article
            {
                Title = item.Entry.Title,
                Url = item.Entry.Url,
                SourceName = Name,
                Kind = ArticleKind.Paper,
                ContentSnippet = null,
                PublishedAt = item.Entry.PublishedAt,
                CollectedAt = collectedAt,
                Tags = topics.Normalize(item.Keywords),
                RawTags = item.Keywords,
            })
            .ToList();
    }
}

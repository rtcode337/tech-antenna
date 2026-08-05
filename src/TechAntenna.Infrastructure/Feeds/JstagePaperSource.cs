using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;

namespace TechAntenna.Infrastructure.Feeds;

/// <summary>
/// J-STAGE(科学技術情報発信・流通総合システム)で**日本語の論文**を集める。
/// arXiv と同じく選択中のトピックを検索語にして1つずつ問い合わせ、検索語をタグにする。
///
/// **arXiv と違って検索語は日本語のまま**投げる —— J-STAGE は和文の索引なので、
/// 英語別名に置き換える必要が無い。取れるタイトルも和文なので翻訳も要らない。
///
/// 取り込むのは**タイトル・URL・公開日と掲載誌名だけ**。抄録は著者の文章なので取り込まない。
/// </summary>
public class JstagePaperSource(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    ITopicStore topicStore,
    TopicCatalog? catalog = null,
    int maxResults = 20,
    int withinYears = 2,
    TimeSpan? delayBetweenKeywords = null) : IArticleSource
{
    public const string HttpClientName = "jstage";

    const string EndpointFormat =
        "https://api.jstage.jst.go.jp/searchapi/do?service=3&text={0}&pubyearfrom={1}&count={2}";

    readonly TimeSpan _delay = delayBetweenKeywords ?? TimeSpan.FromSeconds(3);

    public string Name => "J-STAGE";

    public async Task<IReadOnlyList<Article>> FetchAsync(CancellationToken cancellationToken = default)
    {
        // 検索語には**正式表記のほう**が要る(正規化で崩れたキー `生成ai` を投げても当たらない)
        var keywords = (await topicStore.GetSelectedAsync(cancellationToken))
            .Select(topic => topic.Display)
            .ToList();

        if (keywords.Count == 0)
        {
            return [];
        }

        using var client = httpClientFactory.CreateClient(HttpClientName);
        // 古い論文まで拾うと一覧が埋まるので、直近の数年に絞る
        var from = timeProvider.GetUtcNow().Year - Math.Max(0, withinYears - 1);
        var found = new Dictionary<Uri, (JstageArticle Paper, List<string> Keywords)>();

        for (var i = 0; i < keywords.Count; i++)
        {
            var keyword = keywords[i];
            var requestUri = string.Format(
                EndpointFormat, Uri.EscapeDataString(keyword), from, maxResults);
            var xml = await client.GetStringAsync(requestUri, cancellationToken);

            foreach (var paper in JstageResponseParser.Parse(xml))
            {
                if (found.TryGetValue(paper.Url, out var already))
                {
                    // 同じ論文が複数のトピックで見つかったらタグを足す(捨てると片方に出ない)
                    if (!already.Keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase))
                    {
                        already.Keywords.Add(keyword);
                    }

                    continue;
                }

                found[paper.Url] = (paper, [keyword]);
            }

            // 最後のキーワードの後は待たない(手動実行で無駄に待たせないため)
            if (i < keywords.Count - 1 && _delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }
        }

        var topics = catalog ?? TopicCatalog.Empty;
        var collectedAt = timeProvider.GetUtcNow();

        return found.Values
            .Select(item => new Article
            {
                Title = item.Paper.Title,
                Url = item.Paper.Url,
                // 掲載誌が分かるほうが論文らしさが伝わるので、収集元名に添える
                SourceName = item.Paper.JournalTitle is { Length: > 0 } journal
                    ? $"{Name} / {journal}"
                    : Name,
                Kind = ArticleKind.Paper,
                ContentSnippet = null,
                PublishedAt = item.Paper.PublishedAt,
                CollectedAt = collectedAt,
                Tags = topics.Normalize(item.Keywords),
                RawTags = item.Keywords,
            })
            .ToList();
    }
}

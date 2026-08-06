using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Books;

/// <summary>
/// Qiita の「おすすめ技術書まとめ」系の記事から、薦められている本を拾う。
///
/// **公式 API(v2)を使う。** レンダリング済み HTML を掻き集めるより壊れにくく、
/// 検索が本文まで返すので記事ごとに引き直さなくてよい。
/// 未認証は 60 リクエスト/時、アクセストークンを設定すると 1000 リクエスト/時。
///
/// **クエリは複数指定でき、1クエリずつページングで読む**。検索は新着順に返るため、
/// 1ページで打ち切ると古い定番記事(「読むべき本」まとめの多くはこちら)が読めない。
/// 同じ記事が複数のクエリに当たっても、URL で重複を落として1票に数える。
///
/// 本の特定は**記事に貼られた Amazon リンクの ASIN**から。書籍の ASIN は ISBN-10 そのものなので
/// ISBN-13 に直して書誌を引ける(<see cref="Isbn.FromAsin"/> がチェックディジットまで
/// 検算するので、`B0…` で始まる Kindle 専売などは落ちる)。この検算がノイズを絞るので、
/// タグの付いていない記事まで当たる本文検索のクエリを混ぜても、関係ない記事は自然に落ちる。
///
/// **保存するのは ISBN と出典記事の URL だけ**で、記事本文は保存しない。
/// </summary>
public partial class QiitaBookRecommendationSource(
    IHttpClientFactory httpClientFactory,
    IReadOnlyList<string> queries,
    int maxArticlesPerQuery = 200,
    string accessToken = "",
    TimeSpan? delayBetweenRequests = null) : IBookRecommendationSource
{
    public const string HttpClientName = "qiita";

    /// <summary>
    /// 1ページの記事数。API の上限は 100 だが、本文込みの応答が大きいので半分に抑える
    /// (HttpClient に応答サイズの上限を掛けているため、大きすぎると正常な応答まで落ちる)。
    /// </summary>
    const int PageSize = 50;

    readonly TimeSpan _delay = delayBetweenRequests ?? TimeSpan.FromSeconds(1);

    public string Name => "Qiita";

    /// <summary>
    /// Amazon の商品リンク。`/dp/ASIN`・`/gp/product/ASIN`・`/exec/obidos/ASIN/ASIN` の
    /// どの書き方でも拾えるようにしてある(記事によってまちまちなため)。
    /// </summary>
    [GeneratedRegex(@"amazon\.co\.jp/(?:[^/\s)""]+/)*(?:dp|gp/product|exec/obidos/ASIN)/([0-9A-Za-z]{10})")]
    private static partial Regex AmazonLink();

    public async Task<IReadOnlyList<BookRecommendation>> FetchAsync(
        CancellationToken cancellationToken = default)
    {
        var effectiveQueries = queries.Where(q => !string.IsNullOrWhiteSpace(q)).ToList();
        if (effectiveQueries.Count == 0)
        {
            return [];
        }

        using var client = httpClientFactory.CreateClient(HttpClientName);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        // 記事の URL → 本文。クエリをまたいで同じ記事を1度だけ数えるための入れ物
        var articles = new Dictionary<string, string>(StringComparer.Ordinal);
        var firstRequest = true;

        foreach (var query in effectiveQueries)
        {
            var fetched = 0;
            // Qiita の page は 1〜100
            for (var page = 1; page <= 100 && fetched < maxArticlesPerQuery; page++)
            {
                var perPage = Math.Min(PageSize, maxArticlesPerQuery - fetched);
                var requestUri = "https://qiita.com/api/v2/items"
                    + $"?query={Uri.EscapeDataString(query)}&per_page={perPage}&page={page}";

                // 無料でコミュニティに開かれている API なので、リクエストの間隔を空ける
                if (!firstRequest && _delay > TimeSpan.Zero)
                {
                    await Task.Delay(_delay, cancellationToken);
                }
                firstRequest = false;

                var json = await client.GetStringAsync(requestUri, cancellationToken);
                var count = CollectArticles(json, articles);

                fetched += count;
                if (count < perPage)
                {
                    break; // 最終ページまで読んだ
                }
            }
        }

        // ISBN ごとに、それを薦めていた記事の URL を集める(記事1本 = 1票)
        var byIsbn = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (url, body) in articles)
        {
            // 同じ記事で同じ本が何度出てきても1票(Distinct)
            foreach (var isbn in AmazonLink().Matches(body)
                .Select(match => Isbn.FromAsin(match.Groups[1].Value))
                .OfType<string>()
                .Distinct(StringComparer.Ordinal))
            {
                if (!byIsbn.TryGetValue(isbn, out var urls))
                {
                    urls = [];
                    byIsbn[isbn] = urls;
                }

                urls.Add(url);
            }
        }

        return byIsbn
            .Select(pair => new BookRecommendation(pair.Key, pair.Value))
            .ToList();
    }

    /// <summary>1ページ分の記事を取り込み、そのページに載っていた記事数を返す(重複込み)。</summary>
    static int CollectArticles(string json, Dictionary<string, string> articles)
    {
        var count = 0;

        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            count++;

            var body = GetString(item, "body");
            var url = GetString(item, "url");
            // 出典記事の URL は /books の href に出るため、http/https 以外は取り込まない
            if (body is null || url is null || !WebUrl.TryCreate(url, out _))
            {
                continue;
            }

            articles.TryAdd(url, body);
        }

        return count;
    }

    static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

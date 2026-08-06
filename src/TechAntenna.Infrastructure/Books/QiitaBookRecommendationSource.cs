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
/// 検索1回で本文まで返ってくるのでリクエストも 1 回で済む。
/// 未認証は 60 リクエスト/時、アクセストークンを設定すると 1000 リクエスト/時。
///
/// 本の特定は**記事に貼られた Amazon リンクの ASIN**から。書籍の ASIN は ISBN-10 そのものなので
/// ISBN-13 に直して書誌を引ける(<see cref="Isbn.FromAsin"/> がチェックディジットまで
/// 検算するので、`B0…` で始まる Kindle 専売などは落ちる)。
///
/// **保存するのは ISBN と出典記事の URL だけ**で、記事本文は保存しない。
/// </summary>
public partial class QiitaBookRecommendationSource(
    IHttpClientFactory httpClientFactory,
    string query,
    int maxArticles = 20,
    string accessToken = "") : IBookRecommendationSource
{
    public const string HttpClientName = "qiita";

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
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        using var client = httpClientFactory.CreateClient(HttpClientName);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        var requestUri = "https://qiita.com/api/v2/items"
            + $"?query={Uri.EscapeDataString(query)}&per_page={maxArticles}";
        var json = await client.GetStringAsync(requestUri, cancellationToken);

        // ISBN ごとに、それを薦めていた記事の URL を集める(記事1本 = 1票)
        var byIsbn = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var body = GetString(item, "body");
            var url = GetString(item, "url");
            // 出典記事の URL は /books の href に出るため、http/https 以外は取り込まない
            if (body is null || url is null || !WebUrl.TryCreate(url, out _))
            {
                continue;
            }

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

    static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

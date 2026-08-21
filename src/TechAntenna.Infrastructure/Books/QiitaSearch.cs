using System.Net.Http.Headers;
using System.Text.Json;
using TechAntenna.Core;

namespace TechAntenna.Infrastructure.Books;

/// <summary>
/// 検索で読んだ記事1本。本文は<b>保存しない</b>(複製にしないため)——
/// 本のリンクを拾うあいだだけメモリに置き、残すのは題名と URL だけ。
/// </summary>
public record QiitaArticle(string Url, string Body, string? Title);

/// <summary>
/// Qiita API v2 の記事検索。検索が本文まで返すので、記事ごとに引き直さなくてよい。
/// 未認証は 60 リクエスト/時、アクセストークンを設定すると 1000 リクエスト/時。
///
/// 推薦本(定番)と引用(興味トピック)の<b>2つの経路が同じここを通る</b>。
/// リクエストの間隔は名前付き HttpClient の層(<c>RequestPacingHandler</c>)で守る ——
/// 経路ごとに <c>Task.Delay</c> を置くと、それぞれ自分のぶんの待ちしか知らないので、
/// 2つの経路が続けて走ったときに間隔が縮む。
/// </summary>
public class QiitaSearch(
    IHttpClientFactory httpClientFactory,
    Func<string?>? accessTokenProvider = null)
{
    public const string HttpClientName = "qiita";

    /// <summary>
    /// 1ページの記事数。API の上限は 100 だが、本文込みの応答が大きいので半分に抑える
    /// (HttpClient に応答サイズの上限を掛けているため、大きすぎると正常な応答まで落ちる)。
    /// </summary>
    const int PageSize = 50;

    /// <summary>Qiita の page パラメータの上限。</summary>
    const int MaxPage = 100;

    /// <summary>
    /// 検索して記事を読む。1クエリずつページングするのが肝 —— 検索は新着順に返るため、
    /// 1ページで打ち切ると古い定番記事(「読むべき本」まとめの多くはこちら)が読めない。
    /// 同じ記事が複数のクエリに当たっても、URL で重複を落として1本に数える。
    /// </summary>
    public async Task<IReadOnlyList<QiitaArticle>> SearchAsync(
        IEnumerable<string> queries,
        int maxArticlesPerQuery,
        CancellationToken cancellationToken = default)
    {
        var effectiveQueries = queries.Where(query => !string.IsNullOrWhiteSpace(query)).ToList();
        if (effectiveQueries.Count == 0 || maxArticlesPerQuery <= 0)
        {
            return [];
        }

        using var client = httpClientFactory.CreateClient(HttpClientName);
        // トークンは任意(上限が 60 → 1000 リクエスト/時に上がる)。画面から設定できるので実行時に解決する
        var accessToken = accessTokenProvider?.Invoke();
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        // 記事の URL → 記事。クエリをまたいで同じ記事を1度だけ数えるための入れ物
        var articles = new Dictionary<string, QiitaArticle>(StringComparer.Ordinal);

        foreach (var query in effectiveQueries)
        {
            var fetched = 0;
            for (var page = 1; page <= MaxPage && fetched < maxArticlesPerQuery; page++)
            {
                var perPage = Math.Min(PageSize, maxArticlesPerQuery - fetched);
                var requestUri = "https://qiita.com/api/v2/items"
                    + $"?query={Uri.EscapeDataString(query)}&per_page={perPage}&page={page}";

                var json = await client.GetStringAsync(requestUri, cancellationToken);
                var count = Collect(json, articles);

                fetched += count;
                if (count < perPage)
                {
                    break; // 最終ページまで読んだ
                }
            }
        }

        return articles.Values.ToList();
    }

    /// <summary>1ページ分の記事を取り込み、そのページに載っていた記事数を返す(重複込み)。</summary>
    static int Collect(string json, Dictionary<string, QiitaArticle> articles)
    {
        var count = 0;

        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            count++;

            var body = GetString(item, "body");
            var url = GetString(item, "url");
            // 題名は画面に出す(押す前にどこで挙げられているか分かるように)。取れなければ null のまま
            var title = GetString(item, "title");
            // 出典記事の URL は書籍の一覧の href に出るため、http/https 以外は取り込まない
            if (body is null || url is null || !WebUrl.TryCreate(url, out _))
            {
                continue;
            }

            articles.TryAdd(url, new QiitaArticle(
                url, body, string.IsNullOrWhiteSpace(title) ? null : title));
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

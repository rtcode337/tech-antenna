using System.Net;
using TechAntenna.Core;
using TechAntenna.Core.Topics;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Books;

/// <summary>
/// Google Books API でキーワード検索を行う書籍カタログ。
/// API キーは実質必須(キー無しのリクエストは Google 共有の匿名プロジェクトの枠に入り、
/// その枠は1日あたり 0 件なので最初の1回から 429 になる)。
/// </summary>
public class GoogleBooksCatalog(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    Func<string?> apiKeyProvider,
    int maxResults = 20,
    TopicCatalog? catalog = null) : IBookCatalog
{
    public const string HttpClientName = "googlebooks";

    public string Name => "Google Books";

    public async Task<IReadOnlyList<Book>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        // キーは画面から設定できるので、起動時ではなく検索のたびに解決する
        var apiKey = apiKeyProvider();

        using var client = httpClientFactory.CreateClient(HttpClientName);

        // 日本語の技術書を拾いたいので言語を絞る。
        // 並びは既定の関連度順(orderBy を付けない) —— 集めたいのは新刊ではなく
        // 「その分野で読んでおくべき本」だから。`orderBy=newest` は取りこぼしも大きく、
        // 実測では `機械学習` が 0 件(関連度順なら 300 件)だった
        // 雑誌は除く(`printType=books`)。集めたいのは「読んでおくべき本」なのに、
        // 号を重ねるぶん数の多い雑誌が一覧を占めていた。ただしこれで落ちるのは
        // Google が雑誌として登録しているものだけで、日本のムック・増刊は書籍として返る ——
        // そちらはタイトルの型(`Periodical`)で落とす
        var requestUri =
            $"https://www.googleapis.com/books/v1/volumes?q={Uri.EscapeDataString(keyword)}"
            + $"&maxResults={maxResults}&langRestrict=ja&printType=books";
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            requestUri += $"&key={Uri.EscapeDataString(apiKey)}";
        }

        using var response = await client.GetAsync(requestUri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // 429 はキー未設定と本当の使いすぎの両方で起きるが、原因がまるで違うので切り分けて伝える。
            // 素の EnsureSuccessStatusCode ではどちらか分からない
            throw new HttpRequestException(string.IsNullOrWhiteSpace(apiKey)
                ? "Google Books API が 429 を返した。API キーが未設定のため("
                  + "キー無しのリクエストは共有の匿名プロジェクト扱いで1日あたりの上限が 0)。"
                  + "外部連携の画面で設定する"
                : "Google Books API が 429 を返した。1日あたりの上限に達したか、短時間に送りすぎている");
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        var collectedAt = timeProvider.GetUtcNow();
        return GoogleBooksResponseParser.Parse(json)
            .Where(entry => entry.Title.Length > 0)
            .Select(entry => new Book
            {
                Title = entry.Title,
                Isbn13 = entry.Isbn13,
                Authors = entry.Authors,
                Publisher = entry.Publisher,
                PublishedOn = entry.PublishedOn,
                Url = entry.Url,
                CoverUrl = entry.CoverUrl,
                SourceName = Name,
                CollectedAt = collectedAt,
                // 検索に使ったキーワードを、記事・イベントと突き合わせるためのタグにする
                Tags = (catalog ?? TopicCatalog.Empty).Normalize([keyword]),
                RawTags = [keyword],
            })
            .ToList();
    }
}

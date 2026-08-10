using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Books;

/// <summary>
/// 楽天ブックスでレビュー件数・平均評価を補う。「その分野で読んでおくべき本」を上位に出すための指標で、
/// 書誌情報は触らない(そちらは Google Books と openBD の担当)。
///
/// **ISBN 1件につき1リクエスト。** 楽天ブックス書籍検索API は複数 ISBN の一括指定を持たないため、
/// openBD のようにまとめて引けない。無料でコミュニティに開かれている API なので、
/// リクエストの間隔を空けて1件ずつ引く。
///
/// **取得したレビューを画面に出すときは楽天ウェブサービスのクレジット表記が要る**
/// (利用規約 Article 13)。表示側(`/books`)にリンクを置いてあるので、
/// 出す場所を増やすときは一緒に確認すること。
/// </summary>
public class RakutenBooksEnricher(
    IHttpClientFactory httpClientFactory,
    Func<string?> applicationIdProvider,
    Func<string?>? accessKeyProvider = null,
    TimeSpan? delayBetweenRequests = null) : IBookEnricher
{
    public const string HttpClientName = "rakutenbooks";

    const string Endpoint = "https://openapi.rakuten.co.jp/services/api/BooksBook/Search/20170404";

    readonly TimeSpan _delay = delayBetweenRequests ?? TimeSpan.FromSeconds(1);

    public string Name => "楽天ブックス";

    public async Task<IReadOnlyList<Book>> EnrichAsync(
        IReadOnlyList<Book> books,
        CancellationToken cancellationToken = default)
    {
        // アプリ ID は画面から設定できるので、起動時ではなく実行のたびに解決する
        var applicationId = applicationIdProvider();
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            return books;
        }

        var isbns = books
            .Select(book => book.Isbn13)
            .OfType<string>()
            .Where(isbn => isbn.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (isbns.Count == 0)
        {
            return books;
        }

        using var client = httpClientFactory.CreateClient(HttpClientName);
        var byIsbn = new Dictionary<string, RakutenReview>(StringComparer.Ordinal);

        for (var i = 0; i < isbns.Count; i++)
        {
            var json = await client.GetStringAsync(
                RequestUri(isbns[i], applicationId), cancellationToken);
            foreach (var review in RakutenBooksResponseParser.Parse(json))
            {
                byIsbn[review.Isbn] = review;
            }

            // 最後の1件の後は待たない(手動実行で無駄に待たせないため)
            if (i < isbns.Count - 1 && _delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }
        }

        return books.Select(book => Apply(book, byIsbn)).ToList();
    }

    string RequestUri(string isbn, string applicationId)
    {
        var uri = $"{Endpoint}?format=json&applicationId={Uri.EscapeDataString(applicationId)}"
            + $"&isbn={Uri.EscapeDataString(isbn)}";

        var accessKey = accessKeyProvider?.Invoke();
        return string.IsNullOrWhiteSpace(accessKey)
            ? uri
            : uri + $"&accessKey={Uri.EscapeDataString(accessKey)}";
    }

    static Book Apply(Book book, IReadOnlyDictionary<string, RakutenReview> byIsbn)
    {
        if (book.Isbn13 is not { Length: > 0 } isbn || !byIsbn.TryGetValue(isbn, out var review))
        {
            return book;
        }

        // レビューだけを載せた同じ本を返す(他の補完と同じく、元の値は触らない)
        book.ReviewCount = review.ReviewCount;
        book.ReviewAverage = review.ReviewAverage;
        return book;
    }
}

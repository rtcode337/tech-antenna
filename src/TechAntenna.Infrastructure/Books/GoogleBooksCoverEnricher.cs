using System.Net;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Books;

/// <summary>
/// 書影が欠けている本を、Google Books に ISBN で引いて埋める。
///
/// **openBD は技術書の書影をほとんど持っていない**(実測: リーダブルコード・達人プログラマー・
/// リファクタリング・SQL アンチパターン等 10 冊すべて `cover` が空)。記事から ISBN だけを拾う
/// 定番の書籍はそこが唯一の補完先だったので、表紙の出ない一覧になっていた。
/// 興味トピックの書籍に表紙が出るのは、あちらが Google Books の検索結果
/// (`imageLinks`)から来ているため —— つまり Google Books は同じ本の書影を持っている。
///
/// **引くのは書影が無い本だけ。** 楽天ブックスが先に埋めていれば(そちらは
/// レビューと同じ応答に入っているので追加コストが無い)ここでは何も起きない。
///
/// **ISBN の一括指定はできない**ので 1 冊 1 リクエスト。無料枠は 1 日 1,000 リクエストなので、
/// 同じ本を毎回引かないことが要点 —— 収集側が保存済みの書影を引き継いでから渡してくる
/// (<c>ClassicsCollectionRunner</c>)。**冊数の上限は設けない** ——
/// 上限で切ると「何冊ぶん諦めたか」が画面にも残らないまま、いつまでも埋まらない本が出る。
/// </summary>
public class GoogleBooksCoverEnricher(
    IHttpClientFactory httpClientFactory,
    Func<string?> apiKeyProvider,
    TimeSpan? delayBetweenRequests = null) : IBookEnricher
{
    /// <summary>検索と同じ HttpClient を使う(相手も枠も同じ)。</summary>
    public const string HttpClientName = GoogleBooksCatalog.HttpClientName;

    readonly TimeSpan _delay = delayBetweenRequests ?? TimeSpan.FromSeconds(1);

    public string Name => "Google Books(書影)";

    public async Task<IReadOnlyList<Book>> EnrichAsync(
        IReadOnlyList<Book> books, CancellationToken cancellationToken = default)
    {
        // キーは画面から設定できるので、起動時ではなく実行のたびに解決する。
        // **キーが無いなら問い合わせない** —— キー無しのリクエストは共有の匿名プロジェクト扱いで
        // 1 日あたりの上限が 0 なので、1 冊目から 429 になるだけ
        var apiKey = apiKeyProvider();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return books;
        }

        var isbns = books
            .Where(book => book.CoverUrl is null)
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
        var covers = new Dictionary<string, Uri>(StringComparer.Ordinal);

        for (var i = 0; i < isbns.Count; i++)
        {
            if (await FetchCoverAsync(client, isbns[i], apiKey, cancellationToken) is { } cover)
            {
                covers[isbns[i]] = cover;
            }

            // 最後の1件の後は待たない(手動実行で無駄に待たせないため)
            if (i < isbns.Count - 1 && _delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }
        }

        return books.Select(book => Apply(book, covers)).ToList();
    }

    async Task<Uri?> FetchCoverAsync(
        HttpClient client, string isbn, string apiKey, CancellationToken cancellationToken)
    {
        // ISBN 完全一致の検索。1 冊に絞れるので maxResults は 1
        var requestUri = "https://www.googleapis.com/books/v1/volumes"
            + $"?q={Uri.EscapeDataString($"isbn:{isbn}")}&maxResults=1"
            + $"&key={Uri.EscapeDataString(apiKey)}";

        using var response = await client.GetAsync(requestUri, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // **残りも諦めて投げる。** 枠を使い切った状態で残りを叩いても 429 が並ぶだけで、
            // 呼び出し側(収集ジョブ)は補完の失敗として記録し、集めた本はそのまま保存する
            throw new HttpRequestException(
                "Google Books API が 429 を返した(書影の補完)。1 日あたりの上限に達したか、"
                + "短時間に送りすぎている。書影は次回の収集で埋め直す");
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        return GoogleBooksResponseParser.Parse(json)
            .Select(entry => entry.CoverUrl)
            .FirstOrDefault(cover => cover is not null);
    }

    static Book Apply(Book book, IReadOnlyDictionary<string, Uri> covers)
    {
        if (book.CoverUrl is not null
            || book.Isbn13 is not { Length: > 0 } isbn
            || !covers.TryGetValue(isbn, out var cover))
        {
            return book;
        }

        // 書誌情報の項目が init なので、値を入れるには本を組み直すことになる
        return new Book
        {
            Id = book.Id,
            Title = book.Title,
            Isbn13 = book.Isbn13,
            Authors = book.Authors,
            Publisher = book.Publisher,
            PublishedOn = book.PublishedOn,
            Url = book.Url,
            CoverUrl = cover,
            SourceName = book.SourceName,
            CollectedAt = book.CollectedAt,
            ReviewCount = book.ReviewCount,
            ReviewAverage = book.ReviewAverage,
            Tags = book.Tags,
            // 生タグを写し忘れると、再正規化(RawTags から Tags を作り直す)でタグが空になる
            RawTags = book.RawTags,
            RecommendedBy = book.RecommendedBy,
        };
    }
}

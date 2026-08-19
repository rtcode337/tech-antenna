using System.Net;
using Microsoft.Extensions.Logging;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Books;

/// <summary>
/// 書影が欠けている本を、Google Books に ISBN で引いて埋める。
///
/// openBD は技術書の書影をほとんど持っていない(実測: リーダブルコード・達人プログラマー・
/// リファクタリング・SQL アンチパターン等 10 冊すべて `cover` が空)。記事から ISBN だけを拾う
/// 定番の書籍はそこが唯一の補完先だったので、表紙の出ない一覧になっていた。
/// 興味トピックの書籍に表紙が出るのは、あちらが Google Books の検索結果
/// (`imageLinks`)から来ているため —— つまり Google Books は同じ本の書影を持っている。
///
/// 引くのは書影が無い本だけ。楽天ブックスが先に埋めていれば(そちらは
/// レビューと同じ応答に入っているので追加コストが無い)ここでは何も起きない。
///
/// ISBN の一括指定はできないので 1 冊 1 リクエスト。無料枠は 1 日 1,000 リクエストなので、
/// 同じ本を毎回引かないことが要点 —— 収集側が保存済みの書影を引き継いでから渡してくる
/// (<c>ClassicsCollectionRunner</c>)。冊数の上限は設けない ——
/// 上限で切ると「何冊ぶん諦めたか」が画面にも残らないまま、いつまでも埋まらない本が出る。
/// </summary>
public class GoogleBooksCoverEnricher(
    IHttpClientFactory httpClientFactory,
    Func<string?> apiKeyProvider,
    ILogger<GoogleBooksCoverEnricher> logger,
    TimeSpan? delayBetweenRequests = null) : IBookEnricher
{
    /// <summary>検索と同じ HttpClient を使う(相手も枠も同じ)。</summary>
    public const string HttpClientName = GoogleBooksCatalog.HttpClientName;

    readonly TimeSpan _delay = delayBetweenRequests ?? TimeSpan.FromSeconds(1);

    /// <summary>
    /// この回数だけ続けて失敗したら打ち切る。1冊ずつの失敗(見つからない・一時的なエラー)で
    /// 全体を止めたくはないが、相手が落ちているのに数百回投げ続けるのも困るため。
    /// </summary>
    const int MaxConsecutiveFailures = 5;

    /// <summary>1日あたりの枠に達した(429 / 403)。残りは諦めるが、取れたぶんは返す。</summary>
    sealed class QuotaReachedException(string message) : Exception(message);

    public string Name => "Google Books(書影)";

    public async Task<IReadOnlyList<Book>> EnrichAsync(
        IReadOnlyList<Book> books, CancellationToken cancellationToken = default)
    {
        // キーは画面から設定できるので、起動時ではなく実行のたびに解決する。
        // キーが無いなら問い合わせない —— キー無しのリクエストは共有の匿名プロジェクト扱いで
        // 1 日あたりの上限が 0 なので、1 冊目から 429 になるだけ
        var apiKey = apiKeyProvider();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // 黙って素通りしない。収集は成功として終わるので、ログに出さないと
            // 「補完したのに埋まらなかった」のか「そもそも問い合わせていない」のかが分からない
            logger.LogWarning(
                "Google Books の API キーが未設定のため、書影の補完をしません"
                + "(画面の「設定 → 外部連携」から入れる)");
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
            logger.LogInformation("書影の欠けている本が無いので、Google Books へは問い合わせません");
            return books;
        }

        // 1 冊 1 リクエスト・既定 1 秒間隔なので、冊数がそのまま所要時間になる。
        // 「動いていないのでは」と見えないよう、始める前に見込みを出す
        logger.LogInformation(
            "{Count} 冊の書影を Google Books に問い合わせます(1 冊 1 リクエスト・{Delay} 秒間隔)",
            isbns.Count, _delay.TotalSeconds);

        using var client = httpClientFactory.CreateClient(HttpClientName);
        var covers = new Dictionary<string, Uri>(StringComparer.Ordinal);

        var consecutiveFailures = 0;
        for (var i = 0; i < isbns.Count; i++)
        {
            try
            {
                if (await FetchCoverAsync(client, isbns[i], apiKey, cancellationToken) is { } cover)
                {
                    covers[isbns[i]] = cover;
                }
                consecutiveFailures = 0;
            }
            catch (QuotaReachedException ex)
            {
                // ここまでに取れた書影は捨てない。投げて抜けると呼び出し側は補完前の本を
                // 保存するので、数百リクエストぶんの結果が毎回消えていつまでも埋まらない
                // (860 冊 > 1 日 1,000 の枠なので、枠切れは必ず途中で起きる)
                logger.LogWarning(
                    "{Filled}/{Count} 冊まで埋めたところで Google Books の枠に達した({Reason})。"
                    + "取れたぶんは保存し、残りは次回の収集で埋める",
                    covers.Count, isbns.Count, ex.Message);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 1 冊の失敗で全体を止めない(見つからない本・一時的なエラー)
                consecutiveFailures++;
                logger.LogWarning(ex, "書影を引けなかった(ISBN {Isbn})", isbns[i]);
                if (consecutiveFailures >= MaxConsecutiveFailures)
                {
                    logger.LogWarning(
                        "{Count} 回続けて失敗したので書影の補完を打ち切る({Filled} 冊は保存する)",
                        consecutiveFailures, covers.Count);
                    break;
                }
            }

            // 最後の1件の後は待たない(手動実行で無駄に待たせないため)
            if (i < isbns.Count - 1 && _delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }
        }

        logger.LogInformation(
            "{Filled}/{Count} 冊の書影が Google Books で埋まりました", covers.Count, isbns.Count);

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
        // 枠切れは 429 だけではない。Google の API は 1 日あたりの上限を
        // `403 dailyLimitExceeded` で返すことがある(429 は短時間に送りすぎたとき)。
        // どちらも「叩き続けても同じものが並ぶだけ」なので、残りは諦める
        if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
        {
            throw new QuotaReachedException(
                $"HTTP {(int)response.StatusCode}: "
                + Excerpt(await response.Content.ReadAsStringAsync(cancellationToken)));
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        return GoogleBooksResponseParser.Parse(json)
            .Select(entry => entry.CoverUrl)
            .FirstOrDefault(cover => cover is not null);
    }

    /// <summary>例外メッセージにそのまま載せられる長さに切る。</summary>
    static string Excerpt(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200] + "…";
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

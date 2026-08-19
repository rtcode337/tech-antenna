using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Infrastructure.Books;

namespace TechAntenna.Tests.Infrastructure;

public class GoogleBooksCoverEnricherTests
{
    const string Response = """
        {
          "items": [
            {
              "volumeInfo": {
                "title": "リーダブルコード",
                "industryIdentifiers": [{"type": "ISBN_13", "identifier": "9784873115658"}],
                "imageLinks": {"thumbnail": "https://books.google.com/books/content?id=abc&zoom=1"}
              }
            }
          ]
        }
        """;

    static Book NewBook(string? isbn13, Uri? coverUrl = null) => new()
    {
        Title = "リーダブルコード",
        Isbn13 = isbn13,
        CoverUrl = coverUrl,
        SourceName = "Qiita",
        CollectedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        Tags = ["ai"],
        RawTags = ["AI"],
    };

    static GoogleBooksCoverEnricher NewEnricher(IHttpClientFactory factory, string? apiKey = "test-key") =>
        new(factory, () => apiKey, NullLogger<GoogleBooksCoverEnricher>.Instance,
            delayBetweenRequests: TimeSpan.Zero);

    [Fact]
    public async Task 書影が無い本をISBNで引いて埋める()
    {
        // openBD は技術書の書影をほとんど持たないので、ここが定番の書籍の書影の出どころになる
        var factory = new StubHttpClientFactory(Response);

        var book = Assert.Single(await NewEnricher(factory).EnrichAsync([NewBook("9784873115658")]));

        Assert.Equal(
            "https://books.google.com/books/content?id=abc&zoom=1", book.CoverUrl?.ToString());
        // 書影を入れるために本を組み直すので、生タグが落ちていないことも見る
        Assert.Equal(["AI"], book.RawTags);
        var requested = Assert.Single(factory.RequestedUris);
        Assert.Contains("q=isbn%3A9784873115658", requested.Query);
        Assert.Contains("key=test-key", requested.Query);
    }

    [Fact]
    public async Task 既に書影がある本は引かない()
    {
        // 1 冊 1 リクエストで無料枠は 1 日 1,000。楽天や openBD で埋まったぶんは投げない
        var factory = new StubHttpClientFactory(Response);
        var cover = new Uri("https://thumbnail.image.rakuten.co.jp/medium.jpg");

        var book = Assert.Single(
            await NewEnricher(factory).EnrichAsync([NewBook("9784873115658", cover)]));

        Assert.Empty(factory.RequestedUris);
        Assert.Equal(cover, book.CoverUrl);
    }

    [Fact]
    public async Task ISBNが無ければ引かない()
    {
        var factory = new StubHttpClientFactory(Response);

        var book = Assert.Single(await NewEnricher(factory).EnrichAsync([NewBook(null)]));

        Assert.Empty(factory.RequestedUris);
        Assert.Null(book.CoverUrl);
    }

    [Fact]
    public async Task キーが無ければ引かない()
    {
        // キー無しの Google Books は共有の匿名プロジェクト扱いで枠が 0。投げても 429 になるだけ
        var factory = new StubHttpClientFactory(Response);

        var book = Assert.Single(
            await NewEnricher(factory, apiKey: "").EnrichAsync([NewBook("9784873115658")]));

        Assert.Empty(factory.RequestedUris);
        Assert.Null(book.CoverUrl);
    }

    [Fact]
    public async Task 枠を使い切っても取れたぶんの書影は返す()
    {
        // ここが埋まらない原因だった。投げて抜けると呼び出し側は補完前の本を保存するので、
        // 数百リクエストぶんの書影が毎回消えていた(冊数 > 1 日の枠なので枠切れは必ず起きる)
        var factory = new SequenceHttpClientFactory(
            (HttpStatusCode.OK, Response),
            (HttpStatusCode.TooManyRequests, """{"error":{"message":"rateLimitExceeded"}}"""));

        var books = await NewEnricher(factory).EnrichAsync(
            [NewBook("9784873115658"), NewBook("9784873119045")]);

        Assert.Equal(
            "https://books.google.com/books/content?id=abc&zoom=1", books[0].CoverUrl?.ToString());
        Assert.Null(books[1].CoverUrl);
        // 枠切れの後は投げない(叩いても同じものが並ぶだけ)
        Assert.Equal(2, factory.RequestedUris.Count);
    }

    [Fact]
    public async Task 枠切れが403で返っても諦める()
    {
        // Google の API は 1 日あたりの上限を 403 dailyLimitExceeded で返すことがある
        var factory = new SequenceHttpClientFactory(
            (HttpStatusCode.Forbidden, """{"error":{"message":"dailyLimitExceeded"}}"""),
            (HttpStatusCode.OK, Response));

        var books = await NewEnricher(factory).EnrichAsync(
            [NewBook("9784873115658"), NewBook("9784873119045")]);

        Assert.All(books, book => Assert.Null(book.CoverUrl));
        Assert.Single(factory.RequestedUris);
    }

    [Fact]
    public async Task 一冊の失敗では止めない()
    {
        // 見つからない本・一時的なエラーで、後続の本まで諦めてはいけない
        var factory = new SequenceHttpClientFactory(
            (HttpStatusCode.InternalServerError, "boom"),
            (HttpStatusCode.OK, Response));

        var books = await NewEnricher(factory).EnrichAsync(
            [NewBook("9784873115658"), NewBook("9784873119045")]);

        Assert.Null(books[0].CoverUrl);
        Assert.Equal(
            "https://books.google.com/books/content?id=abc&zoom=1", books[1].CoverUrl?.ToString());
    }

    /// <summary>呼ばれた順に応答を返す(最後の1つはそれ以降も使い回す)。</summary>
    sealed class SequenceHttpClientFactory : IHttpClientFactory
    {
        readonly (HttpStatusCode Status, string Body)[] _responses;

        public SequenceHttpClientFactory(params (HttpStatusCode Status, string Body)[] responses) =>
            _responses = responses;

        public List<Uri> RequestedUris { get; } = [];

        public HttpClient CreateClient(string name) => new(new Handler(this));

        sealed class Handler(SequenceHttpClientFactory owner) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri is { } uri)
                {
                    owner.RequestedUris.Add(uri);
                }
                var (status, body) = owner._responses[
                    Math.Min(owner.RequestedUris.Count - 1, owner._responses.Length - 1)];

                return Task.FromResult(
                    new HttpResponseMessage(status) { Content = new StringContent(body) });
            }
        }
    }

    [Fact]
    public async Task 見つからない本は書影のないままにする()
    {
        var factory = new StubHttpClientFactory("""{"totalItems": 0}""");

        var book = Assert.Single(await NewEnricher(factory).EnrichAsync([NewBook("9784999999999")]));

        Assert.Null(book.CoverUrl);
    }
}

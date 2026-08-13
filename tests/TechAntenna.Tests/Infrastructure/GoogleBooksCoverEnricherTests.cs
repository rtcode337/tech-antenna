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

    static GoogleBooksCoverEnricher NewEnricher(StubHttpClientFactory factory, string? apiKey = "test-key") =>
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
    public async Task 枠を使い切ったら理由の分かる例外にする()
    {
        // 収集ジョブ側は補完の失敗として記録し、集めた本はそのまま保存する
        var factory = new StubHttpClientFactory(Response, HttpStatusCode.TooManyRequests);

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => NewEnricher(factory).EnrichAsync([NewBook("9784873115658")]));

        Assert.Contains("429", error.Message);
    }

    [Fact]
    public async Task 見つからない本は書影のないままにする()
    {
        var factory = new StubHttpClientFactory("""{"totalItems": 0}""");

        var book = Assert.Single(await NewEnricher(factory).EnrichAsync([NewBook("9784999999999")]));

        Assert.Null(book.CoverUrl);
    }
}

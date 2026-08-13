using TechAntenna.Core.Models;
using TechAntenna.Infrastructure.Books;

namespace TechAntenna.Tests.Infrastructure;

public class RakutenBooksEnricherTests
{
    // 楽天の API は要素を {"Item": {...}} で包む。reviewAverage は文字列で返る
    const string Response = """
        {
          "Items": [
            {
              "Item": {
                "title": "楽天側のタイトル",
                "isbn": "9784123456789",
                "reviewCount": 42,
                "reviewAverage": "4.5"
              }
            }
          ],
          "count": 1
        }
        """;

    static Book NewBook(string? isbn13) => new()
    {
        Title = "検索側のタイトル",
        Isbn13 = isbn13,
        SourceName = "Google Books",
        CollectedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        Tags = ["ai"],
        RawTags = ["AI"],
    };

    static RakutenBooksEnricher NewEnricher(StubHttpClientFactory factory) =>
        new(factory, () => "test-app-id", delayBetweenRequests: TimeSpan.Zero);

    [Fact]
    public async Task レビュー件数と平均評価を補う()
    {
        var enricher = NewEnricher(new StubHttpClientFactory(Response));

        var book = Assert.Single(await enricher.EnrichAsync([NewBook("9784123456789")]));

        Assert.Equal(42, book.ReviewCount);
        Assert.Equal(4.5, book.ReviewAverage);
        // 書誌情報は楽天側で上書きしない(担当は Google Books と openBD)
        Assert.Equal("検索側のタイトル", book.Title);
    }

    [Fact]
    public async Task ISBNごとに1リクエストする()
    {
        // 楽天ブックス書籍検索API は複数 ISBN の一括指定を持たない
        var factory = new StubHttpClientFactory(Response);

        await NewEnricher(factory).EnrichAsync([NewBook("9784123456789"), NewBook("9784999999999")]);

        Assert.Equal(2, factory.RequestedUris.Count);
        Assert.All(factory.RequestedUris, uri => Assert.Contains("applicationId=test-app-id", uri.Query));
        Assert.Contains("isbn=9784123456789", factory.RequestedUris[0].Query);
    }

    [Fact]
    public async Task ISBNが無ければ問い合わせない()
    {
        var factory = new StubHttpClientFactory(Response);

        var book = Assert.Single(await NewEnricher(factory).EnrichAsync([NewBook(null)]));

        Assert.Empty(factory.RequestedUris);
        Assert.Null(book.ReviewCount);
    }

    [Fact]
    public async Task アプリIDが無ければ問い合わせない()
    {
        var factory = new StubHttpClientFactory(Response);
        var enricher = new RakutenBooksEnricher(factory, () => "", delayBetweenRequests: TimeSpan.Zero);

        var book = Assert.Single(await enricher.EnrichAsync([NewBook("9784123456789")]));

        Assert.Empty(factory.RequestedUris);
        Assert.Null(book.ReviewCount);
    }

    [Fact]
    public async Task 該当が無ければレビューを付けない()
    {
        // 「レビュー0件」ではなく「分からない」なので null のままにする
        var enricher = NewEnricher(new StubHttpClientFactory("""{"Items":[],"count":0}"""));

        var book = Assert.Single(await enricher.EnrichAsync([NewBook("9784999999999")]));

        Assert.Null(book.ReviewCount);
    }

    [Fact]
    public void レビューが無い本はreviewAverageを0点として扱わない()
    {
        // 楽天はレビューが無いとき "0.0" を返す。そのまま平均に使うと評価が最低の本になる
        var review = Assert.Single(RakutenBooksResponseParser.Parse("""
            {"Items":[{"Item":{"isbn":"9784123456789","reviewCount":0,"reviewAverage":"0.0"}}]}
            """));

        Assert.Equal(0, review.ReviewCount);
        Assert.Null(review.ReviewAverage);
    }

    [Fact]
    public async Task 書影が無い本には楽天の画像を入れる()
    {
        // openBD は技術書の書影をほとんど持たない(実測 10 冊中 0 冊)。楽天の応答には
        // レビューと同じリクエストで画像 URL が入ってくるので、そこから埋める
        var enricher = NewEnricher(new StubHttpClientFactory("""
            {"Items":[{"Item":{
              "isbn":"9784123456789","reviewCount":3,"reviewAverage":"4.0",
              "smallImageUrl":"https://thumbnail.image.rakuten.co.jp/small.jpg",
              "mediumImageUrl":"https://thumbnail.image.rakuten.co.jp/medium.jpg",
              "largeImageUrl":"https://thumbnail.image.rakuten.co.jp/large.jpg"
            }}]}
            """));

        var book = Assert.Single(await enricher.EnrichAsync([NewBook("9784123456789")]));

        // 一覧の書影は小さいので中サイズを使う
        Assert.Equal("https://thumbnail.image.rakuten.co.jp/medium.jpg", book.CoverUrl?.ToString());
        // 書影を入れるために本を組み直すので、他の値が落ちていないことも見る
        Assert.Equal("検索側のタイトル", book.Title);
        Assert.Equal(["AI"], book.RawTags);
        Assert.Equal(3, book.ReviewCount);
    }

    [Fact]
    public async Task 既に書影がある本は上書きしない()
    {
        var enricher = NewEnricher(new StubHttpClientFactory("""
            {"Items":[{"Item":{"isbn":"9784123456789","reviewCount":3,
              "mediumImageUrl":"https://thumbnail.image.rakuten.co.jp/medium.jpg"}}]}
            """));
        var book = NewBook("9784123456789");
        book = new Book
        {
            Title = book.Title,
            Isbn13 = book.Isbn13,
            CoverUrl = new Uri("https://books.google.com/cover.jpg"),
            SourceName = book.SourceName,
            CollectedAt = book.CollectedAt,
            Tags = book.Tags,
            RawTags = book.RawTags,
        };

        var enriched = Assert.Single(await enricher.EnrichAsync([book]));

        Assert.Equal("https://books.google.com/cover.jpg", enriched.CoverUrl?.ToString());
    }

    [Fact]
    public void Itemで包まない形も読める()
    {
        var review = Assert.Single(RakutenBooksResponseParser.Parse("""
            {"Items":[{"isbn":"9784123456789","reviewCount":7,"reviewAverage":3.5}]}
            """));

        Assert.Equal(7, review.ReviewCount);
        Assert.Equal(3.5, review.ReviewAverage);
    }
}

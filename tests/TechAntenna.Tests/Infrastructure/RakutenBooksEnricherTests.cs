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
        new(factory, "test-app-id", delayBetweenRequests: TimeSpan.Zero);

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
        var enricher = new RakutenBooksEnricher(factory, "", delayBetweenRequests: TimeSpan.Zero);

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
    public void Itemで包まない形も読める()
    {
        var review = Assert.Single(RakutenBooksResponseParser.Parse("""
            {"Items":[{"isbn":"9784123456789","reviewCount":7,"reviewAverage":3.5}]}
            """));

        Assert.Equal(7, review.ReviewCount);
        Assert.Equal(3.5, review.ReviewAverage);
    }
}

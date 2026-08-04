using TechAntenna.Core.Models;
using TechAntenna.Infrastructure.Books;

namespace TechAntenna.Tests.Infrastructure;

public class OpenBdEnricherTests
{
    const string Response = """
        [
          {
            "summary": {
              "isbn": "9784123456789",
              "title": "openBD 側のタイトル",
              "publisher": "openBD 側の出版社",
              "pubdate": "20260315",
              "author": "山田 太郎／著",
              "cover": "https://cover.example.com/9784123456789.jpg"
            }
          }
        ]
        """;

    static Book NewBook(string? isbn13) => new()
    {
        Title = "検索側のタイトル",
        Isbn13 = isbn13,
        SourceName = "Google Books",
        CollectedAt = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero),
        Tags = ["c#"],
        RawTags = ["C#"],
    };

    [Fact]
    public async Task 補完してもタグと生タグを保つ()
    {
        // 生タグを落とすと、再正規化(RawTags から Tags を作り直す)でタグが空になり、
        // 補完できた本ほどトピック横断から落ちる
        var enricher = new OpenBdEnricher(new StubHttpClientFactory(Response));

        var result = await enricher.EnrichAsync([NewBook("9784123456789")]);

        var book = Assert.Single(result);
        Assert.Equal(["c#"], book.Tags);
        Assert.Equal(["C#"], book.RawTags);
    }

    [Fact]
    public async Task 欠けている項目を補う()
    {
        var enricher = new OpenBdEnricher(new StubHttpClientFactory(Response));

        var result = await enricher.EnrichAsync([NewBook("9784123456789")]);

        var book = Assert.Single(result);
        Assert.Equal("openBD 側の出版社", book.Publisher);
        Assert.Equal(new DateOnly(2026, 3, 15), book.PublishedOn);
        Assert.Equal(["山田 太郎／著"], book.Authors);
        Assert.Equal(new Uri("https://cover.example.com/9784123456789.jpg"), book.CoverUrl);
    }

    [Fact]
    public async Task 既に値がある項目は上書きしない()
    {
        var enricher = new OpenBdEnricher(new StubHttpClientFactory(Response));
        var withValues = new Book
        {
            Title = "検索側のタイトル",
            Isbn13 = "9784123456789",
            Authors = ["検索側の著者"],
            Publisher = "検索側の出版社",
            PublishedOn = new DateOnly(2020, 1, 1),
            SourceName = "Google Books",
            CollectedAt = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero),
            Tags = ["c#"],
        };

        var result = await enricher.EnrichAsync([withValues]);

        var book = Assert.Single(result);
        Assert.Equal("検索側の出版社", book.Publisher);
        Assert.Equal(new DateOnly(2020, 1, 1), book.PublishedOn);
        Assert.Equal(["検索側の著者"], book.Authors);
        // タイトルは openBD 側で上書きしない
        Assert.Equal("検索側のタイトル", book.Title);
    }

    [Fact]
    public async Task ISBNが無ければ問い合わせずそのまま返す()
    {
        var factory = new StubHttpClientFactory(Response);
        var enricher = new OpenBdEnricher(factory);

        var result = await enricher.EnrichAsync([NewBook(null)]);

        Assert.Empty(factory.RequestedUris);
        Assert.Equal("検索側のタイトル", Assert.Single(result).Title);
    }

    [Fact]
    public async Task 複数のISBNをまとめて問い合わせる()
    {
        var factory = new StubHttpClientFactory(Response);
        var enricher = new OpenBdEnricher(factory);

        await enricher.EnrichAsync([NewBook("9784123456789"), NewBook("9784999999999")]);

        var uri = Assert.Single(factory.RequestedUris);
        Assert.Contains("isbn=9784123456789,9784999999999", Uri.UnescapeDataString(uri.ToString()));
    }

    [Fact]
    public async Task 該当が無いISBNの書籍はそのまま返す()
    {
        var enricher = new OpenBdEnricher(new StubHttpClientFactory("[null]"));

        var result = await enricher.EnrichAsync([NewBook("9784999999999")]);

        var book = Assert.Single(result);
        Assert.Null(book.Publisher);
        Assert.Empty(book.Authors);
    }
}

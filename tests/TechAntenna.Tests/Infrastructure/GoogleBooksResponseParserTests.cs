using TechAntenna.Infrastructure.Books;

namespace TechAntenna.Tests.Infrastructure;

public class GoogleBooksResponseParserTests
{
    // Google Books API のレスポンスを模した JSON
    const string Response = """
        {
          "kind": "books#volumes",
          "totalItems": 2,
          "items": [
            {
              "id": "abc123",
              "volumeInfo": {
                "title": "実践 C# プログラミング",
                "authors": ["山田 太郎", "鈴木 花子"],
                "publisher": "サンプル出版",
                "publishedDate": "2026-03-15",
                "industryIdentifiers": [
                  { "type": "ISBN_10", "identifier": "4123456789" },
                  { "type": "ISBN_13", "identifier": "9784123456789" }
                ],
                "infoLink": "https://books.example.com/books?id=abc123",
                "imageLinks": {
                  "thumbnail": "https://books.example.com/covers/abc123.jpg"
                },
                "description": "本文の説明。取り込まない。"
              }
            },
            {
              "id": "def456",
              "volumeInfo": {
                "title": "最小限の書誌情報しかない本",
                "publishedDate": "2025"
              }
            }
          ]
        }
        """;

    [Fact]
    public void 書誌情報を解析できる()
    {
        var entries = GoogleBooksResponseParser.Parse(Response);

        Assert.Equal(2, entries.Count);
        var first = entries[0];
        Assert.Equal("実践 C# プログラミング", first.Title);
        Assert.Equal("9784123456789", first.Isbn13);
        Assert.Equal(["山田 太郎", "鈴木 花子"], first.Authors);
        Assert.Equal("サンプル出版", first.Publisher);
        Assert.Equal(new DateOnly(2026, 3, 15), first.PublishedOn);
        Assert.Equal(new Uri("https://books.example.com/books?id=abc123"), first.Url);
        Assert.Equal(new Uri("https://books.example.com/covers/abc123.jpg"), first.CoverUrl);
    }

    [Fact]
    public void ISBN13を優先して取り出す()
    {
        var entries = GoogleBooksResponseParser.Parse(Response);

        // ISBN_10 も並んでいるが ISBN_13 を選ぶ
        Assert.Equal("9784123456789", entries[0].Isbn13);
    }

    [Fact]
    public void 欠けている項目はnullや空になる()
    {
        var entries = GoogleBooksResponseParser.Parse(Response);

        var sparse = entries[1];
        Assert.Null(sparse.Isbn13);
        Assert.Empty(sparse.Authors);
        Assert.Null(sparse.Publisher);
        Assert.Null(sparse.Url);
        Assert.Null(sparse.CoverUrl);
    }

    [Theory]
    [InlineData("2013", 2013, 1, 1)]
    [InlineData("2013-03", 2013, 3, 1)]
    [InlineData("2013-03-15", 2013, 3, 15)]
    public void 年だけ年月だけの刊行日も解析できる(string published, int year, int month, int day)
    {
        var json = $$$"""
            {"items":[{"volumeInfo":{"title":"本","publishedDate":"{{{published}}}"}}]}
            """;

        var entry = Assert.Single(GoogleBooksResponseParser.Parse(json));

        Assert.Equal(new DateOnly(year, month, day), entry.PublishedOn);
    }

    [Fact]
    public void 検索結果が0件ならitemsが無く空を返す()
    {
        var entries = GoogleBooksResponseParser.Parse("""{"kind":"books#volumes","totalItems":0}""");

        Assert.Empty(entries);
    }
}

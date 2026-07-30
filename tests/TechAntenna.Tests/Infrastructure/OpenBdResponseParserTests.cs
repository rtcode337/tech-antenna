using TechAntenna.Infrastructure.Books;

namespace TechAntenna.Tests.Infrastructure;

public class OpenBdResponseParserTests
{
    // openBD のレスポンスを模した JSON。見つからなかった ISBN の要素は null になる
    const string Response = """
        [
          {
            "summary": {
              "isbn": "9784123456789",
              "title": "実践 C# プログラミング",
              "publisher": "サンプル出版",
              "pubdate": "20260315",
              "author": "山田 太郎／著",
              "cover": "https://cover.example.com/9784123456789.jpg"
            }
          },
          null,
          {
            "summary": {
              "isbn": "9784999999999",
              "title": "刊行日が年月だけの本",
              "publisher": "",
              "pubdate": "202601",
              "author": "",
              "cover": ""
            }
          }
        ]
        """;

    [Fact]
    public void 書誌情報を解析できる()
    {
        var entries = OpenBdResponseParser.Parse(Response);

        // null 要素は読み飛ばす
        Assert.Equal(2, entries.Count);
        var first = entries[0];
        Assert.Equal("9784123456789", first.Isbn13);
        Assert.Equal("実践 C# プログラミング", first.Title);
        Assert.Equal("サンプル出版", first.Publisher);
        Assert.Equal(new DateOnly(2026, 3, 15), first.PublishedOn);
        Assert.Equal("山田 太郎／著", first.Author);
        Assert.Equal(new Uri("https://cover.example.com/9784123456789.jpg"), first.CoverUrl);
    }

    [Fact]
    public void 空文字の項目はnullとして扱う()
    {
        var entries = OpenBdResponseParser.Parse(Response);

        var sparse = entries[1];
        Assert.Null(sparse.Publisher);
        Assert.Null(sparse.Author);
        Assert.Null(sparse.CoverUrl);
    }

    [Theory]
    [InlineData("20260315", 2026, 3, 15)]
    [InlineData("202601", 2026, 1, 1)]
    [InlineData("2026", 2026, 1, 1)]
    [InlineData("2026-03-15", 2026, 3, 15)]
    public void 各種のpubdate形式を解析できる(string pubdate, int year, int month, int day)
    {
        var json = $$$"""
            [{"summary":{"isbn":"9784123456789","pubdate":"{{{pubdate}}}"}}]
            """;

        var entry = Assert.Single(OpenBdResponseParser.Parse(json));

        Assert.Equal(new DateOnly(year, month, day), entry.PublishedOn);
    }

    [Fact]
    public void 配列でなければFormatExceptionを投げる()
    {
        Assert.Throws<FormatException>(() => OpenBdResponseParser.Parse("""{"error":"bad"}"""));
    }
}

using TechAntenna.Core.Models;

namespace TechAntenna.Tests.Core;

public class BookKeyTests
{
    static Book NewBook(string? isbn13 = null, string? url = null, string title = "本") => new()
    {
        Title = title,
        Isbn13 = isbn13,
        Url = url is null ? null : new Uri(url),
        SourceName = "テスト",
        CollectedAt = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void ISBNがあればISBNをキーにする()
    {
        var key = BookKey.For(NewBook(isbn13: "9784123456789", url: "https://example.com/a"));

        Assert.Equal("isbn:9784123456789", key);
    }

    [Fact]
    public void ISBNが無ければURLをキーにする()
    {
        var key = BookKey.For(NewBook(url: "https://example.com/a"));

        Assert.Equal("url:https://example.com/a", key);
    }

    [Fact]
    public void ISBNもURLも無ければタイトルをキーにする()
    {
        var key = BookKey.For(NewBook(title: "タイトルだけの本"));

        Assert.Equal("title:タイトルだけの本", key);
    }

    [Fact]
    public void 同じISBNなら別のURLでも同じキーになる()
    {
        var a = BookKey.For(NewBook(isbn13: "9784123456789", url: "https://example.com/a"));
        var b = BookKey.For(NewBook(isbn13: "9784123456789", url: "https://example.com/b"));

        Assert.Equal(a, b);
    }
}

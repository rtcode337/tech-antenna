using TechAntenna.Infrastructure.Feeds;

namespace TechAntenna.Tests.Infrastructure;

public class HtmlTextTests
{
    [Fact]
    public void タグを除去して実体参照を戻す()
    {
        var result = HtmlText.Strip("<p>A &amp; B の<code>比較</code></p>");

        Assert.Equal("A & B の 比較", result);
    }

    [Fact]
    public void 連続する空白は1つにまとめる()
    {
        var result = HtmlText.Strip("a\n\n  b\t c");

        Assert.Equal("a b c", result);
    }

    [Fact]
    public void 最大長で切り詰める()
    {
        var result = HtmlText.Strip(new string('あ', 3000), maxLength: 100);

        Assert.Equal(100, result!.Length);
    }

    [Fact]
    public void 空やタグのみならnullを返す()
    {
        Assert.Null(HtmlText.Strip(null));
        Assert.Null(HtmlText.Strip("   "));
        Assert.Null(HtmlText.Strip("<br/><hr/>"));
    }
}

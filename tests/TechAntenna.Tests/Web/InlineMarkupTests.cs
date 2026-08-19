using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

public class InlineMarkupTests
{
    static string Html(string? text) => InlineMarkup.ToHtml(text).Value ?? "";

    [Fact]
    public void 強調とコードをHTMLに直す()
    {
        // そのまま出すと記号が画面に見えていた(「CLI は別コンテナ…」がアスタリスクごと出た)
        Assert.Equal(
            "<code>claude setup-token</code> で発行する。<strong>優先</strong>される",
            Html("`claude setup-token` で発行する。**優先**される"));
    }

    [Fact]
    public void 閉じていない記号は文字のまま残す()
    {
        // 説明文の書き間違いでタグが開いたままになると、以降の画面が崩れる
        Assert.Equal("**閉じ忘れ", Html("**閉じ忘れ"));
        Assert.Equal("`閉じ忘れ", Html("`閉じ忘れ"));
    }

    [Fact]
    public void HTMLは退避する()
    {
        // いまは自前の文字列しか通らないが、順序を逆にすると外部の値でタグが出る
        Assert.Equal("&lt;script&gt;x&lt;/script&gt;", Html("<script>x</script>"));
        Assert.Equal("a &amp; b", Html("a & b"));
    }

    [Fact]
    public void 空はそのまま空()
    {
        Assert.Equal("", Html(null));
        Assert.Equal("", Html(""));
    }

    [Fact]
    public void 複数の強調をそれぞれ閉じる()
    {
        Assert.Equal(
            "<strong>あ</strong>と<strong>い</strong>",
            Html("**あ**と**い**"));
    }
}

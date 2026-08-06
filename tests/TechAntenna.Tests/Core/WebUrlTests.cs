using TechAntenna.Core;

namespace TechAntenna.Tests.Core;

public class WebUrlTests
{
    [Theory]
    [InlineData("https://example.com/articles/1")]
    [InlineData("http://example.com/")]
    public void httpとhttpsの絶対URLは通す(string value)
    {
        Assert.True(WebUrl.TryCreate(value, out var url));
        Assert.Equal(value, url.ToString());
    }

    // 取り込んだ URL は画面の href / img src にそのまま出るため、
    // リンクとして意味を成さないスキームを通すと格納型 XSS の入口になる
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/articles/1")] // 相対
    [InlineData("")]
    [InlineData(null)]
    public void http以外のスキームや相対URLは弾く(string? value)
    {
        Assert.False(WebUrl.TryCreate(value, out _));
        Assert.Throws<FormatException>(() => WebUrl.Require(value));
    }
}

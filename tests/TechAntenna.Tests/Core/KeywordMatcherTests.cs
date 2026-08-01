using TechAntenna.Core;

namespace TechAntenna.Tests.Core;

public class KeywordMatcherTests
{
    [Theory]
    // 日本語に挟まれた英字の語も拾う(Doorkeeper のイベント名で最も多い形)
    [InlineData("生成AI最新ニュースライブ", "AI")]
    [InlineData("AIエージェント設計・活用の実践力", "AI")]
    // 記号を含む語。前後が英数字でなければ一致する
    [InlineData("C#入門もくもく会", "C#")]
    [InlineData("はじめての ASP.NET Core", ".NET")]
    // 大文字小文字と全角半角はそろえて比較する
    [InlineData("llm もくもく会", "LLM")]
    [InlineData("ＡＩ勉強会", "AI")]
    public void 含まれていれば一致する(string text, string keyword) =>
        Assert.True(KeywordMatcher.Contains(text, keyword));

    [Theory]
    // 英単語の一部に埋もれた「AI」を拾わない —— 単純な部分一致だとここで誤爆する
    [InlineData("Rails もくもく会", "AI")]
    [InlineData("email マーケティング入門", "AI")]
    [InlineData("детали", "AI")]
    // Doorkeeper が実際に返してきた誤ヒット。説明文の URL に .net があるだけのもの
    [InlineData("Sendagaya.rb #535", ".NET")]
    [InlineData("10時間飛行訓練コース（国土交通省許可承認取得）", "C#")]
    public void 含まれていなければ一致しない(string text, string keyword) =>
        Assert.False(KeywordMatcher.Contains(text, keyword));

    [Fact]
    public void 最初の出現が英数字に挟まれていても後ろの出現で一致する()
    {
        // 先頭の「Rails」では弾かれるが、後ろの「AI」で一致する
        Assert.True(KeywordMatcher.Contains("Rails と AI の会", "AI"));
    }

    [Theory]
    [InlineData(null, "AI")]
    [InlineData("", "AI")]
    [InlineData("AI 勉強会", "")]
    public void 空の入力は一致しない(string? text, string keyword) =>
        Assert.False(KeywordMatcher.Contains(text, keyword));
}

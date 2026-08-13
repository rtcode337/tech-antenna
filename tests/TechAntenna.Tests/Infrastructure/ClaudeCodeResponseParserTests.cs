using TechAntenna.Infrastructure.Summarization;

namespace TechAntenna.Tests.Infrastructure;

public class ClaudeCodeResponseParserTests
{
    [Fact]
    public void 応答から要約を取り出す()
    {
        const string text = """
            {"summaries":[
              {"index":1,"summary":"一つ目の要約"},
              {"index":2,"summary":"二つ目の要約"}]}
            """;

        var entries = ClaudeCodeResponseParser.Parse(text);

        Assert.Equal(2, entries.Count);
        Assert.Equal(1, entries[0].Index);
        Assert.Equal("一つ目の要約", entries[0].Text);
    }

    [Fact]
    public void 空の要約は落とす()
    {
        const string text = """
            {"summaries":[
              {"index":1,"summary":"  "},
              {"index":2,"summary":"二つ目の要約"}]}
            """;

        var entries = ClaudeCodeResponseParser.Parse(text);

        Assert.Single(entries);
        Assert.Equal(2, entries[0].Index);
    }

    [Fact]
    public void コードフェンスと前置きが付いていても読める()
    {
        // ブリッジ経由ではスキーマを強制できないので、装飾された応答が来ることがある
        const string text = """
            結果は以下のとおりです。

            ```json
            {"summaries":[{"index":1,"summary":"一つ目の要約"}]}
            ```
            """;

        var entries = ClaudeCodeResponseParser.Parse(text);

        Assert.Equal("一つ目の要約", Assert.Single(entries).Text);
    }

    [Fact]
    public void 想定した配列が無ければ例外にする()
    {
        // 別の形で返してきたときに、それらしいテキストを要約として紐づけない
        const string text = """{"result":"要約っぽいテキスト"}""";

        Assert.Throws<FormatException>(() => ClaudeCodeResponseParser.Parse(text));
    }

    [Fact]
    public void JSON_が無ければ例外にする()
    {
        var ex = Assert.Throws<FormatException>(
            () => ClaudeCodeResponseParser.Parse("認証に失敗しました"));

        // 原因を追えるよう、読めなかった本文を例外に載せる
        Assert.Contains("認証に失敗しました", ex.Message);
    }
}

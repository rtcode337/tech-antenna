using TechAntenna.Infrastructure.Summarization;

namespace TechAntenna.Tests.Infrastructure;

public class ClaudeCodeResponseParserTests
{
    [Fact]
    public void 構造化出力から要約を取り出す()
    {
        // claude -p --output-format json の応答(要約に関係しないフィールドは省いている)
        const string json = """
            {"is_error":false,"type":"result","total_cost_usd":0.34,
             "result":"…",
             "structured_output":{"summaries":[
               {"index":1,"summary":"一つ目の要約"},
               {"index":2,"summary":"二つ目の要約"}]}}
            """;

        var entries = ClaudeCodeResponseParser.Parse(json);

        Assert.Equal(2, entries.Count);
        Assert.Equal(1, entries[0].Index);
        Assert.Equal("一つ目の要約", entries[0].Summary);
    }

    [Fact]
    public void 空の要約は落とす()
    {
        const string json = """
            {"is_error":false,"structured_output":{"summaries":[
              {"index":1,"summary":"  "},
              {"index":2,"summary":"二つ目の要約"}]}}
            """;

        var entries = ClaudeCodeParse(json);

        Assert.Single(entries);
        Assert.Equal(2, entries[0].Index);
    }

    [Fact]
    public void is_error_なら原因つきで例外にする()
    {
        // claude は失敗の詳細を result と api_error_status に書く(subtype は success のまま)
        const string json = """
            {"is_error":true,"subtype":"success","api_error_status":401,
             "result":"Failed to authenticate. API Error: 401 OAuth access token is invalid."}
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => ClaudeCodeParse(json));
        Assert.Contains("OAuth access token is invalid", ex.Message);
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public void 終了コードだけ非ゼロのときも原因を取り出せる()
    {
        const string json = """
            {"is_error":true,"api_error_status":401,
             "result":"Failed to authenticate. API Error: 401 OAuth access token is invalid."}
            """;

        var detail = ClaudeCodeResponseParser.DescribeError(json);

        Assert.NotNull(detail);
        Assert.Contains("OAuth access token is invalid", detail);
    }

    [Fact]
    public void JSON_でない出力からは原因を取り出せない()
    {
        Assert.Null(ClaudeCodeResponseParser.DescribeError("segmentation fault"));
    }

    [Fact]
    public void 構造化出力が無ければ例外にする()
    {
        // --json-schema を渡しているのに structured_output が無いのは想定外。
        // result のテキストに勝手にフォールバックすると誤った要約を紐づけかねない
        const string json = """{"is_error":false,"result":"要約っぽいテキスト"}""";

        Assert.Throws<FormatException>(() => ClaudeCodeParse(json));
    }

    [Fact]
    public void JSON_でなければ例外にする()
    {
        Assert.Throws<FormatException>(() => ClaudeCodeParse("Error: not logged in"));
    }

    static IReadOnlyList<ClaudeCodeResponseParser.Entry> ClaudeCodeParse(string json) =>
        ClaudeCodeResponseParser.Parse(json);
}

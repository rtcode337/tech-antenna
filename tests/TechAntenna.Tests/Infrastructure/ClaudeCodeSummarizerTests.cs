using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Infrastructure.Summarization;

namespace TechAntenna.Tests.Infrastructure;

public class ClaudeCodeSummarizerTests
{
    static Article Article(string title, string? snippet) => new()
    {
        Title = title,
        Url = new Uri($"https://example.com/{Uri.EscapeDataString(title)}"),
        SourceName = "Zenn",
        ContentSnippet = snippet,
        CollectedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
    };

    static ClaudeCodeSummarizer Summarizer(IProcessRunner runner) =>
        new(runner, "claude", model: null, TimeSpan.FromSeconds(30));

    /// <summary>claude が返す JSON。要約は入力で振った 1 始まりの番号で戻ってくる。</summary>
    static string Response(params (int Index, string Summary)[] entries)
    {
        var items = entries.Select(e =>
            $$$"""{"index":{{{e.Index}}},"summary":"{{{e.Summary}}}"}""");
        return $$$"""
            {"is_error":false,"type":"result",
             "structured_output":{"summaries":[{{{string.Join(",", items)}}}]}}
            """;
    }

    [Fact]
    public async Task 番号で記事と要約を突き合わせる()
    {
        var articles = new[] { Article("一つ目", "本文A"), Article("二つ目", "本文B") };
        var runner = StubProcessRunner.Returning(Response((1, "Aの要約"), (2, "Bの要約")));

        var results = await Summarizer(runner).SummarizeAsync(articles);

        Assert.Equal("Aの要約", results.Single(r => r.ArticleId == articles[0].Id).Summary);
        Assert.Equal("Bの要約", results.Single(r => r.ArticleId == articles[1].Id).Summary);
    }

    [Fact]
    public async Task 記事をまとめて1回の呼び出しで処理する()
    {
        var articles = new[] { Article("一つ目", "本文A"), Article("二つ目", "本文B") };
        var runner = StubProcessRunner.Returning(Response((1, "Aの要約"), (2, "Bの要約")));

        await Summarizer(runner).SummarizeAsync(articles);

        // 呼び出し1回の固定費が大きいので、件数分だけ起動してはいけない
        Assert.Equal(1, runner.CallCount);
        Assert.Contains("本文A", runner.StandardInput);
        Assert.Contains("本文B", runner.StandardInput);
    }

    [Fact]
    public async Task 本文抜粋が無い記事は呼び出しに含めず空の要約で確定させる()
    {
        var articles = new[] { Article("材料なし", null), Article("材料あり", "本文B") };
        var runner = StubProcessRunner.Returning(Response((1, "Bの要約")));

        var results = await Summarizer(runner).SummarizeAsync(articles);

        Assert.Null(results.Single(r => r.ArticleId == articles[0].Id).Summary);
        Assert.Equal("Bの要約", results.Single(r => r.ArticleId == articles[1].Id).Summary);
        // 材料のある記事だけが番号 1 として渡る
        Assert.DoesNotContain("材料なし", runner.StandardInput);
    }

    [Fact]
    public async Task 範囲外の番号は捨てる()
    {
        var articles = new[] { Article("一つ目", "本文A") };
        // 存在しない記事 2 を返してきたケース。誤った記事に紐づけてはいけない
        var runner = StubProcessRunner.Returning(Response((1, "Aの要約"), (2, "幻の要約")));

        var results = await Summarizer(runner).SummarizeAsync(articles);

        Assert.Single(results);
        Assert.Equal("Aの要約", results[0].Summary);
    }

    [Fact]
    public async Task プロンプトは引数ではなく標準入力で渡す()
    {
        var articles = new[] { Article("一つ目", "本文A") };
        var runner = StubProcessRunner.Returning(Response((1, "Aの要約")));

        await Summarizer(runner).SummarizeAsync(articles);

        // 引数渡しは単一引数の長さ上限(128KiB)に当たるため
        Assert.DoesNotContain(runner.Arguments, a => a.Contains("本文A"));
        Assert.Contains("本文A", runner.StandardInput);
    }

    [Fact]
    public async Task ツールを禁じて1ターンに限定する()
    {
        var runner = StubProcessRunner.Returning(Response((1, "Aの要約")));

        await Summarizer(runner).SummarizeAsync([Article("一つ目", "本文A")]);

        Assert.Contains("-p", runner.Arguments);
        Assert.Contains("--max-turns", runner.Arguments);
        Assert.Contains("--disallowed-tools", runner.Arguments);
        Assert.Contains("--json-schema", runner.Arguments);
    }

    [Fact]
    public async Task モデルを指定したときだけ引数に載せる()
    {
        var runner = StubProcessRunner.Returning(Response((1, "Aの要約")));
        var summarizer = new ClaudeCodeSummarizer(
            runner, "claude", "claude-haiku-4-5", TimeSpan.FromSeconds(30));

        await summarizer.SummarizeAsync([Article("一つ目", "本文A")]);

        Assert.Contains("--model", runner.Arguments);
        Assert.Contains("claude-haiku-4-5", runner.Arguments);
    }

    [Fact]
    public async Task 制限時間切れは例外にする()
    {
        var runner = new StubProcessRunner(new ProcessResult(-1, "", "", TimedOut: true));

        await Assert.ThrowsAsync<TimeoutException>(
            () => Summarizer(runner).SummarizeAsync([Article("一つ目", "本文A")]));
    }

    [Fact]
    public async Task 終了コードが0でなければ例外にする()
    {
        var runner = new StubProcessRunner(new ProcessResult(1, "", "認証に失敗", TimedOut: false));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Summarizer(runner).SummarizeAsync([Article("一つ目", "本文A")]));
        Assert.Contains("認証に失敗", ex.Message);
    }

    [Fact]
    public async Task 材料のある記事が無ければ呼び出さない()
    {
        var runner = StubProcessRunner.Returning(Response());

        var results = await Summarizer(runner).SummarizeAsync([Article("材料なし", "  ")]);

        Assert.Equal(0, runner.CallCount);
        Assert.Null(results.Single().Summary);
    }
}

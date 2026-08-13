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

    /// <summary>ブリッジが返す本文。要約は入力で振った 1 始まりの番号で戻ってくる。</summary>
    static string Response(params (int Index, string Summary)[] entries)
    {
        var items = entries.Select(e =>
            $$$"""{"index":{{{e.Index}}},"summary":"{{{e.Summary}}}"}""");
        return $$$"""{"summaries":[{{{string.Join(",", items)}}}]}""";
    }

    [Fact]
    public async Task 番号で記事と要約を突き合わせる()
    {
        var articles = new[] { Article("一つ目", "本文A"), Article("二つ目", "本文B") };
        var bridge = new StubCliBridge(Response((1, "Aの要約"), (2, "Bの要約")));

        var results = await new ClaudeCodeSummarizer(bridge).SummarizeAsync(articles);

        Assert.Equal("Aの要約", results.Single(r => r.ArticleId == articles[0].Id).Summary);
        Assert.Equal("Bの要約", results.Single(r => r.ArticleId == articles[1].Id).Summary);
    }

    [Fact]
    public async Task 記事をまとめて1回の呼び出しで処理する()
    {
        var articles = new[] { Article("一つ目", "本文A"), Article("二つ目", "本文B") };
        var bridge = new StubCliBridge(Response((1, "Aの要約"), (2, "Bの要約")));

        await new ClaudeCodeSummarizer(bridge).SummarizeAsync(articles);

        // 呼び出し1回の固定費が大きいので、件数分だけ呼んではいけない
        Assert.Equal(1, bridge.CallCount);
        Assert.Contains("本文A", bridge.UserPrompt);
        Assert.Contains("本文B", bridge.UserPrompt);
    }

    [Fact]
    public async Task 本文抜粋が無い記事は呼び出しに含めず空の要約で確定させる()
    {
        var articles = new[] { Article("材料なし", null), Article("材料あり", "本文B") };
        var bridge = new StubCliBridge(Response((1, "Bの要約")));

        var results = await new ClaudeCodeSummarizer(bridge).SummarizeAsync(articles);

        Assert.Null(results.Single(r => r.ArticleId == articles[0].Id).Summary);
        Assert.Equal("Bの要約", results.Single(r => r.ArticleId == articles[1].Id).Summary);
        // 材料のある記事だけが番号 1 として渡る
        Assert.DoesNotContain("材料なし", bridge.UserPrompt);
    }

    [Fact]
    public async Task 範囲外の番号は捨てる()
    {
        var articles = new[] { Article("一つ目", "本文A") };
        // 存在しない記事 2 を返してきたケース。誤った記事に紐づけてはいけない
        var bridge = new StubCliBridge(Response((1, "Aの要約"), (2, "幻の要約")));

        var results = await new ClaudeCodeSummarizer(bridge).SummarizeAsync(articles);

        Assert.Single(results);
        Assert.Equal("Aの要約", results[0].Summary);
    }

    [Fact]
    public async Task スキーマはシステムプロンプトで指示する()
    {
        // ブリッジ経由では --json-schema を渡せないので、形はプロンプトで指定する
        var bridge = new StubCliBridge(Response((1, "Aの要約")));

        await new ClaudeCodeSummarizer(bridge).SummarizeAsync([Article("一つ目", "本文A")]);

        Assert.Contains("summaries", bridge.SystemPrompt);
        Assert.Contains("JSON", bridge.SystemPrompt);
    }

    [Fact]
    public async Task 前置きやコードフェンスが付いていても読める()
    {
        // 「JSON だけ」と指示していても説明を添えてくる応答はある。
        // そこで丸ごと捨てると1バッチ分の要約が消えるので、JSON の部分だけを取り出す
        var bridge = new StubCliBridge(
            "以下が結果です。\n```json\n" + Response((1, "Aの要約")) + "\n```");

        var results = await new ClaudeCodeSummarizer(bridge).SummarizeAsync([Article("一つ目", "本文A")]);

        Assert.Equal("Aの要約", results.Single().Summary);
    }

    [Fact]
    public async Task ブリッジの失敗はそのまま伝える()
    {
        // 呼び出し側(要約ジョブ)が失敗として記録し、次の巡回で引き直せるようにする
        var bridge = StubCliBridge.Failing(new TimeoutException("時間切れ"));

        await Assert.ThrowsAsync<TimeoutException>(
            () => new ClaudeCodeSummarizer(bridge).SummarizeAsync([Article("一つ目", "本文A")]));
    }

    [Fact]
    public async Task 読めない応答は例外にする()
    {
        var bridge = new StubCliBridge("認証に失敗しました");

        await Assert.ThrowsAsync<FormatException>(
            () => new ClaudeCodeSummarizer(bridge).SummarizeAsync([Article("一つ目", "本文A")]));
    }

    [Fact]
    public async Task 材料のある記事が無ければ呼び出さない()
    {
        var bridge = new StubCliBridge(Response());

        var results = await new ClaudeCodeSummarizer(bridge).SummarizeAsync([Article("材料なし", "  ")]);

        Assert.Equal(0, bridge.CallCount);
        Assert.Null(results.Single().Summary);
    }
}

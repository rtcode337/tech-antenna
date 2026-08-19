using TechAntenna.Core.Abstractions;
using TechAntenna.Infrastructure.Bridge;

namespace TechAntenna.Tests.Infrastructure;

/// <summary>
/// Claude Code の CLI をプロセスとして起動する経路。実際に claude は起動しない
/// (<see cref="StubProcessRunner"/> に差し替えて、渡す引数と失敗の扱いだけを見る)。
/// </summary>
public class ClaudeCodeCliBridgeTests
{
    static ClaudeCodeCliBridge Bridge(IProcessRunner runner, string? model = null) =>
        new(runner, "claude", model, TimeSpan.FromSeconds(30));

    [Fact]
    public async Task 本文は標準入力で渡す()
    {
        // 引数で渡すと Linux の単一引数の長さ上限(128KiB)を記事の束で超えて E2BIG になる
        var runner = StubProcessRunner.Returning("要約です");
        var bridge = Bridge(runner);

        var text = await bridge.RunAsync("システム", "記事の本文");

        Assert.Equal("要約です", text);
        Assert.Equal("記事の本文", runner.StandardInput);
        Assert.DoesNotContain("記事の本文", runner.Arguments);
    }

    [Fact]
    public async Task 道具を禁じてシステムプロンプトを引数で渡す()
    {
        var runner = StubProcessRunner.Returning("ok");

        await Bridge(runner).RunAsync("システム", "本文");

        Assert.Contains("-p", runner.Arguments);
        Assert.Contains("--system-prompt", runner.Arguments);
        Assert.Contains("システム", runner.Arguments);
        // 道具を許すと1ターンを道具の呼び出しに使い、結果が返らないことがある
        var disallowed = runner.Arguments[runner.Arguments.IndexOf("--disallowed-tools") + 1];
        Assert.Contains("WebSearch", disallowed);
        Assert.Contains("Bash", disallowed);
    }

    [Fact]
    public async Task モデルは指定したときだけ渡す()
    {
        var withModel = StubProcessRunner.Returning("ok");
        await Bridge(withModel, "claude-sonnet-5").RunAsync("s", "u");
        Assert.Contains("--model", withModel.Arguments);
        Assert.Contains("claude-sonnet-5", withModel.Arguments);

        // null は「CLI の既定に任せる」。空の --model を渡すと CLI が落ちる
        var withoutModel = StubProcessRunner.Returning("ok");
        await Bridge(withoutModel).RunAsync("s", "u");
        Assert.DoesNotContain("--model", withoutModel.Arguments);
    }

    [Fact]
    public void 名前にはモデルまで出す()
    {
        // どのモデルがサブスクの枠を使っているかを画面に見せるため
        Assert.Equal("Claude Code / claude-sonnet-5",
            Bridge(StubProcessRunner.Returning(""), "claude-sonnet-5").Name);
        // 既定に任せたときは、既定が何かこちらから分からないので付けない
        Assert.Equal("Claude Code", Bridge(StubProcessRunner.Returning("")).Name);
    }

    [Fact]
    public async Task 打ち切りはTimeoutExceptionにする()
    {
        // 呼び出し側(ジョブ)は次の巡回で引き直すので、失敗の種類が分かる形で投げる
        var runner = new StubProcessRunner(new ProcessResult(-1, "", "", TimedOut: true));

        await Assert.ThrowsAsync<TimeoutException>(() => Bridge(runner).RunAsync("s", "u"));
    }

    [Fact]
    public async Task 失敗したら理由を載せて投げる()
    {
        // 認証切れ・モデル名の間違いはここに出る(黙って空文字を返すと、要約が空で保存される)
        var runner = new StubProcessRunner(
            new ProcessResult(1, "", "Invalid API key", TimedOut: false));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Bridge(runner).RunAsync("s", "u"));

        Assert.Contains("Invalid API key", error.Message);
    }

    [Fact]
    public async Task 失敗の理由が標準出力にしか無いときもそれを載せる()
    {
        // CLI は失敗の詳細を stdout に書くことがある
        var runner = new StubProcessRunner(
            new ProcessResult(1, "credit balance is too low", "", TimedOut: false));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Bridge(runner).RunAsync("s", "u"));

        Assert.Contains("credit balance", error.Message);
    }
}

using System.Net;
using TechAntenna.Infrastructure.Chiezo;

namespace TechAntenna.Tests.Infrastructure;

public class ChiezoAiBridgeTests
{
    static ChiezoAiBridge Bridge(string? model, string responseBody)
    {
        var factory = new StubHttpClientFactory(responseBody);
        var client = new ChiezoAiClient(factory, "http://chiezo:7010", TimeSpan.FromSeconds(30));

        return new ChiezoAiBridge(
            client, new ChiezoAiSelection("claude", "Claude Code", model, null));
    }

    [Fact]
    public void モデルを選んでいればその名前で名乗る()
    {
        Assert.Equal("Claude Code / sonnet", Bridge("sonnet", "{}").Name);
    }

    [Fact]
    public async Task モデルを相手に任せたときは実際に使われたモデルで名乗る()
    {
        // **どの AI のどのモデルが書いたか**を残すため。呼び出しの前は分からないので、
        // 応答で名乗られたものを使う(生成者名を付けるのは応答を読んだ後)
        var bridge = Bridge(null, """{"content":"はい","model":"claude-sonnet-5"}""");

        Assert.Equal("Claude Code", bridge.Name);
        await bridge.RunAsync("システム", "本文");

        Assert.Equal("Claude Code / claude-sonnet-5", bridge.Name);
    }

    [Fact]
    public async Task 相手がモデルを名乗らなければ相手の名前だけにする()
    {
        var bridge = Bridge(null, """{"content":"はい"}""");

        await bridge.RunAsync("システム", "本文");

        Assert.Equal("Claude Code", bridge.Name);
    }
}

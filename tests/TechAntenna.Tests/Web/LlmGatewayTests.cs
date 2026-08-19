using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechAntenna.Infrastructure.Storage;
using TechAntenna.Web;
using TechAntenna.Tests.Infrastructure;
using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

public class LlmGatewayTests : IDisposable
{
    public void Dispose() => Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

    (LlmGateway Gateway, ApiCredentials Credentials) Build(string chiezoUrl = "")
    {
        var credentials = new ApiCredentials(
            new InMemorySecretStore(TimeProvider.System),
            new EphemeralDataProtectionProvider(),
            NullLogger<ApiCredentials>.Instance);
        var gateway = new LlmGateway(
            credentials,
            // CLI は起動しない(方式の選び方だけを見るテストなので、呼ばれない実行器を渡す)
            StubProcessRunner.Returning(""),
            Options.Create(new ClaudeCodeOptions()),
            Options.Create(new AnthropicOptions()),
            new ChiezoAi(
                new UnusedHttpClientFactory(),
                Options.Create(new ChiezoOptions { BaseUrl = chiezoUrl })),
            TimeProvider.System);
        return (gateway, credentials);
    }

    [Fact]
    public void 両方未設定なら使えない()
    {
        var (gateway, _) = Build();

        Assert.False(gateway.IsConfigured);
        Assert.Null(gateway.Summarizer);
        Assert.Null(gateway.DigestComposer);
    }

    [Fact]
    public async Task ClaudeCodeのトークンがあればClaudeCode方式()
    {
        var (gateway, credentials) = Build();
        await credentials.SetAsync(LlmGateway.ClaudeCodeTokenName, "token");
        await credentials.SetAsync(LlmGateway.AnthropicApiKeyName, "api-key");

        Assert.True(gateway.IsConfigured);
        // トークンがあるときは Anthropic API より優先(サブスクの枠を使う)
        Assert.Equal("Claude Code", gateway.Summarizer!.Name);
    }

    [Fact]
    public async Task APIキーだけならAnthropic方式()
    {
        var (gateway, credentials) = Build();
        await credentials.SetAsync(LlmGateway.AnthropicApiKeyName, "api-key");

        Assert.Equal("Anthropic API", gateway.Summarizer!.Name);
    }

    [Fact]
    public async Task 画面からキーを入れると再起動なしで使えるようになる()
    {
        var (gateway, credentials) = Build();
        Assert.False(gateway.IsConfigured);

        await credentials.SetAsync(LlmGateway.AnthropicApiKeyName, "api-key");

        Assert.True(gateway.IsConfigured);
        Assert.Equal("Anthropic API", gateway.Summarizer!.Name);
    }

    [Fact]
    public async Task キーが変わらなければ同じインスタンスを返し続ける()
    {
        var (gateway, credentials) = Build();
        await credentials.SetAsync(LlmGateway.AnthropicApiKeyName, "api-key");

        Assert.Same(gateway.Summarizer, gateway.Summarizer);
    }

    [Fact]
    public async Task ChiezoでメインのAIを選んでいればそちらを使う()
    {
        // **選んだ相手が最優先。** わざわざ選んだものより、同居のサイドカーを優先する理由が無い
        var (gateway, credentials) = Build(chiezoUrl: "http://chiezo:7010");
        await credentials.SetAsync(LlmGateway.ClaudeCodeTokenName, "token");
        await AiSettings.SaveAsync(
            credentials,
            new AiConfig(new AiChoice("gemini", "Gemini", "gemini-2.5-flash", null), []));

        Assert.True(gateway.IsConfigured);
        // 画面と生成者名にはモデルまで出す
        Assert.Equal("Gemini / gemini-2.5-flash", gateway.Summarizer!.Name);
    }

    [Fact]
    public async Task メインを選んでいなければ従来の方式のまま()
    {
        // URL を入れただけでは切り替わらない(誰に頼むかが決まっていない)
        var (gateway, credentials) = Build(chiezoUrl: "http://chiezo:7010");
        await credentials.SetAsync(LlmGateway.ClaudeCodeTokenName, "token");

        Assert.Equal("Claude Code", gateway.Summarizer!.Name);
    }

    [Fact]
    public async Task メインにAnthropicAPIを選べばトークンがあってもそちらを使う()
    {
        // **これが選べるようになった理由。** かつては「トークン > API キー」の優先順しか無く、
        // 両方入れてある環境で Anthropic API を使うにはトークンを消すしかなかった
        var (gateway, credentials) = Build();
        await credentials.SetAsync(LlmGateway.ClaudeCodeTokenName, "token");
        await credentials.SetAsync(LlmGateway.AnthropicApiKeyName, "api-key");
        await AiSettings.SaveAsync(
            credentials,
            new AiConfig(
                new AiChoice(AiSettings.AnthropicBackend, "Anthropic API(従量課金)", null, null), []));

        Assert.Equal("Anthropic API", gateway.Summarizer!.Name);
    }

    [Fact]
    public async Task メインにClaudeCodeを選べばChiezoが設定済みでもそちらを使う()
    {
        var (gateway, credentials) = Build(chiezoUrl: "http://chiezo:7010");
        await credentials.SetAsync(LlmGateway.ClaudeCodeTokenName, "token");
        await AiSettings.SaveAsync(
            credentials,
            new AiConfig(
                new AiChoice(AiSettings.ClaudeCodeBackend, "Claude Code(サブスクの枠)", null, null), []));

        Assert.Equal("Claude Code", gateway.Summarizer!.Name);
    }

    [Fact]
    public async Task 選んだ相手のキーが消えていれば従来の優先順に落ちる()
    {
        // 選択は残っているがキーが無い状態(画面で削除した)。**動かない相手のまま止まらない**
        var (gateway, credentials) = Build();
        await credentials.SetAsync(LlmGateway.AnthropicApiKeyName, "api-key");
        await AiSettings.SaveAsync(
            credentials,
            new AiConfig(
                new AiChoice(AiSettings.ClaudeCodeBackend, "Claude Code(サブスクの枠)", null, null), []));

        Assert.Equal("Anthropic API", gateway.Summarizer!.Name);
    }

    [Fact]
    public async Task メインがローカルの相手でもChiezoのサブは並ぶ()
    {
        // 読み比べはサブの役目なので、メインが Claude Code でも Chiezo のサブは効く
        var (gateway, credentials) = Build(chiezoUrl: "http://chiezo:7010");
        await credentials.SetAsync(LlmGateway.ClaudeCodeTokenName, "token");
        await AiSettings.SaveAsync(
            credentials,
            new AiConfig(
                new AiChoice(AiSettings.ClaudeCodeBackend, "Claude Code(サブスクの枠)", null, null),
                [new AiChoice("gemini", "Gemini", null, null)]));

        var generators = gateway.DigestGenerators;
        // ローカルの相手の生成者キーは従来と同じ default(過去のダイジェストと揃える)
        Assert.Equal(["default", "chiezo:gemini"], generators.Select(g => g.Key));
        Assert.True(generators[0].IsPrimary);
        Assert.Equal("Claude Code", generators[0].Name);
    }

    [Fact]
    public async Task サブのAIはサマリーの生成者にだけ並ぶ()
    {
        // 比べたいのは文章。要約や翻訳まで相手の数だけ走らせない
        var (gateway, credentials) = Build(chiezoUrl: "http://chiezo:7010");
        await AiSettings.SaveAsync(
            credentials,
            new AiConfig(
                new AiChoice("gemini", "Gemini", null, null),
                [new AiChoice("claude", "Claude Code", "sonnet", "high")]));

        var generators = gateway.DigestGenerators;
        Assert.Equal(["chiezo:gemini", "chiezo:claude"], generators.Select(g => g.Key));
        Assert.True(generators[0].IsPrimary);
        Assert.False(generators[1].IsPrimary);
        Assert.Equal("Claude Code / sonnet", generators[1].Name);
        // 要約はメインだけ
        Assert.Equal("Gemini", gateway.Summarizer!.Name);
    }
}

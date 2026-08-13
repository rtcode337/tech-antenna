using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechAntenna.Infrastructure.Storage;
using TechAntenna.Web;
using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

public class LlmGatewayTests : IDisposable
{
    // ブリッジと共有する設定 DB の書き出し先。テストの成果物ディレクトリを汚さないよう
    // 一時ディレクトリに逃がす(トークンを設定すると実際に書かれる)
    readonly string _stateDirectory = Path.Combine(
        Path.GetTempPath(), "tech-antenna-gateway-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_stateDirectory))
        {
            Directory.Delete(_stateDirectory, recursive: true);
        }
    }

    (LlmGateway Gateway, ApiCredentials Credentials) Build(string chiezoUrl = "")
    {
        var credentials = new ApiCredentials(
            new InMemorySecretStore(TimeProvider.System),
            new EphemeralDataProtectionProvider(),
            NullLogger<ApiCredentials>.Instance);
        var gateway = new LlmGateway(
            credentials,
            new UnusedHttpClientFactory(),
            Options.Create(new ClaudeCodeOptions { StateDirectory = _stateDirectory }),
            Options.Create(new AnthropicOptions()),
            new ChiezoAi(
                new UnusedHttpClientFactory(),
                Options.Create(new ChiezoOptions { BaseUrl = chiezoUrl })),
            TimeProvider.System,
            NullLogger<LlmGateway>.Instance);
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
    public async Task ClaudeCodeのトークンはブリッジが読む設定DBへ書き出す()
    {
        // CLI は別コンテナ(ブリッジ)で動くので、このプロセスの環境変数では渡せない
        var (gateway, credentials) = Build();
        await credentials.SetAsync(LlmGateway.ClaudeCodeTokenName, "token");

        _ = gateway.Summarizer;

        Assert.True(File.Exists(Path.Combine(_stateDirectory, "settings.db")));
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

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechAntenna.Infrastructure.Storage;
using TechAntenna.Web;
using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

public class LlmGatewayTests
{
    static (LlmGateway Gateway, ApiCredentials Credentials) Build()
    {
        var credentials = new ApiCredentials(
            new InMemorySecretStore(TimeProvider.System),
            new EphemeralDataProtectionProvider(),
            NullLogger<ApiCredentials>.Instance);
        var gateway = new LlmGateway(
            credentials,
            Options.Create(new ClaudeCodeOptions()),
            Options.Create(new AnthropicOptions()),
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
}

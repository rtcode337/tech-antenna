using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using TechAntenna.Infrastructure.Storage;
using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

public class AiSettingsTests
{
    static ApiCredentials Credentials() => new(
        new InMemorySecretStore(TimeProvider.System),
        new EphemeralDataProtectionProvider(),
        NullLogger<ApiCredentials>.Instance);

    [Fact]
    public async Task 保存した選択をそのまま読み直せる()
    {
        var credentials = Credentials();
        var config = new AiConfig(
            new AiChoice("gemini", "Gemini", "gemini-2.5-flash", null),
            [new AiChoice("claude", "Claude Code", "sonnet", "high")]);

        await AiSettings.SaveAsync(credentials, config);

        var loaded = AiSettings.Load(credentials);
        Assert.Equal(config.Main, loaded.Main);
        Assert.Equal(config.Subs, loaded.Subs);
    }

    [Fact]
    public async Task メインを外したら設定ごと消す()
    {
        // 「サブだけ」は成り立たない —— 誰が本命かが決まらない
        var credentials = Credentials();
        await AiSettings.SaveAsync(
            credentials, new AiConfig(new AiChoice("gemini", "Gemini", null, null), []));

        await AiSettings.SaveAsync(credentials, AiConfig.Empty);

        Assert.Null(AiSettings.Load(credentials).Main);
        Assert.False(credentials.Has(AiSettings.ConfigName));
    }

    [Fact]
    public void 壊れた値は未設定として扱う()
    {
        // 形を変えたときに、画面から選び直せなくならないようにする
        var credentials = Credentials();

        Assert.Null(AiSettings.Load(credentials).Main);
    }

    [Fact]
    public void メインと同じ相手はサブから落とす()
    {
        // 同じ相手に2回書かせても比べる意味が無い
        var config = new AiConfig(
            new AiChoice("gemini", "Gemini", null, null),
            [new AiChoice("gemini", "Gemini", null, null), new AiChoice("claude", "Claude Code", null, null)]);

        Assert.Equal(["gemini", "claude"], config.All().Select(choice => choice.Backend));
    }
}

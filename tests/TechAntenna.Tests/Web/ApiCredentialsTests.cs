using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using TechAntenna.Infrastructure.Storage;
using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

public class ApiCredentialsTests
{
    static ApiCredentials Credentials(
        InMemorySecretStore? store = null,
        IDataProtectionProvider? protection = null) => new(
        store ?? new InMemorySecretStore(TimeProvider.System),
        protection ?? new EphemeralDataProtectionProvider(),
        NullLogger<ApiCredentials>.Instance);

    [Fact]
    public async Task 保存した値を引ける()
    {
        var credentials = Credentials();

        Assert.Null(credentials.Get("Connpass:ApiKey"));
        Assert.False(credentials.Has("Connpass:ApiKey"));

        await credentials.SetAsync("Connpass:ApiKey", "画面の値");

        Assert.Equal("画面の値", credentials.Get("Connpass:ApiKey"));
        Assert.True(credentials.Has("Connpass:ApiKey"));
    }

    [Fact]
    public async Task 削除すると未設定へ戻る()
    {
        var credentials = Credentials();
        await credentials.SetAsync("Connpass:ApiKey", "画面の値");

        await credentials.RemoveAsync("Connpass:ApiKey");

        Assert.Null(credentials.Get("Connpass:ApiKey"));
    }

    [Fact]
    public async Task 前後の空白は落として保存する()
    {
        var credentials = Credentials();

        await credentials.SetAsync("Connpass:ApiKey", "  値  ");

        Assert.Equal("値", credentials.Get("Connpass:ApiKey"));
    }

    [Fact]
    public async Task 保存すると版数が進む_LLMゲートウェイの組み直しの合図()
    {
        var credentials = Credentials();
        var before = credentials.Version;

        await credentials.SetAsync("Anthropic:ApiKey", "key");

        Assert.True(credentials.Version > before);
    }

    [Fact]
    public async Task 値は暗号化されて保存される()
    {
        var store = new InMemorySecretStore(TimeProvider.System);
        var credentials = Credentials(store);

        await credentials.SetAsync("Connpass:ApiKey", "秘密の値");

        var saved = Assert.Single(await store.GetAllAsync());
        Assert.DoesNotContain("秘密の値", saved.Value);
    }

    [Fact]
    public async Task 復号できない値は未設定として扱う_鍵が変わった場合()
    {
        // 別の鍵(別の protector)で保存された状態を作る = 鍵ディレクトリを
        // 永続化せずコンテナを作り直した状況
        var store = new InMemorySecretStore(TimeProvider.System);
        await Credentials(store).SetAsync("Connpass:ApiKey", "旧環境の値");

        var reborn = Credentials(store, new EphemeralDataProtectionProvider());
        await reborn.RefreshAsync();

        Assert.Null(reborn.Get("Connpass:ApiKey"));
        // 行は消さない(鍵を戻せば読める可能性を残す)
        Assert.Single(await store.GetAllAsync());
    }
}

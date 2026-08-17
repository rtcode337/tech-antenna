using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using TechAntenna.Infrastructure.Storage;
using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

/// <summary>
/// 面掃きのオン/オフ。**既定は無効**で、画面で入れたときだけ走る ——
/// 1 回の収集で数十リクエストかかるので、消し忘れたサーバーが叩き続けないため。
/// </summary>
public class SweepSettingsTests
{
    static ApiCredentials Credentials() => new(
        new InMemorySecretStore(TimeProvider.System),
        new EphemeralDataProtectionProvider(),
        NullLogger<ApiCredentials>.Instance);

    [Fact]
    public void 未設定なら無効()
    {
        var credentials = Credentials();

        Assert.False(SweepSettings.IsEnabled(credentials, SweepSettings.ConnpassName));
        Assert.False(SweepSettings.IsEnabled(credentials, SweepSettings.DoorkeeperName));
    }

    [Fact]
    public async Task 画面で入れると有効になり外すと既定へ戻る()
    {
        var credentials = Credentials();

        await SweepSettings.SetAsync(credentials, SweepSettings.ConnpassName, true);
        Assert.True(SweepSettings.IsEnabled(credentials, SweepSettings.ConnpassName));
        // 収集元は片方ずつ入れられる(両方を一度に動かすとリクエストが倍かかる)
        Assert.False(SweepSettings.IsEnabled(credentials, SweepSettings.DoorkeeperName));

        await SweepSettings.SetAsync(credentials, SweepSettings.ConnpassName, false);
        Assert.False(SweepSettings.IsEnabled(credentials, SweepSettings.ConnpassName));
        // 外したときは行ごと消す(「無効」を保存しておく必要が無い)
        Assert.False(credentials.Has(SweepSettings.ConnpassName));
    }

    [Fact]
    public async Task 想定外の値は有効と見なさない()
    {
        // 走らせるかどうかは相手を叩く量に直結するので、読めない値は無効側に倒す
        var credentials = Credentials();
        await credentials.SetAsync(SweepSettings.ConnpassName, "yes");

        Assert.False(SweepSettings.IsEnabled(credentials, SweepSettings.ConnpassName));
    }
}

using Microsoft.Extensions.Time.Testing;
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

    [Fact]
    public void 掃いた記録が無ければ回る()
    {
        // **初回は必ず回る。** 入れた直後に何も起きないと、動かしたつもりが効いていないのか、
        // まだ時間ではないのかが画面から見分けられない
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero));

        Assert.True(SweepSettings.IsDue(Credentials(), clock, SweepSettings.ConnpassName));
    }

    [Fact]
    public async Task 掃いた直後は回らず一日たつと回る()
    {
        // 面掃きは1回で最大20リクエスト。**中身は1日でほとんど変わらない**ので、
        // 同じ日に何度収集しても掃き直さない
        var credentials = Credentials();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));

        await SweepSettings.MarkRunAsync(credentials, clock, SweepSettings.ConnpassName, full: true);

        Assert.False(SweepSettings.IsDue(credentials, clock, SweepSettings.ConnpassName));

        clock.Advance(TimeSpan.FromHours(23));
        Assert.False(SweepSettings.IsDue(credentials, clock, SweepSettings.ConnpassName));

        clock.Advance(TimeSpan.FromHours(1));
        Assert.True(SweepSettings.IsDue(credentials, clock, SweepSettings.ConnpassName));
    }

    [Fact]
    public async Task 相手ごとに別の記録を持つ()
    {
        // connpass を掃いた記録で Doorkeeper まで止まると、片方だけが更新されなくなる
        var credentials = Credentials();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));

        await SweepSettings.MarkRunAsync(credentials, clock, SweepSettings.ConnpassName, full: true);

        Assert.False(SweepSettings.IsDue(credentials, clock, SweepSettings.ConnpassName));
        Assert.True(SweepSettings.IsDue(credentials, clock, SweepSettings.DoorkeeperName));
    }

    [Fact]
    public void 記録が無ければ全掃き()
    {
        // 初回は差分の起点が無い(何を持っているか分からない)ので、期間を数え上げる
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero));

        Assert.Null(SweepSettings.IncrementalSince(Credentials(), clock, SweepSettings.ConnpassName));
    }

    [Fact]
    public async Task 二回目からは差分で引き一週間たつと全掃きへ戻る()
    {
        // **差分では拾えないものがある** —— 参加者数が伸びてしきい値を越えたイベントは
        // 公開日が変わらないので、公開日での差分に出てこない。だから週に一度は数え直す
        var credentials = Credentials();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));
        var first = clock.GetUtcNow();

        await SweepSettings.MarkRunAsync(credentials, clock, SweepSettings.ConnpassName, full: true);

        clock.Advance(TimeSpan.FromHours(12));
        Assert.Equal(first, SweepSettings.IncrementalSince(credentials, clock, SweepSettings.ConnpassName));

        // 差分で掃いたときは「最後の全掃き」の記録は動かさない
        await SweepSettings.MarkRunAsync(credentials, clock, SweepSettings.ConnpassName, full: false);
        var second = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(second, SweepSettings.IncrementalSince(credentials, clock, SweepSettings.ConnpassName));

        // 全掃きから1週間たったら、また数え上げる
        clock.Advance(TimeSpan.FromDays(7));
        Assert.Null(SweepSettings.IncrementalSince(credentials, clock, SweepSettings.ConnpassName));
    }
}

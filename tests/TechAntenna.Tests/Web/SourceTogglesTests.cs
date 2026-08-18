using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using TechAntenna.Infrastructure.Storage;
using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

/// <summary>
/// 収集元1つ1つのオン/オフ。**既定は有効**で、止めたものだけを保存する ——
/// 面掃き(<see cref="SweepSettings"/>)とは既定が逆。収集元は普通に使うものなので、
/// 既定を無効にすると新しい収集元を足すたびに「入れたのに集まらない」が起きる。
/// </summary>
public class SourceTogglesTests
{
    static ApiCredentials Credentials() => new(
        new InMemorySecretStore(TimeProvider.System),
        new EphemeralDataProtectionProvider(),
        NullLogger<ApiCredentials>.Instance);

    /// <summary>何も止めていない状態(他のテストが収集ランナーを組み立てるのに使う)。</summary>
    public static SourceToggles AllEnabled() => new(Credentials());

    [Fact]
    public void 未設定なら有効()
    {
        var toggles = new SourceToggles(Credentials());

        Assert.True(toggles.IsEnabled(SourceToggles.Article, "Zenn"));
    }

    [Fact]
    public async Task 止めると無効になり動かすと既定へ戻る()
    {
        var credentials = Credentials();
        var toggles = new SourceToggles(credentials);
        var key = SourceToggles.KeyOf(SourceToggles.Article, "Zenn");

        await toggles.SetAsync(key, enabled: false);
        Assert.False(toggles.IsEnabled(SourceToggles.Article, "Zenn"));

        await toggles.SetAsync(key, enabled: true);
        Assert.True(toggles.IsEnabled(SourceToggles.Article, "Zenn"));
        // 動かすときは行ごと消す(既定＝有効に戻す)
        Assert.False(credentials.Has(key));
    }

    [Fact]
    public async Task 同じ名前でも役割が違えば別の収集元()
    {
        // `Qiita` は推薦本(定番の書籍)と話題度(トピック)の両方にある。
        // **名前だけを鍵にすると、片方を止めたときにもう片方まで止まる**
        var toggles = new SourceToggles(Credentials());

        await toggles.SetAsync(SourceToggles.KeyOf(SourceToggles.Trend, "Qiita"), enabled: false);

        Assert.False(toggles.IsEnabled(SourceToggles.Trend, "Qiita"));
        Assert.True(toggles.IsEnabled(SourceToggles.Recommendation, "Qiita"));
    }

    [Fact]
    public async Task 止めた収集元は一覧から外れる()
    {
        var toggles = new SourceToggles(Credentials());
        string[] sources = ["Zenn", "Qiita", "Publickey"];

        await toggles.SetAsync(SourceToggles.KeyOf(SourceToggles.Article, "Qiita"), enabled: false);

        Assert.Equal(
            ["Zenn", "Publickey"],
            toggles.Enabled(sources, SourceToggles.Article, name => name));
    }

    [Fact]
    public void 面掃きの設定とは見分けられる()
    {
        // 画面の切り替えは1つのボタンで両方を扱うので、保存先を接頭辞で振り分けている
        Assert.True(SourceToggles.IsSourceKey(
            SourceToggles.KeyOf(SourceToggles.Event, "connpass")));
        Assert.False(SourceToggles.IsSourceKey(SweepSettings.ConnpassName));
    }
}

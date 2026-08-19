using System.Globalization;
using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

public class ScheduleSettingsTests
{
    static DateTimeOffset Jst(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    // 打ち方(区切り・桁)の違いで弾かない。保存する形は1つにそろえる
    [Theory]
    [InlineData("07:00, 19:00")]
    [InlineData("07:00 19:00")]
    [InlineData("7:00、19:00")]
    [InlineData("19:00,07:00")]
    [InlineData(" 19:00\n07:00\n07:00 ")]
    public void 区切りや順番が違っても同じ時刻として読む(string input)
    {
        Assert.True(ScheduleSettings.TryParseTimes(input, out var times, out var error));
        Assert.Null(error);
        Assert.Equal("07:00, 19:00", ScheduleSettings.Format(times));
    }

    [Fact]
    public void 空なら時刻なし_定期実行しない()
    {
        Assert.True(ScheduleSettings.TryParseTimes("", out var times, out _));
        Assert.Empty(times);
        Assert.False(ScheduleSettings.IsDue(times, Jst("2026-08-10T07:00:00+09:00"), Jst("2026-08-11T23:00:00+09:00")));
        Assert.Null(ScheduleSettings.NextOccurrence(times, Jst("2026-08-10T07:00:00+09:00")));
    }

    // 読めない値で一部だけ保存すると、画面の表示と実際に走る設定が食い違う
    [Theory]
    [InlineData("25:00")]
    [InlineData("7時")]
    [InlineData("07:00, ほげ")]
    public void 読めない時刻は理由付きで弾く(string input)
    {
        Assert.False(ScheduleSettings.TryParseTimes(input, out var times, out var error));
        Assert.Empty(times);
        Assert.NotNull(error);
    }

    [Fact]
    public void 上限を超える個数は弾く()
    {
        var many = string.Join(",", Enumerable.Range(0, 25).Select(i => $"{i % 24:00}:{i:00}"));

        Assert.False(ScheduleSettings.TryParseTimes(many, out _, out var error));
        Assert.Contains($"{ScheduleSettings.MaxTimes}", error);
    }

    [Fact]
    public void 時刻を跨いだら走る()
    {
        ScheduleSettings.TryParseTimes("07:00, 19:00", out var times, out _);

        // 前回 06:00 → いま 07:30。07:00 を跨いでいる
        Assert.True(ScheduleSettings.IsDue(times, Jst("2026-08-10T06:00:00+09:00"), Jst("2026-08-10T07:30:00+09:00")));
    }

    [Fact]
    public void 跨いでいなければ走らない()
    {
        ScheduleSettings.TryParseTimes("07:00, 19:00", out var times, out _);

        // 前回 07:00 → いま 18:59。次は 19:00 でまだ来ていない
        Assert.False(ScheduleSettings.IsDue(times, Jst("2026-08-10T07:00:00+09:00"), Jst("2026-08-10T18:59:00+09:00")));
    }

    // 時刻は日本時間で解釈する。UTC で持っている値を渡しても結果は変わらない
    [Fact]
    public void 判定は日本時間で行う()
    {
        ScheduleSettings.TryParseTimes("07:00", out var times, out _);

        // 前回 21:00Z(= 8/10 06:00 JST)。22:01Z は 8/10 07:01 JST なので 07:00 を跨いだ
        var lastRun = Jst("2026-08-09T21:00:00+00:00");
        Assert.True(ScheduleSettings.IsDue(times, lastRun, Jst("2026-08-09T22:01:00+00:00")));
        // 21:59Z は 8/10 06:59 JST。まだ来ていない
        Assert.False(ScheduleSettings.IsDue(times, lastRun, Jst("2026-08-09T21:59:00+00:00")));
    }

    // 止まっていたあいだに何度跨いでいても、走るのは1回(判定は真偽値なので回数を持たない)
    [Fact]
    public void 何日止まっていても1回だけ走る()
    {
        ScheduleSettings.TryParseTimes("07:00, 19:00", out var times, out _);

        Assert.True(ScheduleSettings.IsDue(times, Jst("2026-08-01T07:00:00+09:00"), Jst("2026-08-10T12:00:00+09:00")));
    }

    [Fact]
    public void 次の実行時刻は最も早いものを返す()
    {
        ScheduleSettings.TryParseTimes("07:00, 19:00", out var times, out _);

        Assert.Equal(
            Jst("2026-08-10T19:00:00+09:00"),
            ScheduleSettings.NextOccurrence(times, Jst("2026-08-10T12:00:00+09:00")));

        // その日の最後を過ぎたら翌日の最初
        Assert.Equal(
            Jst("2026-08-11T07:00:00+09:00"),
            ScheduleSettings.NextOccurrence(times, Jst("2026-08-10T20:00:00+09:00")));
    }

    // 設定キーはジョブごとに分かれる(片方を切ってももう片方は残る)
    // 「次にいつ何件走るか」の1行は、画面とチェックのその場保存(API)で同じものを出す。
    // 2か所で組むと、チェックを入れた直後の画面だけ古い文言が残る
    [Fact]
    public void 時刻が無ければ定期実行しないと言う()
    {
        Assert.Equal(
            "いまは定期実行しません（時刻が未設定）。",
            ScheduleSettings.Describe([], 3, Jst("2026-08-19T10:00:00+09:00")));
    }

    [Fact]
    public void 時刻はあってもチェックが無ければそう言う()
    {
        // 設定できているつもりで動いていない、に気づけるようにする
        Assert.Equal(
            "時刻は設定されていますが、走るジョブがありません（チェックが1つも入っていません）。",
            ScheduleSettings.Describe([new TimeOnly(7, 0)], 0, Jst("2026-08-19T10:00:00+09:00")));
    }

    [Fact]
    public void 次に走る時刻と件数を日本時間で言う()
    {
        Assert.Equal(
            "次は 2026-08-19 19:00 JST に 2 件のジョブが走ります。",
            ScheduleSettings.Describe(
                [new TimeOnly(7, 0), new TimeOnly(19, 0)], 2, Jst("2026-08-19T10:00:00+09:00")));
    }

    [Fact]
    public void ジョブごとに設定キーが分かれる()
    {
        Assert.Equal("Schedule:Job:digest", ScheduleSettings.EnabledName("digest"));
        Assert.NotEqual(
            ScheduleSettings.EnabledName("summary"), ScheduleSettings.EnabledName("digest"));
    }
}

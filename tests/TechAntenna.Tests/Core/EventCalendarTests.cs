using TechAntenna.Core;
using TechAntenna.Core.Models;

namespace TechAntenna.Tests.Core;

public class EventCalendarTests
{
    static TechEvent Event(string title, DateTimeOffset startsAt) =>
        new()
        {
            Title = title,
            Url = new Uri($"https://example.com/{title}"),
            SourceName = "test",
            StartsAt = startsAt,
            CollectedAt = DateTimeOffset.UnixEpoch,
        };

    /// <summary>日本時間の日時。収集元は時差付きで返すので、テストもその形で書く。</summary>
    static DateTimeOffset Jst(int year, int month, int day, int hour = 19) =>
        new(year, month, day, hour, 0, 0, JapanTime.Offset);

    [Fact]
    public void 月キーを読み書きできる()
    {
        Assert.True(EventCalendar.TryParseKey("2026-09", out var year, out var month));
        Assert.Equal((2026, 9), (year, month));
        Assert.Equal("2026-09", EventCalendar.FormatKey(2026, 9));

        Assert.False(EventCalendar.TryParseKey("2026/09", out _, out _));
        Assert.False(EventCalendar.TryParseKey(null, out _, out _));
    }

    [Fact]
    public void 週は日曜始まりで7列にそろう()
    {
        var calendar = EventCalendar.Build(2026, 8, [], Jst(2026, 8, 11));

        Assert.All(calendar.Weeks, week => Assert.Equal(7, week.Count));
        // 2026-08-01 は土曜なので、最初の週は 7/26(日)から始まる
        Assert.Equal(new DateOnly(2026, 7, 26), calendar.Weeks[0][0].Date);
        Assert.Equal(DayOfWeek.Sunday, calendar.Weeks[0][0].Date.DayOfWeek);
        // 最後のマスは土曜で終わる
        var last = calendar.Weeks[^1][^1];
        Assert.Equal(DayOfWeek.Saturday, last.Date.DayOfWeek);
        Assert.True(last.Date >= new DateOnly(2026, 8, 31));
    }

    [Fact]
    public void 前後の月からはみ出したマスに印を付ける()
    {
        var calendar = EventCalendar.Build(2026, 8, [], Jst(2026, 8, 11));

        Assert.False(calendar.Weeks[0][0].InMonth);
        Assert.True(calendar.Weeks[0].Single(day => day.Date == new DateOnly(2026, 8, 1)).InMonth);
    }

    [Fact]
    public void イベントを日本時間の日付に振り分ける()
    {
        // UTC のままだと 8/9 に入ってしまう時刻(JST では 8/10 の朝 8 時)
        var early = Event("朝のイベント", new DateTimeOffset(2026, 8, 9, 23, 0, 0, TimeSpan.Zero));
        var evening = Event("夜のイベント", Jst(2026, 8, 10, 19));

        var calendar = EventCalendar.Build(2026, 8, [evening, early], Jst(2026, 8, 1));

        var day = calendar.Weeks
            .SelectMany(week => week)
            .Single(d => d.Date == new DateOnly(2026, 8, 10));
        // 同じ日のイベントは開始の早い順
        Assert.Equal(["朝のイベント", "夜のイベント"], day.Events.Select(e => e.Title));
    }

    [Fact]
    public void 今日の印は日本時間で付ける()
    {
        // UTC では 8/10、日本時間では 8/11
        var calendar = EventCalendar.Build(2026, 8, [], new DateTimeOffset(2026, 8, 10, 22, 0, 0, TimeSpan.Zero));

        var today = calendar.Weeks.SelectMany(week => week).Single(day => day.IsToday);
        Assert.Equal(new DateOnly(2026, 8, 11), today.Date);
    }

    [Fact]
    public void マスの範囲は前後の月にはみ出す()
    {
        var (from, to) = EventCalendar.GridRange(2026, 8);

        // 8/1(土)の週は 7/26(日)から
        Assert.Equal(new DateTimeOffset(2026, 7, 26, 0, 0, 0, JapanTime.Offset), from);
        // 8/31(月)の週は 9/5(土)まで(終端は翌日の 0 時)
        Assert.Equal(new DateTimeOffset(2026, 9, 6, 0, 0, 0, JapanTime.Offset), to);
    }

    [Fact]
    public void 月の範囲は日本時間の月初から翌月初まで()
    {
        var (from, to) = EventCalendar.MonthRange(2026, 8);

        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, JapanTime.Offset), from);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, JapanTime.Offset), to);
    }

    [Fact]
    public void 前後の月へのリンクは年をまたぐ()
    {
        var january = EventCalendar.Build(2026, 1, [], Jst(2026, 1, 5));

        Assert.Equal("2025-12", january.PreviousKey);
        Assert.Equal("2026-02", january.NextKey);
        Assert.Equal("2026年1月", january.Title);
    }
}

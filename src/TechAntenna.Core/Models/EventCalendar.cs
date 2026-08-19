using System.Globalization;

namespace TechAntenna.Core.Models;

/// <summary>カレンダーの1日ぶん。</summary>
/// <param name="Date">その日(日本時間)。</param>
/// <param name="InMonth">表示中の月の日か(前後の月からはみ出したマスは false)。</param>
/// <param name="IsToday">今日(日本時間)か。</param>
/// <param name="Events">その日に始まるイベント(開始の早い順)。</param>
public record CalendarDay(DateOnly Date, bool InMonth, bool IsToday, IReadOnlyList<TechEvent> Events);

/// <summary>カレンダーの1か月。週(日曜始まり)の並びを持つ。</summary>
public record CalendarMonth(int Year, int Month, IReadOnlyList<IReadOnlyList<CalendarDay>> Weeks)
{
    /// <summary>見出し(<c>2026年8月</c>)。</summary>
    public string Title => $"{Year}年{Month}月";

    /// <summary>URL に載せる形(<c>2026-08</c>)。</summary>
    public string Key => EventCalendar.FormatKey(Year, Month);

    public string PreviousKey => EventCalendar.FormatKey(new DateOnly(Year, Month, 1).AddMonths(-1));

    public string NextKey => EventCalendar.FormatKey(new DateOnly(Year, Month, 1).AddMonths(1));
}

/// <summary>
/// イベントの月カレンダーを組み立てる。
///
/// 日付の境界はすべて日本時間で数える(<see cref="JapanTime"/>)—— UTC のまま日付に
/// 直すと、日本の朝 9 時までに始まるイベントが前日のマスに入る。
/// 画面(Razor)ではなくここに置いてあるのは、月をまたぐマスの埋め方や週の折り返しを
/// テストできるようにするため。
/// </summary>
public static class EventCalendar
{
    /// <summary>週の始まりは日曜(日本のカレンダーの一般的な形)。</summary>
    const DayOfWeek FirstDayOfWeek = DayOfWeek.Sunday;

    /// <summary>曜日の見出し(日曜始まり)。</summary>
    public static IReadOnlyList<string> DayNames { get; } = ["日", "月", "火", "水", "木", "金", "土"];

    /// <summary><c>2026-08</c> の形。URL のクエリと画面のリンクで同じ表記を使う。</summary>
    public static string FormatKey(int year, int month) =>
        string.Create(CultureInfo.InvariantCulture, $"{year:D4}-{month:D2}");

    public static string FormatKey(DateOnly date) => FormatKey(date.Year, date.Month);

    /// <summary>
    /// クエリの <c>?month=2026-08</c> を読む。読めなければ false ——
    /// 既定の月に黙って落とさないのは呼び出し側の判断に任せるため。
    /// </summary>
    public static bool TryParseKey(string? text, out int year, out int month)
    {
        year = 0;
        month = 0;
        if (!DateTime.TryParseExact(
                (text ?? "").Trim(), "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        year = parsed.Year;
        month = parsed.Month;
        return true;
    }

    /// <summary>
    /// カレンダーに出すマスの範囲(日本時間)。月そのものではなく前後にはみ出したマスまでを
    /// 含むので、ストアにはこの範囲で問い合わせる —— そうしないと、月末の週に並ぶ
    /// 翌月頭のマスだけイベントが消えて「その日は何も無い」ように見える。
    /// </summary>
    public static (DateTimeOffset From, DateTimeOffset To) GridRange(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var start = first.AddDays(-Offset(first.DayOfWeek));
        var end = first.AddMonths(1);
        end = end.AddDays(6 - Offset(end.AddDays(-1).DayOfWeek));

        return (Midnight(start), Midnight(end));
    }

    /// <summary>その月そのものの範囲(日本時間)。一覧を「その月のイベント」に絞るのに使う。</summary>
    public static (DateTimeOffset From, DateTimeOffset To) MonthRange(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        return (Midnight(first), Midnight(first.AddMonths(1)));
    }

    /// <summary>
    /// 月のカレンダーを組む。<paramref name="events"/> には
    /// <see cref="GridRange"/> で取ったイベントを渡す(範囲外のものは黙って落ちる)。
    /// </summary>
    public static CalendarMonth Build(
        int year, int month, IEnumerable<TechEvent> events, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(JapanTime.To(now).DateTime);
        var byDate = events
            .GroupBy(techEvent => DateOnly.FromDateTime(JapanTime.To(techEvent.StartsAt).DateTime))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<TechEvent>)group.OrderBy(e => e.StartsAt).ToList());

        var (from, to) = GridRange(year, month);
        var weeks = new List<IReadOnlyList<CalendarDay>>();
        var week = new List<CalendarDay>();

        for (var date = DateOnly.FromDateTime(JapanTime.To(from).DateTime);
             Midnight(date) < to;
             date = date.AddDays(1))
        {
            week.Add(new CalendarDay(
                date,
                date.Month == month && date.Year == year,
                date == today,
                byDate.TryGetValue(date, out var dayEvents) ? dayEvents : []));

            if (week.Count == DayNames.Count)
            {
                weeks.Add(week);
                week = [];
            }
        }

        return new CalendarMonth(year, month, weeks);
    }

    /// <summary>週の始まり(日曜)から数えた曜日の位置。</summary>
    static int Offset(DayOfWeek day) => ((int)day - (int)FirstDayOfWeek + 7) % 7;

    /// <summary>その日の 0 時(日本時間)。保存は UTC なので、比較のたびにここを通す。</summary>
    static DateTimeOffset Midnight(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), JapanTime.Offset);
}

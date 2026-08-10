using System.Globalization;
using TechAntenna.Core;

namespace TechAntenna.Tests.Core;

public class JapanTimeTests
{
    // 収集元によって時差はまちまち(UTC の feed・+09:00 の feed・海外の -05:00 など)。
    // どれで受け取っても、同じ瞬間なら同じ表示になること
    [Theory]
    [InlineData("2026-08-10T12:30:00+00:00")]
    [InlineData("2026-08-10T21:30:00+09:00")]
    [InlineData("2026-08-10T07:30:00-05:00")]
    public void 時差がまちまちでもJSTにそろえて出す(string value)
    {
        var parsed = DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

        Assert.Equal("2026-08-10 21:30 JST", JapanTime.Format(parsed));
        Assert.Equal("8/10 21:30 JST", JapanTime.FormatShort(parsed));
        Assert.Equal("2026-08-10", JapanTime.FormatDate(parsed));
        Assert.Equal("2026-08-10 21:30", JapanTime.FormatCompact(parsed));
        Assert.Equal("20260810-2130", JapanTime.FormatStamp(parsed));
    }

    // UTC の日付と JST の日付は日跨ぎでずれる。「今日」を数えるのは読む人のいる日本時間のほう
    [Fact]
    public void UTCでは前日の時刻もJSTの日付で出す()
    {
        var utcEvening = DateTimeOffset.Parse("2026-08-10T23:00:00+00:00", CultureInfo.InvariantCulture);

        Assert.Equal("2026-08-11", JapanTime.FormatDate(utcEvening));
    }

    // 書式は実行環境の文化圏に依らせない(和暦や区切りの違いで表示が揃わなくなるため)
    [Fact]
    public void 文化圏を変えても書式は変わらない()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ja-JP")
            {
                DateTimeFormat = { Calendar = new JapaneseCalendar() },
            };

            Assert.Equal(
                "2026-08-10 21:30 JST",
                JapanTime.Format(DateTimeOffset.Parse("2026-08-10T21:30:00+09:00", CultureInfo.InvariantCulture)));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}

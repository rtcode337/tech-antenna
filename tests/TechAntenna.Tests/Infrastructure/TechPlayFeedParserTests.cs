using TechAntenna.Infrastructure.Events;

namespace TechAntenna.Tests.Infrastructure;

public class TechPlayFeedParserTests
{
    // 実際の配信と同じ形。日時は tp: 名前空間にあり、時差の表記が無い
    const string Feed = """
        <?xml version="1.0" encoding="utf-8"?>
        <rss version="2.0" xmlns:tp="https://rss.techplay.jp/" xmlns:dc="http://purl.org/dc/elements/1.1/">
          <channel>
            <title>TECH PLAY</title>
            <item>
              <title>生成AI 実践ハンズオン</title>
              <link>https://techplay.jp/event/998890</link>
              <category>IT</category>
              <category>テクノロジー</category>
              <category>イベント</category>
              <category>生成AI</category>
              <tp:eventDate>2026-08-08</tp:eventDate>
              <tp:eventStartTime>2026-08-08 12:30:00</tp:eventStartTime>
              <tp:eventEndTime>2026-08-08 14:00:00</tp:eventEndTime>
              <tp:eventPlace>オンライン</tp:eventPlace>
              <tp:eventAddress></tp:eventAddress>
              <dc:creator>日本マイクロソフト株式会社</dc:creator>
            </item>
            <item>
              <title>開始時刻の無いイベント</title>
              <link>https://techplay.jp/event/998891</link>
              <tp:eventDate>2026-08-09</tp:eventDate>
              <tp:eventPlace>北とぴあ 701会議室</tp:eventPlace>
              <tp:eventAddress>東京都北区王子1-11-1</tp:eventAddress>
            </item>
            <item>
              <title>link が無いイベント</title>
              <tp:eventStartTime>2026-08-10 19:00:00</tp:eventStartTime>
            </item>
          </channel>
        </rss>
        """;

    [Fact]
    public void 日本時間として読んで_UTC_に正規化する()
    {
        var entry = TechPlayFeedParser.Parse(Feed).First();

        // 12:30 JST = 03:30 UTC
        Assert.Equal(
            new DateTime(2026, 8, 8, 3, 30, 0, DateTimeKind.Utc),
            entry.StartsAt.UtcDateTime);
        Assert.Equal(
            new DateTime(2026, 8, 8, 5, 0, 0, DateTimeKind.Utc),
            entry.EndsAt!.Value.UtcDateTime);
    }

    [Fact]
    public void 会場と住所とカテゴリを取り出す()
    {
        var entry = TechPlayFeedParser.Parse(Feed).First();

        Assert.Equal("オンライン", entry.Place);
        // 空要素は null にして、あとで「値がある」と誤解しないようにする
        Assert.Null(entry.Address);
        Assert.Equal(["IT", "テクノロジー", "イベント", "生成AI"], entry.Categories);
    }

    [Fact]
    public void 開始時刻が無ければ開催日の0時として読む()
    {
        var entry = TechPlayFeedParser.Parse(Feed)
            .Single(e => e.Title == "開始時刻の無いイベント");

        // 2026-08-09 00:00 JST = 2026-08-08 15:00 UTC
        Assert.Equal(
            new DateTime(2026, 8, 8, 15, 0, 0, DateTimeKind.Utc),
            entry.StartsAt.UtcDateTime);
        Assert.Null(entry.EndsAt);
        Assert.Equal("東京都北区王子1-11-1", entry.Address);
    }

    [Fact]
    public void 辿れないイベントは取り込まない()
    {
        var entries = TechPlayFeedParser.Parse(Feed);

        Assert.Equal(2, entries.Count);
        Assert.DoesNotContain(entries, e => e.Title == "link が無いイベント");
    }

    [Fact]
    public void RSS_以外の形式は例外にする()
    {
        var atom = """<feed xmlns="http://www.w3.org/2005/Atom"><entry /></feed>""";

        var ex = Assert.Throws<FormatException>(() => TechPlayFeedParser.Parse(atom));
        Assert.Contains("feed", ex.Message);
    }

    [Fact]
    public void 主催者は_dc_creator_から取る()
    {
        // RSS の標準要素でも tp: の独自要素でもないので、見落としていた ——
        // ここが空だとベンダーのウェビナーが「公式」と判定されない
        Assert.Equal("日本マイクロソフト株式会社", TechPlayFeedParser.Parse(Feed).First().Organizer);
    }

    [Fact]
    public void 主催者が無いイベントは_null_のまま()
    {
        Assert.Null(TechPlayFeedParser.Parse(Feed).ElementAt(1).Organizer);
    }
}

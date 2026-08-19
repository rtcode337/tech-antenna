using Microsoft.Extensions.Time.Testing;
using TechAntenna.Infrastructure.Events;

namespace TechAntenna.Tests.Infrastructure;

public class TechPlayEventSourceTests
{
    const string Feed = """
        <?xml version="1.0" encoding="utf-8"?>
        <rss version="2.0" xmlns:tp="https://rss.techplay.jp/" xmlns:dc="http://purl.org/dc/elements/1.1/">
          <channel>
            <item>
              <title>生成AI 実践ハンズオン</title>
              <link>https://techplay.jp/event/998890</link>
              <category>IT</category>
              <category>テクノロジー</category>
              <category>イベント</category>
              <category>生成AI</category>
              <category>初心者</category>
              <tp:eventStartTime>2026-08-08 12:30:00</tp:eventStartTime>
              <tp:eventPlace>オンライン</tp:eventPlace>
              <dc:creator>日本マイクロソフト株式会社</dc:creator>
            </item>
            <item>
              <title>会場開催のセミナー</title>
              <link>https://techplay.jp/event/998891</link>
              <category>IT</category>
              <tp:eventStartTime>2026-08-09 19:00:00</tp:eventStartTime>
              <tp:eventPlace>EBAテック株式会社</tp:eventPlace>
              <tp:eventAddress>東京都渋谷区</tp:eventAddress>
            </item>
            <item>
              <title>終わったイベント</title>
              <link>https://techplay.jp/event/998000</link>
              <tp:eventStartTime>2026-07-20 19:00:00</tp:eventStartTime>
              <tp:eventPlace>オンライン</tp:eventPlace>
            </item>
          </channel>
        </rss>
        """;

    static FakeTimeProvider Clock() =>
        new(new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero));

    static TechPlayEventSource Source() => new(
        new StubHttpClientFactory(Feed),
        Clock(),
        new Uri("https://rss.techplay.jp/event/w3c-rss-format/rss.xml"));

    [Fact]
    public async Task カテゴリをタグにする()
    {
        var events = await Source().FetchAsync();

        var handson = events.Single(e => e.Title == "生成AI 実践ハンズオン");
        // どのイベントにも付く IT・テクノロジー・イベントは落とす。
        // 「初心者」は何の話題かを表さないため TagNormalizer 側でも落ちる
        Assert.Equal(["生成ai"], handson.Tags);
        // 生タグは落とさずに残す(正規化の規則を変えたときに引き直せるようにするため)
        Assert.Contains("初心者", handson.RawTags);
    }

    [Fact]
    public async Task 定型カテゴリしか無ければタグは空になる()
    {
        var events = await Source().FetchAsync();

        Assert.Empty(events.Single(e => e.Title == "会場開催のセミナー").Tags);
    }

    [Fact]
    public async Task 会場表記からオンライン開催を推定する()
    {
        var events = await Source().FetchAsync();

        Assert.True(events.Single(e => e.Title == "生成AI 実践ハンズオン").IsOnline);
        var onsite = events.Single(e => e.Title == "会場開催のセミナー");
        Assert.False(onsite.IsOnline);
        Assert.Equal("EBAテック株式会社", onsite.Venue);
    }

    [Fact]
    public async Task 終わったイベントは取り込まない()
    {
        var events = await Source().FetchAsync();

        Assert.Equal(2, events.Count);
        Assert.DoesNotContain(events, e => e.Title == "終わったイベント");
    }

    [Fact]
    public async Task 収集元の名前を付ける()
    {
        var events = await Source().FetchAsync();

        Assert.All(events, e => Assert.Equal("TECH PLAY", e.SourceName));
    }

    [Fact]
    public async Task 主催者を取り込む()
    {
        // TECH PLAY は参加者数を持たないので、公式かどうかがこの収集元の唯一の重み ——
        // 主催者を落とすと、いちばん厚いベンダーのウェビナーが注目度で沈む
        var events = await Source().FetchAsync();

        Assert.Equal(
            "日本マイクロソフト株式会社",
            events.Single(e => e.Title == "生成AI 実践ハンズオン").Organizer);
        Assert.Null(events.Single(e => e.Title == "会場開催のセミナー").Organizer);
        // 参加者数は RSS に無いので null のまま(0 と区別する)
        Assert.Null(events.First().ParticipantCount);
    }
}

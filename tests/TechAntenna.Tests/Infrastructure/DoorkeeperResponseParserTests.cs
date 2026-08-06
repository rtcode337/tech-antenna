using TechAntenna.Infrastructure.Events;

namespace TechAntenna.Tests.Infrastructure;

public class DoorkeeperResponseParserTests
{
    // Doorkeeper のレスポンスを模した JSON。各イベントは "event" で包まれる
    const string Response = """
        [
          {
            "event": {
              "id": 12345,
              "title": "C# 勉強会",
              "starts_at": "2026-08-20T19:00:00.000+09:00",
              "ends_at": "2026-08-20T21:00:00.000+09:00",
              "venue_name": "東京都渋谷区の会議室",
              "address": "東京都渋谷区1-1-1",
              "public_url": "https://example.doorkeeper.jp/events/12345"
            }
          },
          {
            "event": {
              "id": 12346,
              "title": "オンライン LT 会",
              "starts_at": "2026-08-25T20:00:00.000+09:00",
              "ends_at": null,
              "venue_name": "オンライン",
              "address": null,
              "public_url": "https://example.doorkeeper.jp/events/12346"
            }
          }
        ]
        """;

    [Fact]
    public void eventで包まれたレスポンスを解析できる()
    {
        var entries = DoorkeeperResponseParser.Parse(Response);

        Assert.Equal(2, entries.Count);
        var first = entries[0];
        Assert.Equal("C# 勉強会", first.Title);
        Assert.Equal(new Uri("https://example.doorkeeper.jp/events/12345"), first.Url);
        Assert.Equal(new DateTimeOffset(2026, 8, 20, 19, 0, 0, TimeSpan.FromHours(9)), first.StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 8, 20, 21, 0, 0, TimeSpan.FromHours(9)), first.EndsAt);
        Assert.Equal("東京都渋谷区の会議室", first.VenueName);
        Assert.Equal("東京都渋谷区1-1-1", first.Address);
    }

    [Fact]
    public void nullのフィールドはnullとして扱う()
    {
        var entries = DoorkeeperResponseParser.Parse(Response);

        var online = entries[1];
        Assert.Null(online.EndsAt);
        Assert.Null(online.Address);
        Assert.Equal("オンライン", online.VenueName);
    }

    [Fact]
    public void 包まれていない形にも対応する()
    {
        var json = """
            [{"title":"素の形","public_url":"https://example.doorkeeper.jp/events/1"}]
            """;

        var entry = Assert.Single(DoorkeeperResponseParser.Parse(json));

        Assert.Equal("素の形", entry.Title);
    }

    [Fact]
    public void public_urlが無いイベントは取り込まない()
    {
        var json = """
            [{"event":{"title":"URL が無いイベント","starts_at":"2026-08-20T19:00:00+09:00"}}]
            """;

        Assert.Empty(DoorkeeperResponseParser.Parse(json));
    }

    [Fact]
    public void 配列でなければFormatExceptionを投げる()
    {
        Assert.Throws<FormatException>(() => DoorkeeperResponseParser.Parse("""{"error":"bad"}"""));
    }

    [Fact]
    public void public_urlがhttp以外のスキームなら取り込まない()
    {
        // href にそのまま出るため、javascript: 等を通すと格納型 XSS になる
        var json = """
            [{"event":{"title":"不正な URL","public_url":"javascript:alert(1)"}}]
            """;

        Assert.Empty(DoorkeeperResponseParser.Parse(json));
    }
}

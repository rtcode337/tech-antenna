using TechAntenna.Infrastructure.Events;

namespace TechAntenna.Tests.Infrastructure;

public class ConnpassResponseParserTests
{
    // connpass API v2 のレスポンスを模した JSON
    const string V2Response = """
        {
          "results_returned": 2,
          "results_available": 2,
          "results_start": 1,
          "events": [
            {
              "id": 300001,
              "title": ".NET 勉強会 #1",
              "url": "https://example.connpass.com/event/300001/",
              "hash_tag": "dotnetstudy",
              "started_at": "2026-08-10T19:00:00+09:00",
              "ended_at": "2026-08-10T21:00:00+09:00",
              "place": "東京都千代田区の会議室",
              "address": "東京都千代田区1-1-1",
              "group": { "id": 1, "subdomain": "example", "title": "日本マイクロソフト", "url": "https://example.connpass.com/" },
              "owner_display_name": "主催者たろう",
              "accepted": 120,
              "waiting": 30
            },
            {
              "id": 300002,
              "title": "Blazor もくもく会",
              "url": "https://example.connpass.com/event/300002/",
              "hash_tag": null,
              "started_at": "2026-08-12T20:00:00+09:00",
              "ended_at": null,
              "place": "オンライン",
              "address": null,
              "group": null,
              "owner_display_name": "個人開催のひと"
            }
          ]
        }
        """;

    // v1 系のフィールド名(event_url)を使うレスポンス
    const string V1StyleResponse = """
        {
          "events": [
            {
              "event_id": 100001,
              "title": "旧形式のイベント",
              "event_url": "https://example.connpass.com/event/100001/",
              "started_at": "2026-09-01T10:00:00+09:00"
            }
          ]
        }
        """;

    [Fact]
    public void V2のレスポンスを解析できる()
    {
        var entries = ConnpassResponseParser.Parse(V2Response);

        Assert.Equal(2, entries.Count);
        var first = entries[0];
        Assert.Equal(".NET 勉強会 #1", first.Title);
        Assert.Equal(new Uri("https://example.connpass.com/event/300001/"), first.Url);
        Assert.Equal(new DateTimeOffset(2026, 8, 10, 19, 0, 0, TimeSpan.FromHours(9)), first.StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 8, 10, 21, 0, 0, TimeSpan.FromHours(9)), first.EndsAt);
        Assert.Equal("東京都千代田区の会議室", first.Place);
        Assert.Equal("dotnetstudy", first.HashTag);
    }

    [Fact]
    public void nullのフィールドはnullとして扱う()
    {
        var entries = ConnpassResponseParser.Parse(V2Response);

        var online = entries[1];
        Assert.Null(online.HashTag);
        Assert.Null(online.EndsAt);
        Assert.Null(online.Address);
        Assert.Equal("オンライン", online.Place);
    }

    [Fact]
    public void 主催グループと参加者数を取り出す()
    {
        var entries = ConnpassResponseParser.Parse(V2Response);

        // 公式かどうかの判定材料はグループ名(補欠 30 人は参加者数に足さない)
        Assert.Equal("日本マイクロソフト", entries[0].Organizer);
        Assert.Equal(120, entries[0].ParticipantCount);
    }

    [Fact]
    public void グループの無いイベントは管理者の表示名で代える()
    {
        var entries = ConnpassResponseParser.Parse(V2Response);

        Assert.Equal("個人開催のひと", entries[1].Organizer);
        // 参加者数の項目自体が無いときは null(0 と混ぜない)
        Assert.Null(entries[1].ParticipantCount);
    }

    [Fact]
    public void V1系のフィールド名にも対応する()
    {
        var entries = ConnpassResponseParser.Parse(V1StyleResponse);

        var entry = Assert.Single(entries);
        Assert.Equal(new Uri("https://example.connpass.com/event/100001/"), entry.Url);
    }

    [Fact]
    public void events配列が無ければFormatExceptionを投げる()
    {
        Assert.Throws<FormatException>(() => ConnpassResponseParser.Parse("""{"detail": "error"}"""));
    }
}

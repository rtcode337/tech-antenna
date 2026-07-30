using Microsoft.Extensions.Time.Testing;
using TechAntenna.Infrastructure.Events;

namespace TechAntenna.Tests.Infrastructure;

public class DoorkeeperEventSourceTests
{
    const string Response = """
        [
          {
            "event": {
              "title": "C# 勉強会",
              "starts_at": "2026-08-20T19:00:00.000+09:00",
              "venue_name": "東京都渋谷区の会議室",
              "public_url": "https://example.doorkeeper.jp/events/12345"
            }
          },
          {
            "event": {
              "title": "オンライン LT 会",
              "starts_at": "2026-08-25T20:00:00.000+09:00",
              "venue_name": "オンライン",
              "public_url": "https://example.doorkeeper.jp/events/12346"
            }
          }
        ]
        """;

    static FakeTimeProvider Clock() =>
        new(new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task 検索キーワードをタグにする()
    {
        var source = new DoorkeeperEventSource(
            new StubHttpClientFactory(Response), Clock(), ["C#"], TimeSpan.Zero);

        var events = await source.FetchAsync();

        Assert.All(events, e => Assert.Equal(["c#"], e.Tags));
    }

    [Fact]
    public async Task 会場表記からオンライン開催を推定する()
    {
        var source = new DoorkeeperEventSource(
            new StubHttpClientFactory(Response), Clock(), ["C#"], TimeSpan.Zero);

        var events = await source.FetchAsync();

        var onsite = events.Single(e => e.Title == "C# 勉強会");
        var online = events.Single(e => e.Title == "オンライン LT 会");
        Assert.False(onsite.IsOnline);
        Assert.True(online.IsOnline);
    }

    [Fact]
    public async Task キーワードごとに問い合わせて今日以降に絞る()
    {
        var factory = new StubHttpClientFactory(Response);
        var source = new DoorkeeperEventSource(factory, Clock(), ["C#", "Blazor"], TimeSpan.Zero);

        await source.FetchAsync();

        Assert.Equal(2, factory.RequestedUris.Count);
        Assert.All(factory.RequestedUris, uri =>
            Assert.Contains("since=2026-07-30", uri.ToString()));
    }

    [Fact]
    public async Task 複数のキーワードで見つかったイベントはまとめてタグを足す()
    {
        var factory = new StubHttpClientFactory(Response);
        var source = new DoorkeeperEventSource(factory, Clock(), ["C#", "Blazor"], TimeSpan.Zero);

        var events = await source.FetchAsync();

        // 同じ2件が両方のキーワードで返るため、URL でまとまり件数は増えない
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(["c#", "blazor"], e.Tags));
    }
}

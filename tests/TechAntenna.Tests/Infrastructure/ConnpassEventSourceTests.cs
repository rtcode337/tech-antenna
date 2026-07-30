using Microsoft.Extensions.Time.Testing;
using TechAntenna.Infrastructure.Events;

namespace TechAntenna.Tests.Infrastructure;

public class ConnpassEventSourceTests
{
    const string Response = """
        {
          "events": [
            {
              "id": 300001,
              "title": ".NET 勉強会",
              "url": "https://example.connpass.com/event/300001/",
              "hash_tag": "dotnetstudy",
              "started_at": "2026-08-10T19:00:00+09:00",
              "place": "東京都千代田区の会議室"
            },
            {
              "id": 300002,
              "title": "ハッシュタグが無いイベント",
              "url": "https://example.connpass.com/event/300002/",
              "hash_tag": null,
              "started_at": "2026-08-12T20:00:00+09:00",
              "place": "オンライン"
            }
          ]
        }
        """;

    static FakeTimeProvider Clock() =>
        new(new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task ハッシュタグが無くても検索キーワードがタグになる()
    {
        var source = new ConnpassEventSource(
            new StubHttpClientFactory(Response), Clock(), ["C#"], TimeSpan.Zero);

        var events = await source.FetchAsync();

        var noHashTag = events.Single(e => e.Title == "ハッシュタグが無いイベント");
        Assert.Equal(["c#"], noHashTag.Tags);
    }

    [Fact]
    public async Task ハッシュタグがあれば検索キーワードと併せてタグにする()
    {
        var source = new ConnpassEventSource(
            new StubHttpClientFactory(Response), Clock(), ["C#"], TimeSpan.Zero);

        var events = await source.FetchAsync();

        var withHashTag = events.Single(e => e.Title == ".NET 勉強会");
        Assert.Equal(["c#", "dotnetstudy"], withHashTag.Tags);
    }

    [Fact]
    public async Task キーワードごとに問い合わせる()
    {
        var factory = new StubHttpClientFactory(Response);
        var source = new ConnpassEventSource(factory, Clock(), ["C#", "Blazor"], TimeSpan.Zero);

        await source.FetchAsync();

        Assert.Equal(2, factory.RequestedUris.Count);
        // keyword_or ではなく keyword で1語ずつ引く
        Assert.All(factory.RequestedUris, uri => Assert.Contains("keyword=", uri.ToString()));
        Assert.DoesNotContain(factory.RequestedUris, uri => uri.ToString().Contains("keyword_or="));
    }

    [Fact]
    public async Task 複数のキーワードで見つかったイベントはまとめてタグを足す()
    {
        var factory = new StubHttpClientFactory(Response);
        var source = new ConnpassEventSource(factory, Clock(), ["C#", "Blazor"], TimeSpan.Zero);

        var events = await source.FetchAsync();

        // 同じ2件が両方のキーワードで返るため、URL でまとまり件数は増えない
        Assert.Equal(2, events.Count);
        var noHashTag = events.Single(e => e.Title == "ハッシュタグが無いイベント");
        Assert.Equal(["c#", "blazor"], noHashTag.Tags);
    }

    [Fact]
    public async Task 会場表記からオンライン開催を推定する()
    {
        var source = new ConnpassEventSource(
            new StubHttpClientFactory(Response), Clock(), ["C#"], TimeSpan.Zero);

        var events = await source.FetchAsync();

        Assert.False(events.Single(e => e.Title == ".NET 勉強会").IsOnline);
        Assert.True(events.Single(e => e.Title == "ハッシュタグが無いイベント").IsOnline);
    }
}

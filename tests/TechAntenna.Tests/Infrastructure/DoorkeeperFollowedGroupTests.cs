using Microsoft.Extensions.Time.Testing;
using TechAntenna.Core;
using TechAntenna.Infrastructure.Events;

namespace TechAntenna.Tests.Infrastructure;

/// <summary>
/// 購読しているコミュニティを <c>/groups/&lt;名前&gt;/events</c> で直接引く経路。
/// 検索(<c>q=</c>)と違い、<b>タイトルの照合をしない</b>のが要点 ——
/// 「このコミュニティのイベントは全部見たい」が購読の意味だから。
/// </summary>
public class DoorkeeperFollowedGroupTests
{
    const string SearchResponse = """
        [
          { "event": {
              "title": "AI 勉強会",
              "public_url": "https://example.doorkeeper.jp/events/1",
              "starts_at": "2026-09-01T19:00:00Z",
              "participants": 15
          } }
        ]
        """;

    /// <summary>グループ指定で返る中身。収集語をどこにも含まない。</summary>
    const string GroupEventsResponse = """
        [
          { "event": {
              "title": "秋のカンファレンス",
              "public_url": "https://example.doorkeeper.jp/events/2",
              "starts_at": "2026-09-20T10:00:00Z",
              "participants": 400,
              "group": { "name": "例のコミュニティ" }
          } }
        ]
        """;

    static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero));

    static RoutedHttpClientFactory Factory() => RoutedHttpClientFactory.Matching(
        ("/groups/example/events", GroupEventsResponse),
        ("/events?q=", SearchResponse));

    static DoorkeeperEventSource Source(IHttpClientFactory factory, string roster, params string[] keywords) =>
        new(factory, Clock(), keywords, TimeSpan.Zero,
            followedProvider: () => FollowedGroups.Parse(roster));

    [Fact]
    public async Task 検索語に当たらなくても購読しているコミュニティのイベントは入る()
    {
        var source = Source(Factory(), "doorkeeper:example 例のコミュニティ", "AI");

        var events = await source.FetchAsync();

        // 検索の経路はタイトル照合(KeywordMatcher)を通すので、この題では絶対に入らない
        var conference = events.Single(e => e.Title == "秋のカンファレンス");
        Assert.Equal("例のコミュニティ", conference.PickedBy);
        Assert.Empty(conference.Tags);
    }

    [Fact]
    public async Task グループのエンドポイントを開催日以降で引く()
    {
        var factory = Factory();

        await Source(factory, "doorkeeper:example").FetchAsync();

        var uri = Assert.Single(factory.RequestedUris, u => u.ToString().Contains("/groups/"));
        Assert.Contains("/groups/example/events", uri.ToString());
        // 過ぎたイベントを拾わない。「今日」は開催地の日本時間で数える
        Assert.Contains("since=2026-08-15", uri.ToString());
        Assert.Contains("expand[]=group", uri.ToString());
    }

    [Fact]
    public async Task 名簿の打ち間違いは飛ばして残りを集める()
    {
        var source = Source(Factory(), """
            doorkeeper:typo-typo 打ち間違い
            doorkeeper:example 例のコミュニティ
            """);

        var events = await source.FetchAsync();

        Assert.Contains(events, e => e.Title == "秋のカンファレンス");
    }

    [Fact]
    public void 購読していればトピックの選択が空でも集めるものがある()
    {
        Assert.True(Source(Factory(), "doorkeeper:example").WorksWithoutTopics);
        Assert.False(Source(Factory(), "connpass:other").WorksWithoutTopics);
    }
}

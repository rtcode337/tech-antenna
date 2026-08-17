using Microsoft.Extensions.Time.Testing;
using TechAntenna.Infrastructure.Events;

namespace TechAntenna.Tests.Infrastructure;

/// <summary>
/// Doorkeeper の面掃き。connpass の面掃きと同じ役割なので、見張るところも同じ ——
/// 「小さいものが確実に落ちること」と「相手を叩きすぎないこと」。
/// </summary>
public class DoorkeeperSweepEventSourceTests
{
    static string Response(params (string Title, int? Participants)[] events)
    {
        var items = events.Select((e, i) => $$"""
            { "event": {
              "title": {{System.Text.Json.JsonSerializer.Serialize(e.Title)}},
              "public_url": "https://example.doorkeeper.jp/events/{{i + 1}}",
              "starts_at": "2026-09-10T19:00:00+09:00",
              "venue_name": "東京",
              "participants": {{(e.Participants?.ToString() ?? "null")}},
              "group": { "name": "例のコミュニティ" }
            } }
            """);

        return $"[{string.Join(",", items)}]";
    }

    static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero));

    static DoorkeeperSweepEventSource Source(
        IHttpClientFactory factory, int minParticipants = 100, int months = 2) =>
        new(factory, Clock(), minParticipants, months, TimeSpan.Zero);

    [Fact]
    public async Task 参加者数がしきい値に届かないものは落とす()
    {
        var factory = new StubHttpClientFactory(
            Response(("大きいカンファレンス", 500), ("小さい勉強会", 12)));

        var kept = Assert.Single(await Source(factory).FetchAsync());

        Assert.Equal("大きいカンファレンス", kept.Title);
        Assert.Equal("例のコミュニティ", kept.Organizer);
    }

    [Fact]
    public async Task 参加者数が取れていないものは残さない()
    {
        // 「人が集まっている」ことだけを根拠に拾う経路なので、根拠の無いものは通さない
        var factory = new StubHttpClientFactory(Response(("数の分からないイベント", null)));

        Assert.Empty(await Source(factory).FetchAsync());
    }

    [Fact]
    public async Task 入った理由をイベント自身に持たせる()
    {
        var factory = new StubHttpClientFactory(Response(("大きいカンファレンス", 500)));

        var kept = Assert.Single(await Source(factory).FetchAsync());

        Assert.Equal("参加者 100 人以上", kept.PickedBy);
        Assert.Equal("Doorkeeper(面掃き)", kept.SourceName);
        // 検索語で引いていないので、タグは付かない(主催者名をタグにはしない)
        Assert.Empty(kept.Tags);
    }

    [Fact]
    public async Task 検索語を付けず日本時間の今日から期間で引く()
    {
        var factory = new StubHttpClientFactory(Response(("大きいカンファレンス", 500)));

        await Source(factory).FetchAsync();

        // 空のページが返らないスタブなので上限まで読む。見たいのは 1 ページ目の組み立て方
        var uri = factory.RequestedUris[0].ToString();
        Assert.DoesNotContain("q=", uri);
        // UTC の日付で引くと、日本の朝 9 時までは前日から引くことになる
        Assert.Contains("since=2026-08-15", uri);
        Assert.Contains("until=2026-10-15", uri);
        // 主催者名は expand しないと取れない(公式かどうかの判定材料)
        Assert.Contains("expand[]=group", uri);
    }

    [Fact]
    public async Task 空のページが返ったらそこで止める()
    {
        // 総件数を返さない API なので、「読み切ったか」は空のページでしか分からない
        var factory = RoutedHttpClientFactory.Matching(
            ("page=1", Response(("大きいカンファレンス", 500))),
            ("page=2", "[]"));

        var source = Source(factory);
        Assert.Single(await source.FetchAsync());

        Assert.Equal(2, factory.RequestedUris.Count);
        Assert.False(source.Truncated);
    }

    [Fact]
    public async Task 上限まで読んでも終わらなければ打ち切って結果に残す()
    {
        // 黙って切ると「全部見た」と読めてしまう
        var factory = new StubHttpClientFactory(Response(("大きいカンファレンス", 500)));
        var source = Source(factory);

        await source.FetchAsync();

        Assert.True(source.Truncated);
        Assert.Equal(10, factory.RequestedUris.Count);
    }

    [Fact]
    public void 検索語を使わないのでトピックの選択が空でも走る()
    {
        Assert.True(Source(new StubHttpClientFactory("[]")).WorksWithoutTopics);
    }

    [Fact]
    public async Task トークン未設定ならこの収集元だけ黙って休む()
    {
        var factory = new StubHttpClientFactory(Response(("大きいカンファレンス", 500)));
        var source = new DoorkeeperSweepEventSource(
            factory, Clock(), 100, 2, TimeSpan.Zero, accessTokenProvider: () => "");

        Assert.Empty(await source.FetchAsync());
        Assert.Empty(factory.RequestedUris);
    }
}

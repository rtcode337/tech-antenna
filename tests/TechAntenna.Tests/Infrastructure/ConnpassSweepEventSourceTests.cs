using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using TechAntenna.Infrastructure.Events;

namespace TechAntenna.Tests.Infrastructure;

/// <summary>
/// 月ごとの面掃き。<b>検索語も名簿も使わず、参加者数だけで切る</b>経路なので、
/// 「小さいものが確実に落ちること」と「相手を叩きすぎないこと」を見張る。
/// </summary>
public class ConnpassSweepEventSourceTests
{
    static string Response(params (string Title, int? Accepted)[] events)
    {
        var items = events.Select((e, i) => $$"""
            {
              "title": {{System.Text.Json.JsonSerializer.Serialize(e.Title)}},
              "url": "https://example.connpass.com/event/{{i + 1}}/",
              "started_at": "2026-09-10T10:00:00+09:00",
              "place": "東京",
              "accepted": {{(e.Accepted?.ToString(CultureInfo.InvariantCulture) ?? "null")}}
            }
            """);

        return $$"""{ "events": [ {{string.Join(",", items)}} ], "results_available": {{events.Length}} }""";
    }

    static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero));

    static ConnpassSweepEventSource Source(
        IHttpClientFactory factory, int minParticipants = 100, int months = 1) =>
        new(factory, Clock(), minParticipants, months, TimeSpan.Zero);

    [Fact]
    public async Task 参加者数がしきい値に届かないものは落とす()
    {
        var factory = new StubHttpClientFactory(
            Response(("大きいカンファレンス", 500), ("小さい勉強会", 12)));

        var events = await Source(factory).FetchAsync();

        var kept = Assert.Single(events);
        Assert.Equal("大きいカンファレンス", kept.Title);
    }

    [Fact]
    public async Task 参加者数が取れていないものは残さない()
    {
        // この経路は「人が集まっている」ことだけを根拠に拾っている。
        // 根拠が無いものを通すと、単なる全件取り込みになる
        var factory = new StubHttpClientFactory(Response(("数の分からないイベント", null)));

        Assert.Empty(await Source(factory).FetchAsync());
    }

    [Fact]
    public async Task 入った理由をイベント自身に持たせる()
    {
        var factory = new StubHttpClientFactory(Response(("大きいカンファレンス", 500)));

        var kept = Assert.Single(await Source(factory).FetchAsync());

        // トピックに当たらないイベントが一覧にいる訳を、カードが説明できるようにする
        Assert.Equal("参加者 100 人以上", kept.PickedBy);
        Assert.Equal("connpass(面掃き)", kept.SourceName);
    }

    [Fact]
    public async Task 月を指定して引き日本時間の今月から数える()
    {
        var factory = new StubHttpClientFactory(Response(("大きいカンファレンス", 500)));

        await Source(factory, months: 3).FetchAsync();

        // UTC で数えると月初の朝 9 時までは前の月を掃くことになる
        Assert.Equal(3, factory.RequestedUris.Count);
        Assert.Contains(factory.RequestedUris, uri => uri.ToString().Contains("ym=202608"));
        Assert.Contains(factory.RequestedUris, uri => uri.ToString().Contains("ym=202610"));
    }

    [Fact]
    public async Task 返りが上限に満たなければ続きを取りに行かない()
    {
        var factory = new StubHttpClientFactory(Response(("大きいカンファレンス", 500)));

        await Source(factory).FetchAsync();

        Assert.Single(factory.RequestedUris);
        Assert.Empty(Source(factory).Truncated);
    }

    [Fact]
    public async Task 検索語を使わないのでトピックの選択が空でも走る()
    {
        var factory = new StubHttpClientFactory(Response());

        Assert.True(Source(factory).WorksWithoutTopics);
        Assert.Empty(await Source(factory).FetchAsync());
    }

    [Fact]
    public async Task 上限まで読んでも終わらない月は打ち切って結果に残す()
    {
        // 100 件ちょうどが返り続ける月。黙って切ると「全部見た」と読めてしまうので、
        // どの月を最後まで見られなかったかを持ち帰る
        var full = Response([.. Enumerable.Range(0, 100).Select(i => ($"イベント{i}", (int?)500))]);
        var factory = new StubHttpClientFactory(full.Replace("\"results_available\": 100", "\"results_available\": 5000"));
        var source = Source(factory);

        await source.FetchAsync();

        Assert.Equal(["2026-08"], source.Truncated);
        // 1か月あたりのページ数には歯止めがある(青天井に叩き続けない)
        Assert.Equal(10, factory.RequestedUris.Count);
    }

    [Fact]
    public async Task 画面で止めているあいだは叩きに行かない()
    {
        // 切り替えは実行のたびに読む(起動時に見て分岐すると、画面で入れても
        // 再起動するまで効かない)。止まっているあいだは相手に 1 回も触らない
        var factory = new StubHttpClientFactory("[]");
        var source = new ConnpassSweepEventSource(factory, Clock(), 100, 1, TimeSpan.Zero, enabledProvider: () => false);

        Assert.Empty(await source.FetchAsync());
        Assert.Empty(factory.RequestedUris);
        // 無効なら WorksWithoutTopics も false —— true のままだと、面掃きを止めていても
        // ランナーが「トピックが空でも集めるものがある」と判断し、案内が出なくなる
        Assert.False(source.WorksWithoutTopics);
    }

    [Fact]
    public async Task 差分では公開日で引き一回で済ませる()
    {
        // 2回目からは「前回以降に公開されたぶん」だけ引く。全掃きは月ごとに
        // 最大10ページ×月数かかるが、`publish_ymd` は複数指定できるので
        // 何日ぶんでも 1 リクエストにまとまる
        var factory = new StubHttpClientFactory(Response(("大きいカンファレンス", 500)));
        var since = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.FromHours(9));

        var source = new ConnpassSweepEventSource(
            factory, Clock(), minParticipants: 100, months: 2, TimeSpan.Zero,
            incrementalSinceProvider: () => since);

        var events = await source.FetchAsync();

        Assert.Single(events);
        var uri = Assert.Single(factory.RequestedUris);
        // 前回の日から今日(JST)までを並べる。前回の日も含める ——
        // その日の途中で走ったときに取りこぼさないため
        Assert.Contains("publish_ymd=20260813", uri.Query);
        Assert.Contains("publish_ymd=20260815", uri.Query);
        // 月ごとの全掃きには行っていない
        Assert.DoesNotContain("ym=", uri.Query);
    }

    [Fact]
    public async Task 差分でも掃く期間の外は取り込まない()
    {
        // 公開日で引くと、開催が遠い先のイベントも一緒に返ってくる ——
        // 全掃きと同じ範囲だけを持たないと、実行した時刻で入る/入らないが変わる
        var far = """
            { "events": [ {
              "title": "半年先の大型カンファレンス",
              "url": "https://example.connpass.com/event/99/",
              "started_at": "2027-03-10T10:00:00+09:00",
              "accepted": 900
            } ], "results_available": 1 }
            """;
        var factory = new StubHttpClientFactory(far);

        var source = new ConnpassSweepEventSource(
            factory, Clock(), minParticipants: 100, months: 2, TimeSpan.Zero,
            incrementalSinceProvider: () => new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.FromHours(9)));

        Assert.Empty(await source.FetchAsync());
    }

    [Fact]
    public async Task 長く止めていたら全掃きへ戻す()
    {
        // 何十日ぶんも publish_ymd を並べるより、月ごとに掃くほうが速い
        var factory = new StubHttpClientFactory(Response(("大きいカンファレンス", 500)));

        var source = new ConnpassSweepEventSource(
            factory, Clock(), minParticipants: 100, months: 1, TimeSpan.Zero,
            incrementalSinceProvider: () => new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(9)));

        await source.FetchAsync();

        Assert.All(factory.RequestedUris, uri => Assert.Contains("ym=", uri.Query));
    }
}

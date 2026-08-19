using Microsoft.Extensions.Time.Testing;
using TechAntenna.Core;
using TechAntenna.Infrastructure.Events;

namespace TechAntenna.Tests.Infrastructure;

/// <summary>
/// 購読しているシリーズを ID で直接引く経路(<see cref="FollowedGroups"/>)。
/// キーワード検索では構造的に落ちる固有名詞のカンファレンスを取りこぼさないための道なので、
/// 「検索語に当たらなくても入ること」がここでの主眼。
/// </summary>
public class ConnpassFollowedGroupTests
{
    /// <summary>検索(keyword=…)で返る中身。収集語「AI」に当たるイベント。</summary>
    const string SearchResponse = """
        {
          "events": [
            {
              "title": "生成AI もくもく会",
              "url": "https://example.connpass.com/event/1/",
              "started_at": "2026-09-01T19:00:00+09:00",
              "place": "オンライン",
              "accepted": 20
            }
          ]
        }
        """;

    /// <summary>シリーズ指定(series_id=…)で返る中身。収集語をどこにも含まない。</summary>
    const string SeriesResponse = """
        {
          "events": [
            {
              "title": "RubyKaigi 2026",
              "url": "https://example.connpass.com/event/2/",
              "hash_tag": "rubykaigi",
              "started_at": "2026-09-10T10:00:00+09:00",
              "place": "松山市",
              "accepted": 900,
              "group": { "title": "日本Rubyの会" }
            }
          ]
        }
        """;

    const string GroupResponse = """
        { "groups": [ { "id": 4321, "subdomain": "rubykaigi", "title": "RubyKaigi" } ] }
        """;

    static FakeTimeProvider Clock() => new(new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero));

    static RoutedHttpClientFactory Factory() => RoutedHttpClientFactory.Matching(
        ("/groups/", GroupResponse),
        ("series_id=", SeriesResponse),
        ("keyword=", SearchResponse));

    static ConnpassEventSource Source(IHttpClientFactory factory, string roster, params string[] keywords) =>
        new(factory, Clock(), keywords, TimeSpan.Zero,
            followedProvider: () => FollowedGroups.Parse(roster));

    [Fact]
    public async Task 検索語に当たらなくても購読しているシリーズのイベントは入る()
    {
        var source = Source(Factory(), "connpass:rubykaigi RubyKaigi", "AI");

        var events = await source.FetchAsync();

        var kaigi = events.Single(e => e.Title == "RubyKaigi 2026");
        // タイトルにも会場にも収集語「AI」は無い。検索だけの経路では絶対に入らないもの
        Assert.Equal("RubyKaigi", kaigi.PickedBy);
    }

    [Fact]
    public async Task 購読で入ったイベントのタグはハッシュタグだけになる()
    {
        var source = Source(Factory(), "connpass:rubykaigi RubyKaigi", "AI");

        var events = await source.FetchAsync();

        // グループの表示名はタグにしない —— イベント名が語彙に流れ込むと、
        // タグの一覧と LLM の仕分けがその回限りの固有名詞で埋まる
        Assert.Equal(["rubykaigi"], events.Single(e => e.Title == "RubyKaigi 2026").Tags);
    }

    [Fact]
    public async Task 検索で見つけたイベントには購読の印が付かない()
    {
        var source = Source(Factory(), "connpass:rubykaigi RubyKaigi", "AI");

        var events = await source.FetchAsync();

        Assert.Null(events.Single(e => e.Title == "生成AI もくもく会").PickedBy);
    }

    [Fact]
    public async Task サブドメインはシリーズIDへ引き直してから使う()
    {
        var factory = Factory();
        var source = Source(factory, "connpass:rubykaigi RubyKaigi", "AI");

        await source.FetchAsync();

        // グループの ID は画面に出てこないので、名簿にはサブドメインを書けるようにしてある
        Assert.Contains(factory.RequestedUris, uri => uri.ToString().Contains("/groups/?subdomain=rubykaigi"));
        Assert.Contains(factory.RequestedUris, uri => uri.ToString().Contains("series_id=4321"));
    }

    [Fact]
    public async Task 数字で書けば引き直さない()
    {
        var factory = Factory();
        var source = Source(factory, "connpass:4321 RubyKaigi");

        await source.FetchAsync();

        Assert.DoesNotContain(factory.RequestedUris, uri => uri.ToString().Contains("/groups/"));
        Assert.Contains(factory.RequestedUris, uri => uri.ToString().Contains("series_id=4321"));
    }

    [Fact]
    public async Task 一度引いたサブドメインは覚えておく()
    {
        var factory = Factory();
        var source = Source(factory, "connpass:rubykaigi RubyKaigi");

        await source.FetchAsync();
        await source.FetchAsync();

        // グループの ID は変わらないので、収集のたびに引き直すのは相手を無駄に叩くだけ
        Assert.Single(factory.RequestedUris, uri => uri.ToString().Contains("/groups/"));
    }

    [Fact]
    public async Task 名簿の打ち間違いは飛ばして残りを集める()
    {
        // 存在しないサブドメイン(404)。1行の誤りで収集全体を止めない
        var factory = RoutedHttpClientFactory.Matching(
            ("subdomain=rubykaigi", GroupResponse),
            ("series_id=", SeriesResponse),
            ("keyword=", SearchResponse));
        var source = Source(factory, """
            connpass:typo-typo 打ち間違い
            connpass:rubykaigi RubyKaigi
            """);

        var events = await source.FetchAsync();

        Assert.Contains(events, e => e.Title == "RubyKaigi 2026");
    }

    [Fact]
    public void 購読していればトピックの選択が空でも集めるものがある()
    {
        // EventCollectionRunner はこれを見て、選択が空でもこの収集元を走らせる
        Assert.True(Source(Factory(), "connpass:rubykaigi").WorksWithoutTopics);
        Assert.False(Source(Factory(), "").WorksWithoutTopics);
    }
}

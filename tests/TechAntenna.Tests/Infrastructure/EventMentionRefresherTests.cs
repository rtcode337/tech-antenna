using Microsoft.Extensions.Time.Testing;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Events;
using TechAntenna.Infrastructure.Storage;

namespace TechAntenna.Tests.Infrastructure;

/// <summary>
/// 記事の言及数を数え直す一連の流れ。<b>外部を一切叩かない</b>ので、
/// ここで見るのは「手元の材料からどんな数が付くか」だけ。
/// </summary>
public class EventMentionRefresherTests
{
    static readonly DateTimeOffset Now = new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);

    static TechEvent Event(string title, int daysFromNow) => new()
    {
        Title = title,
        Url = new Uri($"https://example.com/event/{Uri.EscapeDataString(title)}"),
        SourceName = "connpass",
        StartsAt = Now.AddDays(daysFromNow),
        CollectedAt = Now,
    };

    static Article Article(string title) => new()
    {
        Title = title,
        Url = new Uri($"https://example.com/article/{Uri.EscapeDataString(title)}"),
        SourceName = "Zenn",
        CollectedAt = Now,
    };

    static async Task<InMemoryEventStore> RefreshAsync(
        IReadOnlyList<TechEvent> events, IReadOnlyList<Article> articles)
    {
        var eventStore = new InMemoryEventStore();
        var articleStore = new InMemoryArticleStore();
        await eventStore.AddRangeAsync(events);
        await articleStore.AddRangeAsync(articles);

        await new EventMentionRefresher(
            eventStore, articleStore, TopicCatalog.Empty, new FakeTimeProvider(Now)).RefreshAsync();

        return eventStore;
    }

    static async Task<int?> MentionsOf(InMemoryEventStore store, string title) =>
        (await store.GetInRangeAsync(Now.AddYears(-1), Now.AddYears(1), 100))
            .Single(e => e.Title == title).MentionCount;

    [Fact]
    public async Task 記事で名前が挙がっているイベントに本数が付く()
    {
        var store = await RefreshAsync(
            [Event("RubyKaigi 2026", 20)],
            [Article("RubyKaigi 2026 の感想"), Article("RubyKaigi に行ってきた"), Article("Go の話")]);

        Assert.Equal(2, await MentionsOf(store, "RubyKaigi 2026"));
    }

    [Fact]
    public async Task 誰も書いていないイベントは0本になる()
    {
        // 0 と null は別物。0 は「測ったが書かれていない」
        var store = await RefreshAsync([Event("DroidKaigi 2026", 20)], [Article("Go の話")]);

        Assert.Equal(0, await MentionsOf(store, "DroidKaigi 2026"));
    }

    [Fact]
    public async Task 照合語を作れないイベントは測らないままにする()
    {
        // 「もくもく会」を照合語にすると、もくもく会の記事すべてが言及になってしまう。
        // 測れないなら測らない —— null のまま置く
        var store = await RefreshAsync([Event("もくもく会", 20)], [Article("もくもく会に行った")]);

        Assert.Null(await MentionsOf(store, "もくもく会"));
    }

    [Fact]
    public async Task 終わったばかりのイベントも数え直す()
    {
        // 記事の多くは開催後(参加レポート)に書かれる。過ぎたものを外すと、
        // 言及数が伸びるのはこれからのイベントばかりになる
        var store = await RefreshAsync(
            [Event("PHPerKaigi 2026", -10)], [Article("PHPerKaigi 2026 参加レポート")]);

        Assert.Equal(1, await MentionsOf(store, "PHPerKaigi 2026"));
    }

    [Fact]
    public async Task 記事が1本も無ければ何もしない()
    {
        var store = await RefreshAsync([Event("RubyKaigi 2026", 20)], []);

        // 0 で埋めない —— 記事を1本も集めていない状態は「測った」とは言えない
        Assert.Null(await MentionsOf(store, "RubyKaigi 2026"));
    }
}

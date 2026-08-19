using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Events;
using TechAntenna.Infrastructure.Storage;
using TechAntenna.Web;
using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

/// <summary>
/// 収集の絞り込み。<b>トピックの選択で絞るのが基本だが、購読と面掃きで入ったものは通す</b> ——
/// この2つは検索語で見つけたのではないので、選んだトピックのタグを持っていない。
/// ここで落とすと経路ごと無効になる(実装しても1件も増えない、という壊れ方をする)。
/// </summary>
public class EventCollectionRunnerTests
{
    static readonly DateTimeOffset Now = new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);

    class StubSource(bool worksWithoutTopics, params TechEvent[] events) : IEventSource
    {
        public string Name => "テスト用の収集元";

        public bool WorksWithoutTopics => worksWithoutTopics;

        /// <summary>実際に呼ばれたか(叩きに行かない判断を確かめるため)。</summary>
        public int FetchCount { get; private set; }

        public Task<IReadOnlyList<TechEvent>> FetchAsync(CancellationToken cancellationToken = default)
        {
            FetchCount++;

            return Task.FromResult<IReadOnlyList<TechEvent>>(events);
        }
    }

    static TechEvent Event(string title, string? pickedBy = null, params string[] tags) => new()
    {
        Title = title,
        Url = new Uri($"https://example.com/{Uri.EscapeDataString(title)}"),
        SourceName = "テスト用の収集元",
        StartsAt = Now.AddDays(10),
        CollectedAt = Now,
        PickedBy = pickedBy,
        Tags = tags,
        RawTags = tags,
    };

    /// <summary>止めた収集元を渡して組み立てる(叩きに行かないことを確かめるため)。</summary>
    static async Task<(EventCollectionRunner Runner, InMemoryEventStore Store)> BuildDisabledAsync(
        StubSource source, string sourceName, params string[] selectedTopics)
    {
        var credentials = new ApiCredentials(
            new InMemorySecretStore(TimeProvider.System),
            new EphemeralDataProtectionProvider(),
            NullLogger<ApiCredentials>.Instance);
        var toggles = new SourceToggles(credentials);
        await toggles.SetAsync(SourceToggles.KeyOf(SourceToggles.Event, sourceName), enabled: false);

        return await BuildAsync(source, toggles, selectedTopics);
    }

    static Task<(EventCollectionRunner Runner, InMemoryEventStore Store)> BuildAsync(
        IEventSource source, params string[] selectedTopics) =>
        BuildAsync(source, SourceTogglesTests.AllEnabled(), selectedTopics);

    static async Task<(EventCollectionRunner Runner, InMemoryEventStore Store)> BuildAsync(
        IEventSource source, SourceToggles toggles, params string[] selectedTopics)
    {
        var topics = new InMemoryTopicStore();
        var catalog = new TopicCatalog([.. selectedTopics.Select(t => new TopicCatalogEntry(t, [], null))]);
        if (selectedTopics.Length > 0)
        {
            await topics.UpdateSelectionAsync([.. selectedTopics.Select(TagNormalizer.ToKey)]);
        }

        var events = new InMemoryEventStore();
        var articles = new InMemoryArticleStore();
        var clock = new FakeTimeProvider(Now);

        return (new EventCollectionRunner(
            [source], toggles, events, topics, catalog,
            new TagObserver(new InMemoryTagStore(), articles, events, new InMemoryBookStore(), clock),
            new EventMentionRefresher(events, articles, catalog, clock),
            Options.Create(new CollectionOptions { DelayBetweenSourcesSeconds = 0 }),
            NullLogger<EventCollectionRunner>.Instance), events);
    }

    [Fact]
    public async Task 購読で入ったイベントはトピックに当たらなくても保存する()
    {
        var source = new StubSource(true, Event("RubyKaigi 2026", pickedBy: "RubyKaigi"));
        var (runner, store) = await BuildAsync(source, "AI");

        var result = await runner.RunOnceAsync();

        Assert.Equal(1, result.Added);
        Assert.Single(await store.GetUpcomingAsync(Now, 10));
    }

    [Fact]
    public async Task 検索で入ったイベントはトピックに当たらなければ落とす()
    {
        // 従来どおり。購読の免除を入れたせいで絞りが効かなくなっていないかを見る
        var source = new StubSource(false, Event("関係のない勉強会", tags: "rust"));
        var (runner, store) = await BuildAsync(source, "AI");

        var result = await runner.RunOnceAsync();

        Assert.Equal(0, result.Added);
        Assert.Empty(await store.GetUpcomingAsync(Now, 10));
    }

    [Fact]
    public async Task トピックの選択が空でも購読の経路があれば走る()
    {
        var source = new StubSource(true, Event("RubyKaigi 2026", pickedBy: "RubyKaigi"));
        var (runner, store) = await BuildAsync(source);

        var result = await runner.RunOnceAsync();

        Assert.Null(result.Note);
        Assert.Single(await store.GetUpcomingAsync(Now, 10));
    }

    [Fact]
    public async Task トピックの選択が空で検索しかできない収集元は叩かない()
    {
        // 集まらないと分かっている相手にリクエストを投げない(理由は結果の文言に出す)
        var source = new StubSource(false, Event("勉強会", tags: "ai"));
        var (runner, _) = await BuildAsync(source);

        var result = await runner.RunOnceAsync();

        Assert.Equal(0, source.FetchCount);
        Assert.NotNull(result.Note);
    }

    [Fact]
    public async Task 止めた収集元は叩きに行かない()
    {
        // 画面で止めたら、リクエストを出す前に落とす。集めた結果を捨てるのでは
        // 相手を叩いてしまうので、収集元の一覧から外す形にしてある
        var source = new StubSource(true, Event("止めた相手のイベント", pickedBy: "テスト"));
        var (runner, store) = await BuildDisabledAsync(source, "テスト用の収集元", "AI");

        var result = await runner.RunOnceAsync();

        Assert.Equal(0, source.FetchCount);
        Assert.Equal(0, result.Added);
        Assert.Empty(await store.GetUpcomingAsync(Now, 10));
        // 失敗ではなく「止めている」ことが結果の文言に出る
        Assert.Contains("止まっています", result.Note);
    }
}

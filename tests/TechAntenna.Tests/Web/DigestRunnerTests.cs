using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Storage;
using TechAntenna.Web;
using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

public class DigestRunnerTests
{
    static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>受け取った材料を記録し、固定のダイジェストを返す IDigestComposer。
    /// **1回の実行で2本呼ばれる**ことがあるので、材料は範囲ごとに残す。</summary>
    class StubComposer : IDigestComposer
    {
        public List<DigestMaterials> Received { get; } = [];

        public string Name => "スタブ";

        /// <summary>その範囲で呼ばれていれば材料、呼ばれていなければ null。</summary>
        public DigestMaterials? For(DigestScope scope) =>
            Received.FirstOrDefault(materials => materials.Scope == scope);

        public Task<Digest> ComposeAsync(
            DigestMaterials materials, CancellationToken cancellationToken = default)
        {
            Received.Add(materials);
            return Task.FromResult(new Digest
            {
                Scope = materials.Scope,
                GeneratedAt = Now,
                Lead = "導入。",
                Items = [new DigestItem("見出し", "本文。", null)],
                GeneratorName = Name,
            });
        }
    }

    static Article Article(string title, ArticleKind kind, params string[] tags) => new()
    {
        Title = title,
        Url = new Uri($"https://example.com/{Uri.EscapeDataString(title)}"),
        SourceName = "Zenn",
        Kind = kind,
        Tags = tags,
        CollectedAt = Now.AddHours(-1),
    };

    /// <summary>通知の呼び出しを記録する IDigestNotifier。fail=true なら失敗させる。</summary>
    class StubNotifier(bool fail = false) : IDigestNotifier
    {
        public List<DigestScope> Notified { get; } = [];

        public int CallCount => Notified.Count;

        public string Name => "スタブ通知";

        public Task<bool> NotifyAsync(Digest digest, CancellationToken cancellationToken = default)
        {
            Notified.Add(digest.Scope);
            return fail
                ? Task.FromException<bool>(new HttpRequestException("落ちた"))
                : Task.FromResult(true);
        }
    }

    static DigestRunner Runner(
        StubComposer? composer,
        InMemoryArticleStore articles,
        InMemoryEventStore events,
        InMemoryTopicStore topics,
        InMemoryDigestStore digests,
        TopicCatalog catalog,
        StubNotifier? notifier = null) =>
        new(new StubLlmGateway(digestComposer: composer),
            notifier is null ? [] : [notifier],
            articles,
            events,
            topics,
            digests,
            catalog,
            Options.Create(new DigestOptions()),
            new FakeTimeProvider(Now),
            NullLogger<DigestRunner>.Instance);

    /// <summary>興味トピック(生成AI ← LLM)を選んだ状態のトピックストアと語彙。</summary>
    static async Task<(InMemoryTopicStore Topics, TopicCatalog Catalog)> SelectedTopicsAsync()
    {
        var catalog = new TopicCatalog([
            new TopicCatalogEntry("生成AI", [], null),
            new TopicCatalogEntry("LLM", [], "生成AI"),
        ]);
        var topics = new InMemoryTopicStore();
        await topics.UpsertAsync([new Topic { Key = "生成ai", Display = "生成AI" }], Now);
        await topics.UpdateSelectionAsync(["生成ai"]);

        return (topics, catalog);
    }

    [Fact]
    public async Task LLMが未設定なら実行しない()
    {
        var runner = Runner(
            composer: null,
            new InMemoryArticleStore(),
            new InMemoryEventStore(),
            new InMemoryTopicStore(),
            new InMemoryDigestStore(),
            TopicCatalog.Empty);

        Assert.False(runner.IsConfigured);
        Assert.False((await runner.RunOnceAsync()).Composed);
    }

    [Fact]
    public async Task 材料が無ければ生成せずに保存もしない()
    {
        var composer = new StubComposer();
        var digests = new InMemoryDigestStore();
        var runner = Runner(
            composer,
            new InMemoryArticleStore(),
            new InMemoryEventStore(),
            new InMemoryTopicStore(),
            digests,
            TopicCatalog.Empty);

        var result = await runner.RunOnceAsync();

        Assert.False(result.Composed);
        Assert.Empty(composer.Received);
        Assert.Null(await digests.GetLatestAsync(DigestScope.Overall));
    }

    // 興味トピックを選んでいない状態。全体のサマリーだけが作られること
    [Fact]
    public async Task 興味トピックが未設定なら全体のサマリーだけ作る()
    {
        var composer = new StubComposer();
        var articles = new InMemoryArticleStore();
        await articles.AddRangeAsync([Article("話題の記事", ArticleKind.Article)]);
        var digests = new InMemoryDigestStore();
        var runner = Runner(
            composer, articles, new InMemoryEventStore(), new InMemoryTopicStore(),
            digests, TopicCatalog.Empty);

        var result = await runner.RunOnceAsync();

        var part = Assert.Single(result.Parts);
        Assert.Equal(DigestScope.Overall, part.Scope);
        Assert.Equal(1, part.Items);
        Assert.NotNull(await digests.GetLatestAsync(DigestScope.Overall));
        Assert.Null(await digests.GetLatestAsync(DigestScope.Interests));
        Assert.Contains(composer.For(DigestScope.Overall)!.Articles, a => a.Title == "話題の記事");
    }

    // 2本を別々に保存し、通知も範囲ごとに1通ずつ送ること
    [Fact]
    public async Task 興味トピックがあれば2本作って別々に通知する()
    {
        var (topics, catalog) = await SelectedTopicsAsync();
        var articles = new InMemoryArticleStore();
        await articles.AddRangeAsync([
            Article("話題の記事", ArticleKind.Article),
            Article("LLMの記事", ArticleKind.Article, "llm"),
        ]);
        var digests = new InMemoryDigestStore();
        var notifier = new StubNotifier();
        var runner = Runner(
            new StubComposer(), articles, new InMemoryEventStore(), topics,
            digests, catalog, notifier);

        var result = await runner.RunOnceAsync();

        Assert.Equal(
            [DigestScope.Overall, DigestScope.Interests],
            result.Parts.Select(part => part.Scope));
        Assert.Equal(2, result.Notified);
        Assert.Equal([DigestScope.Overall, DigestScope.Interests], notifier.Notified);
        Assert.NotNull(await digests.GetLatestAsync(DigestScope.Overall));
        Assert.NotNull(await digests.GetLatestAsync(DigestScope.Interests));
    }

    // 全体のサマリーは「興味トピック関係なし」。材料にトピックもイベントも混ぜない
    [Fact]
    public async Task 全体のサマリーの材料は興味トピックに依らない()
    {
        var (topics, catalog) = await SelectedTopicsAsync();
        var articles = new InMemoryArticleStore();
        await articles.AddRangeAsync([Article("LLMの記事", ArticleKind.Article, "llm")]);
        var events = new InMemoryEventStore();
        await events.AddRangeAsync([Event()]);

        var composer = new StubComposer();
        var runner = Runner(composer, articles, events, topics, new InMemoryDigestStore(), catalog);

        await runner.RunOnceAsync();

        var overall = composer.For(DigestScope.Overall)!;
        Assert.Empty(overall.SelectedTopics);
        Assert.Empty(overall.UpcomingEvents);
    }

    [Fact]
    public async Task 通知に失敗しても生成は成功のまま保存される()
    {
        var articles = new InMemoryArticleStore();
        await articles.AddRangeAsync([Article("話題の記事", ArticleKind.Article)]);
        var digests = new InMemoryDigestStore();
        var runner = Runner(
            new StubComposer(), articles, new InMemoryEventStore(), new InMemoryTopicStore(),
            digests, TopicCatalog.Empty, new StubNotifier(fail: true));

        var result = await runner.RunOnceAsync();

        Assert.True(result.Composed);
        Assert.Equal(1, result.NotifyFailed);
        Assert.NotNull(await digests.GetLatestAsync(DigestScope.Overall));
    }

    [Fact]
    public async Task 材料が無ければ通知もしない()
    {
        var notifier = new StubNotifier();
        var runner = Runner(
            new StubComposer(), new InMemoryArticleStore(), new InMemoryEventStore(),
            new InMemoryTopicStore(), new InMemoryDigestStore(), TopicCatalog.Empty, notifier);

        await runner.RunOnceAsync();

        Assert.Equal(0, notifier.CallCount);
    }

    [Fact]
    public async Task 興味トピックは配下込みで記事とイベントに当てる()
    {
        // 親(生成AI)だけを選んでも、子(LLM)のタグしか持たない記事・イベントが材料に入る
        var (topics, catalog) = await SelectedTopicsAsync();
        var articles = new InMemoryArticleStore();
        await articles.AddRangeAsync([Article("LLMの記事", ArticleKind.Article, "llm")]);
        var events = new InMemoryEventStore();
        await events.AddRangeAsync([Event()]);

        var composer = new StubComposer();
        var runner = Runner(composer, articles, events, topics, new InMemoryDigestStore(), catalog);

        await runner.RunOnceAsync();

        var interests = composer.For(DigestScope.Interests);
        Assert.NotNull(interests);
        Assert.Contains(interests!.UpcomingEvents, e => e.Title == "LLM勉強会");
        Assert.Contains(interests.Articles, article => article.Title == "LLMの記事");
        Assert.Equal(["生成AI"], interests.SelectedTopics);
    }

    static TechEvent Event() => new()
    {
        Title = "LLM勉強会",
        Url = new Uri("https://example.com/llm-event"),
        SourceName = "connpass",
        StartsAt = Now.AddDays(3),
        Tags = ["llm"],
        CollectedAt = Now,
    };
}

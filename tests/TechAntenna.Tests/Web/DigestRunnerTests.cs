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

    /// <summary>受け取った材料を記録し、固定のダイジェストを返す IDigestComposer。</summary>
    class StubComposer : IDigestComposer
    {
        public DigestMaterials? Received { get; private set; }

        public string Name => "スタブ";

        public Task<Digest> ComposeAsync(
            DigestMaterials materials, CancellationToken cancellationToken = default)
        {
            Received = materials;
            return Task.FromResult(new Digest
            {
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
        public int CallCount { get; private set; }

        public string Name => "スタブ通知";

        public Task<bool> NotifyAsync(Digest digest, CancellationToken cancellationToken = default)
        {
            CallCount++;
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
        Assert.Equal(DigestRunResult.Nothing, await runner.RunOnceAsync());
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
        Assert.Null(composer.Received);
        Assert.Null(await digests.GetLatestAsync());
    }

    [Fact]
    public async Task 直近の話題からダイジェストを生成して保存する()
    {
        var composer = new StubComposer();
        var articles = new InMemoryArticleStore();
        await articles.AddRangeAsync([Article("話題の記事", ArticleKind.Article)]);
        var digests = new InMemoryDigestStore();
        var runner = Runner(
            composer, articles, new InMemoryEventStore(), new InMemoryTopicStore(),
            digests, TopicCatalog.Empty);

        var result = await runner.RunOnceAsync();

        Assert.True(result.Composed);
        Assert.Equal(1, result.Items);
        Assert.NotNull(await digests.GetLatestAsync());
        Assert.Contains(
            composer.Received!.TrendingArticles, article => article.Title == "話題の記事");
    }

    [Fact]
    public async Task 生成できたら通知する()
    {
        var articles = new InMemoryArticleStore();
        await articles.AddRangeAsync([Article("話題の記事", ArticleKind.Article)]);
        var notifier = new StubNotifier();
        var runner = Runner(
            new StubComposer(), articles, new InMemoryEventStore(), new InMemoryTopicStore(),
            new InMemoryDigestStore(), TopicCatalog.Empty, notifier);

        var result = await runner.RunOnceAsync();

        Assert.Equal(1, notifier.CallCount);
        Assert.Equal(1, result.Notified);
        Assert.Equal(0, result.NotifyFailed);
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
        Assert.NotNull(await digests.GetLatestAsync());
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
        var catalog = new TopicCatalog([
            new TopicCatalogEntry("生成AI", [], null),
            new TopicCatalogEntry("LLM", [], "生成AI"),
        ]);
        var topics = new InMemoryTopicStore();
        await topics.UpsertAsync([new Topic { Key = "生成ai", Display = "生成AI" }], Now);
        await topics.UpdateSelectionAsync(["生成ai"]);

        var articles = new InMemoryArticleStore();
        await articles.AddRangeAsync([Article("LLMの記事", ArticleKind.Article, "llm")]);
        var events = new InMemoryEventStore();
        await events.AddRangeAsync([new TechEvent
        {
            Title = "LLM勉強会",
            Url = new Uri("https://example.com/llm-event"),
            SourceName = "connpass",
            StartsAt = Now.AddDays(3),
            Tags = ["llm"],
            CollectedAt = Now,
        }]);

        var composer = new StubComposer();
        var runner = Runner(
            composer, articles, events, topics, new InMemoryDigestStore(), catalog);

        await runner.RunOnceAsync();

        Assert.NotNull(composer.Received);
        Assert.Contains(composer.Received!.UpcomingEvents, e => e.Title == "LLM勉強会");
        Assert.Equal(["生成AI"], composer.Received.SelectedTopics);
        // 記事は「直近の話題」と「興味トピック」のどちらかに入っていればよい
        // (話題度上位に入った分は興味側から除く実装のため)
        Assert.Contains(
            composer.Received.TrendingArticles.Concat(composer.Received.InterestArticles),
            article => article.Title == "LLMの記事");
    }
}

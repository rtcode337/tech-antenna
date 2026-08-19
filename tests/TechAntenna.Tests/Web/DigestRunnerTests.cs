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
    /// 1回の実行で2本呼ばれることがあるので、材料は範囲ごとに残す。</summary>
    class StubComposer(string name = "スタブ", Exception? failure = null) : IDigestComposer
    {
        public List<DigestMaterials> Received { get; } = [];

        public string Name => name;

        /// <summary>その範囲で呼ばれていれば材料、呼ばれていなければ null。</summary>
        public DigestMaterials? For(DigestScope scope) =>
            Received.FirstOrDefault(materials => materials.Scope == scope);

        public Task<Digest> ComposeAsync(
            DigestMaterials materials, CancellationToken cancellationToken = default)
        {
            Received.Add(materials);
            if (failure is not null)
            {
                return Task.FromException<Digest>(failure);
            }

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
        IDigestComposer? composer,
        InMemoryArticleStore articles,
        InMemoryEventStore events,
        InMemoryTopicStore topics,
        InMemoryDigestStore digests,
        TopicCatalog catalog,
        StubNotifier? notifier = null,
        IReadOnlyList<DigestGenerator>? generators = null) =>
        new(new StubLlmGateway(digestComposer: composer, digestGenerators: generators),
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
        // 通知は生成と逆順。ntfy のアプリは新着が上に並ぶので、最後に送った
        // 「技術界隈全体」が一番上に出る(画面の文言は生成順のまま)
        Assert.Equal([DigestScope.Interests, DigestScope.Overall], notifier.Notified);
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

    [Fact]
    public async Task 複数のAIに同じ材料で書かせて全部保存する()
    {
        // ホームで読み比べるので、同じ回・同じ材料でそろっていることが要点
        var articles = new InMemoryArticleStore();
        await articles.AddRangeAsync([Article("話題の記事", ArticleKind.News)]);
        var digests = new InMemoryDigestStore();
        var main = new StubComposer("メインAI");
        var sub = new StubComposer("サブAI");
        var runner = Runner(
            main,
            articles,
            new InMemoryEventStore(),
            new InMemoryTopicStore(),
            digests,
            TopicCatalog.Empty,
            generators:
            [
                new DigestGenerator("chiezo:main", main.Name, true, main),
                new DigestGenerator("chiezo:sub", sub.Name, false, sub),
            ]);

        var result = await runner.RunOnceAsync();

        var run = await digests.GetLatestRunAsync(DigestScope.Overall);
        Assert.Equal(2, run.Count);
        // 先頭はメイン(ホームの既定の表示と通知に使う)
        Assert.True(run[0].IsPrimary);
        Assert.Equal("chiezo:main", run[0].GeneratorKey);
        Assert.False(run[1].IsPrimary);
        // 同じ回として寄せられる(時刻ではなく回で比べる)
        Assert.Equal(run[0].RunId, run[1].RunId);
        // 材料は同じものを渡す
        Assert.Single(main.Received);
        Assert.Single(sub.Received);
        Assert.Contains("AI 2 本", result.Describe());
    }

    [Fact]
    public async Task サブが失敗してもメインは残す()
    {
        // 読みたいのはメインで、比較はおまけ
        var articles = new InMemoryArticleStore();
        await articles.AddRangeAsync([Article("話題の記事", ArticleKind.News)]);
        var digests = new InMemoryDigestStore();
        var main = new StubComposer("メインAI");
        var sub = new StubComposer("サブAI", new HttpRequestException("落ちた"));
        var notifier = new StubNotifier();
        var runner = Runner(
            main,
            articles,
            new InMemoryEventStore(),
            new InMemoryTopicStore(),
            digests,
            TopicCatalog.Empty,
            notifier,
            generators:
            [
                new DigestGenerator("chiezo:main", main.Name, true, main),
                new DigestGenerator("chiezo:sub", sub.Name, false, sub),
            ]);

        await runner.RunOnceAsync();

        var run = await digests.GetLatestRunAsync(DigestScope.Overall);
        Assert.Equal("chiezo:main", Assert.Single(run).GeneratorKey);
        // 通知はメインの 1 本だけ(サブは比較のためのもの)
        Assert.Equal(1, notifier.CallCount);
    }

    [Fact]
    public async Task メインが失敗しても同じ回のサブは保存する()
    {
        // 以前はここで捨てていた。Task.WhenAll がメインの例外で待ち合わせごと
        // 投げるので、同じ回に書けていたサブまで受け取れなかった ——
        // 読み比べ用の相手がいちばん要る日に、記録が何も残らなかった
        var articles = new InMemoryArticleStore();
        await articles.AddRangeAsync([Article("話題の記事", ArticleKind.News)]);
        var digests = new InMemoryDigestStore();
        var main = new StubComposer("メインAI", new HttpRequestException("Chiezo がエラーを返した(HTTP 502)"));
        var sub = new StubComposer("サブAI");
        var notifier = new StubNotifier();
        var runner = Runner(
            main, articles, new InMemoryEventStore(), new InMemoryTopicStore(), digests,
            TopicCatalog.Empty, notifier,
            generators:
            [
                new DigestGenerator("chiezo:main", main.Name, true, main),
                new DigestGenerator("chiezo:sub", sub.Name, false, sub),
            ]);

        // メインが書けなかった回はジョブとしては失敗(定期実行のログと画面に出す)
        var error = await Assert.ThrowsAsync<DigestPrimaryFailedException>(
            () => runner.RunOnceAsync());

        // …が、サブの書いたものは残っていて、通知も届いている
        var saved = Assert.Single(await digests.GetLatestRunAsync(DigestScope.Overall));
        Assert.Equal("chiezo:sub", saved.GeneratorKey);
        // 通知とホームは「1本目」を使うので、残ったサブをメインに繰り上げる
        Assert.True(saved.IsPrimary);
        Assert.Equal([DigestScope.Overall], notifier.Notified);
        // 何が落ちて何が残ったかを1文で言い切る(画面には「失敗: 」を付けて出る)。
        // 全文を固定する —— ここは運用中に読む唯一の手掛かりなので、
        // 直すときは「読んで打つ手が分かるか」を見てから直す
        Assert.Equal(
            "メインの AI が書けませんでした(技術界隈全体: メインAI — "
            + "Chiezo がエラーを返した(HTTP 502))。"
            + " その範囲はサブの AI が書いたもので代替して保存しています。"
            + " 保存できたぶん: 技術界隈全体 1 項目。 ntfy へ 1 通 通知しました。",
            error.Message);
    }

    [Fact]
    public async Task 全体が失敗しても興味トピックは作る()
    {
        // 以前は道連れになっていた。先に作る「全体」で例外が上がると、
        // 2本目の「興味トピック」は試すことすらできなかった
        var articles = new InMemoryArticleStore();
        await articles.AddRangeAsync([Article("LLMの記事", ArticleKind.Article, "llm")]);
        var (topics, catalog) = await SelectedTopicsAsync();
        var digests = new InMemoryDigestStore();
        var composer = new ScopeFailingComposer("メインAI", DigestScope.Overall);
        var runner = Runner(
            composer, articles, new InMemoryEventStore(), topics, digests, catalog,
            new StubNotifier());

        var error = await Assert.ThrowsAsync<DigestPrimaryFailedException>(
            () => runner.RunOnceAsync());

        Assert.Null(await digests.GetLatestAsync(DigestScope.Overall));
        Assert.NotNull(await digests.GetLatestAsync(DigestScope.Interests));
        // 失敗した範囲と、作れた範囲の両方を文言に出す
        Assert.Contains("技術界隈全体", error.Message);
        Assert.Contains("興味トピック", error.Message);
    }

    [Fact]
    public async Task 両方の範囲でメインが失敗したら両方を文言に出す()
    {
        var articles = new InMemoryArticleStore();
        await articles.AddRangeAsync([Article("LLMの記事", ArticleKind.Article, "llm")]);
        var (topics, catalog) = await SelectedTopicsAsync();
        var digests = new InMemoryDigestStore();
        var composer = new StubComposer("メインAI", new HttpRequestException("落ちた"));
        var runner = Runner(
            composer, articles, new InMemoryEventStore(), topics, digests, catalog,
            new StubNotifier());

        var error = await Assert.ThrowsAsync<DigestPrimaryFailedException>(
            () => runner.RunOnceAsync());

        // 片方だけ落ちたのか両方なのかで打つ手が違う
        Assert.Contains("技術界隈全体", error.Message);
        Assert.Contains("興味トピック", error.Message);
        // 代替が1本も無いので、そう書かない
        Assert.DoesNotContain("サブの AI が書いたもの", error.Message);
    }

    /// <summary>指定した範囲でだけ失敗する IDigestComposer(範囲どうしの独立を見るため)。</summary>
    class ScopeFailingComposer(string name, DigestScope failing) : IDigestComposer
    {
        public string Name => name;

        public Task<Digest> ComposeAsync(
            DigestMaterials materials, CancellationToken cancellationToken = default) =>
            materials.Scope == failing
                ? Task.FromException<Digest>(new HttpRequestException("落ちた"))
                : Task.FromResult(new Digest
                {
                    Scope = materials.Scope,
                    GeneratedAt = Now,
                    Lead = "導入。",
                    Items = [new DigestItem("見出し", "本文。", null)],
                    GeneratorName = name,
                });
    }
}

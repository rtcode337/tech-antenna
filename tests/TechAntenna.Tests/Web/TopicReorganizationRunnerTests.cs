using Microsoft.Extensions.Logging.Abstractions;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;
using TechAntenna.Core.Trends;
using TechAntenna.Infrastructure.Storage;
using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

public class TopicReorganizationRunnerTests
{
    class StubTrendSource(IReadOnlyList<TrendTopicCandidate> candidates) : ITrendTopicSource
    {
        public string Name => "スタブ";

        public Task<IReadOnlyList<TrendTopicCandidate>> FetchAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(candidates);
    }

    /// <summary>聞かれた語を記録するだけの分類器。分類は何も返さない(検証は Unknown になる)。</summary>
    class RecordingClassifier : ITopicClassifier
    {
        public string Name => "記録用";

        public List<IReadOnlyList<string>> Asked { get; } = [];

        public Task<IReadOnlyList<TopicClassifierVerdict>> ClassifyAsync(
            IReadOnlyList<string> tags,
            IReadOnlyList<TopicCatalogEntry> existingTopics,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Asked.Add(tags);

            return Task.FromResult<IReadOnlyList<TopicClassifierVerdict>>([]);
        }
    }

    /// <summary>決められた応答を返す分類器。</summary>
    class StubClassifier(IReadOnlyList<TopicClassifierVerdict> verdicts) : ITopicClassifier
    {
        public string Name => "スタブ";

        public Task<IReadOnlyList<TopicClassifierVerdict>> ClassifyAsync(
            IReadOnlyList<string> tags,
            IReadOnlyList<TopicCatalogEntry> existingTopics,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default) => Task.FromResult(verdicts);
    }

    static TopicReorganizationRunner NewRunner(
        TopicCatalog catalog,
        ITrendTopicSource source,
        ITagStore tagStore,
        ITopicStore topicStore,
        ITopicClassifier? classifier = null,
        IArticleStore? articleStore = null)
    {
        var articles = articleStore ?? new InMemoryArticleStore();
        var events = new InMemoryEventStore();
        var books = new InMemoryBookStore();

        return new TopicReorganizationRunner(
            catalog,
            [source],
            tagStore,
            topicStore,
            articles,
            events,
            books,
            new TagRenormalizationRunner(catalog, articles, events, books),
            new TopicCatalogRefresher(catalog, topicStore, tagStore),
            NullLogger<TopicReorganizationRunner>.Instance,
            TimeProvider.System,
            classifier);
    }

    [Fact]
    public async Task 話題度は親へ合算される()
    {
        // 親(プログラミング言語)は構造の語で自身の話題度が付かない。
        // 合算しないとツリーの並びで沈み、子が根として孤立して見える
        var catalog = new TopicCatalog(
        [
            new TopicCatalogEntry("プログラミング", [], null),
            new TopicCatalogEntry("プログラミング言語", [], "プログラミング"),
            new TopicCatalogEntry("Python", [], "プログラミング言語"),
            new TopicCatalogEntry("Rust", [], "プログラミング言語"),
        ]);
        var tags = new InMemoryTagStore();
        var topics = new InMemoryTopicStore();
        await SeedAsync(catalog, tags, topics);

        await NewRunner(catalog, new StubTrendSource(
        [
            new TrendTopicCandidate("Python", 30, "スタブ"),
            new TrendTopicCandidate("Rust", 10, "スタブ"),
        ]), tags, topics).RunOnceAsync();

        var byKey = (await topics.GetAllAsync()).ToDictionary(topic => topic.Key);
        // 単体はソース内シェア(合計に対する割合 × 100)なので Python=75, Rust=25
        Assert.Equal(75, byKey["python"].TrendScore, precision: 5);
        Assert.Equal(25, byKey["rust"].TrendScore, precision: 5);
        // 合算は自身 + 配下の合計。親自身の単体は 0 のまま
        Assert.Equal(0, byKey["プログラミング言語"].TrendScore, precision: 5);
        Assert.Equal(100, byKey["プログラミング言語"].SubtreeTrendScore, precision: 5);
        Assert.Equal(100, byKey["プログラミング"].SubtreeTrendScore, precision: 5);
        Assert.Equal(75, byKey["python"].SubtreeTrendScore, precision: 5);
    }

    [Fact]
    public async Task いまの正規化で作られないキーの行は掃除される()
    {
        // 正規化の規則を変えると、以前のキーの行が幽霊として残る
        // (中身は正しい行に合流済みなのに、観測は行を消さないため)
        var tags = new InMemoryTagStore();
        var topics = new InMemoryTopicStore();
        var at = new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);
        await tags.ObserveAsync(
        [
            // ハッシュタグの印・末尾カンマ・区切りだけ —— いまの正規化では作られない
            new TagObservation("#生成ai", ArticleCount: 1),
            new TagObservation("生成ai,", ArticleCount: 1),
            new TagObservation(",", ArticleCount: 1),
            new TagObservation("生成ai", ArticleCount: 1),
        ], at);

        await NewRunner(TopicCatalog.Empty, new StubTrendSource([]), tags, topics).RunOnceAsync();

        Assert.Equal(["生成ai"], (await tags.GetAllAsync()).Select(tag => tag.Key));
    }

    [Fact]
    public async Task トレンドで見つかった新語はその回では聞かず次の回に聞く()
    {
        // **押すまで何語 LLM に流れるか分からない**のを避けるため、その場で取得した語は足さない。
        // 今回のトレンドで見つかった語はタグとして残り、次の回の対象になる
        var tags = new InMemoryTagStore();
        var topics = new InMemoryTopicStore();
        var classifier = new RecordingClassifier();
        var trend = new StubTrendSource([new TrendTopicCandidate("Kiro", 10, "スタブ")]);

        await NewRunner(TopicCatalog.Empty, trend, tags, topics, classifier).RunOnceAsync();

        // 1 回目: 聞く語は無い(kiro はタグとして残るだけ)
        Assert.Empty(classifier.Asked.SelectMany(asked => asked));
        var tag = Assert.Single(await tags.GetAllAsync());
        Assert.Equal("kiro", tag.Key);
        Assert.Equal(TagStatus.Pending, tag.Status);

        // 2 回目: 前回のトレンドで現れた語を聞く
        await NewRunner(TopicCatalog.Empty, trend, tags, topics, classifier).RunOnceAsync();

        Assert.Equal(["kiro"], classifier.Asked.SelectMany(asked => asked));
    }

    [Fact]
    public async Task 仕分けの結果が語彙とタグの状態になる()
    {
        var tags = new InMemoryTagStore();
        var topics = new InMemoryTopicStore();
        var at = new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);
        var articles = await ArticlesWith(("rag", 5), ("ニュース", 9));
        // 前回までに観測してあるタグが対象(仕分けは今回の観測より前に走る)
        await tags.ObserveAsync(
            [new TagObservation("rag", ArticleCount: 5), new TagObservation("ニュース", ArticleCount: 9)], at);
        var classifier = new StubClassifier(
        [
            new TopicClassifierVerdict(1, "skip", null, null),
            new TopicClassifierVerdict(2, "new", null, "RAG", "検索して答えさせる手法", "rag"),
        ]);

        await NewRunner(TopicCatalog.Empty, new StubTrendSource([]), tags, topics, classifier, articles)
            .RunOnceAsync();

        var byKey = (await tags.GetAllAsync()).ToDictionary(tag => tag.Key);
        Assert.Equal(TagStatus.Promoted, byKey["rag"].Status);
        Assert.Equal("rag", byKey["rag"].TopicKey);
        Assert.Equal(TagStatus.NotTopic, byKey["ニュース"].Status);
        Assert.Null(byKey["ニュース"].TopicKey);

        var topic = Assert.Single(await topics.GetAllAsync());
        Assert.Equal("RAG", topic.Display);
        Assert.Equal("検索して答えさせる手法", topic.Description);
        // 件数は「自分自身 + 別名」のタグから合算する
        Assert.Equal(5, topic.ArticleCount);
    }

    [Fact]
    public async Task 別名の件数は寄せ先のトピックに合算される()
    {
        // 以前は再正規化の副作用で成立していた。タグ層を挟んだので構造で保証される
        var tags = new InMemoryTagStore();
        var topics = new InMemoryTopicStore();
        var at = new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);
        await topics.UpsertAsync([new Topic { Key = "ai", Display = "AI" }], at);
        var articles = await ArticlesWith(("ai", 2), ("人工知能", 3));
        await tags.ObserveAsync(
            [new TagObservation("ai"), new TagObservation("人工知能")], at);
        await tags.DecideAsync(
        [
            new TagDecision("ai", TagStatus.Promoted, "ai", DecidedBy.Seed),
            new TagDecision("人工知能", TagStatus.Alias, "ai", DecidedBy.Seed),
        ], at);

        await NewRunner(TopicCatalog.Empty, new StubTrendSource([]), tags, topics, articleStore: articles)
            .RunOnceAsync();

        Assert.Equal(5, (await topics.GetAsync("ai"))!.ArticleCount);
    }

    /// <summary>指定のタグを持つ記事を作る(件数の集計は記事ストアから作られる)。</summary>
    static async Task<InMemoryArticleStore> ArticlesWith(params (string Tag, int Count)[] tags)
    {
        var store = new InMemoryArticleStore();
        var articles = new List<Article>();
        foreach (var (tag, count) in tags)
        {
            for (var i = 0; i < count; i++)
            {
                articles.Add(new Article
                {
                    Title = $"{tag} の記事 {i}",
                    Url = new Uri($"https://example.com/{Uri.EscapeDataString(tag)}/{i}"),
                    SourceName = "スタブ",
                    CollectedAt = new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero),
                    // 再正規化が RawTags から Tags を作り直すので、両方に入れておく
                    RawTags = [tag],
                    Tags = [tag],
                });
            }
        }

        await store.AddRangeAsync(articles);

        return store;
    }

    /// <summary>語彙を先に入れておく(シードの代わり)。</summary>
    static async Task SeedAsync(TopicCatalog catalog, ITagStore tags, ITopicStore topics)
    {
        var at = new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);
        await topics.UpsertAsync(
            catalog.Entries.Select(entry => new Topic
            {
                Key = entry.Key,
                Display = entry.Display,
                Parent = entry.Parent is { Length: > 0 } parent ? TagNormalizer.ToKey(parent) : null,
                DecidedBy = DecidedBy.Seed,
            }).ToList(),
            at);
        await tags.ObserveAsync(
            catalog.Entries.Select(entry => new TagObservation(entry.Key)).ToList(), at);
        await tags.DecideAsync(
            catalog.Entries
                .Select(entry => new TagDecision(entry.Key, TagStatus.Promoted, entry.Key, DecidedBy.Seed))
                .ToList(),
            at);
    }
}

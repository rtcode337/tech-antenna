using Microsoft.Extensions.Logging.Abstractions;
using TechAntenna.Core.Abstractions;
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

    static TopicReorganizationRunner NewRunner(
        TopicCatalog catalog,
        ITrendTopicSource source,
        ITopicStore topicStore,
        ITopicClassifier? classifier = null)
    {
        var articles = new InMemoryArticleStore();
        var events = new InMemoryEventStore();
        var books = new InMemoryBookStore();
        var classifications = new InMemoryTopicClassificationStore();

        return new TopicReorganizationRunner(
            catalog,
            [source],
            topicStore,
            articles,
            events,
            books,
            classifications,
            new InMemoryTopicDescriptionStore(),
            new TopicCandidateFinder(
                catalog, topicStore, articles, events, books, classifications, TimeProvider.System),
            new TagRenormalizationRunner(catalog, articles, events, books),
            NullLogger<TopicReorganizationRunner>.Instance,
            TimeProvider.System,
            classifier);
    }

    [Fact]
    public async Task 話題度は親へ合算される()
    {
        // 親(プログラミング言語)は構造の語で自身の話題度が付かない。
        // 合算しないと一覧の取得数から押し出され、子が根として孤立して見える
        var catalog = new TopicCatalog(
        [
            new TopicCatalogEntry("プログラミング", [], null),
            new TopicCatalogEntry("プログラミング言語", [], "プログラミング"),
            new TopicCatalogEntry("Python", [], "プログラミング言語"),
            new TopicCatalogEntry("Rust", [], "プログラミング言語"),
        ]);
        var store = new InMemoryTopicStore();
        var runner = NewRunner(catalog, new StubTrendSource(
        [
            new TrendTopicCandidate("Python", 30, "スタブ"),
            new TrendTopicCandidate("Rust", 10, "スタブ"),
        ]), store);

        await runner.RunOnceAsync();

        var topics = (await store.GetTopicsAsync(10)).ToDictionary(t => t.Tag);
        // 単体はソース内シェア(合計に対する割合 × 100)なので Python=75, Rust=25
        Assert.Equal(75, topics["python"].TrendScore, precision: 5);
        Assert.Equal(25, topics["rust"].TrendScore, precision: 5);
        // 合算は自身 + 配下の合計。親自身の単体は 0 のまま
        Assert.Equal(0, topics["プログラミング言語"].TrendScore, precision: 5);
        Assert.Equal(100, topics["プログラミング言語"].SubtreeTrendScore, precision: 5);
        Assert.Equal(100, topics["プログラミング"].SubtreeTrendScore, precision: 5);
        Assert.Equal(75, topics["python"].SubtreeTrendScore, precision: 5);
    }

    [Fact]
    public async Task いまの正規化で作られないキーの行は掃除される()
    {
        // 正規化の規則を変えると、以前のキーの行が幽霊として一覧に残る
        // (中身は正しい行に合流済みなのに、Upsert は行を消さないため)
        var catalog = new TopicCatalog([new TopicCatalogEntry("生成AI", [], null)]);
        var store = new InMemoryTopicStore();
        var at = new DateTimeOffset(2026, 8, 7, 0, 0, 0, TimeSpan.Zero);
        await store.UpsertAsync(
        [
            // ハッシュタグの印・末尾カンマ・区切りだけ —— いまの正規化では作られない
            new TopicUpdate("#生成ai", "#生成ai", null, 0, 0, 0, 0, 0, 0),
            new TopicUpdate("生成ai,", "生成ai,", null, 0, 0, 0, 0, 0, 0),
            new TopicUpdate(",", ",", null, 0, 0, 0, 0, 0, 0),
            new TopicUpdate("生成ai", "生成AI", null, 0, 0, 0, 0, 0, 0),
        ], at);

        await NewRunner(catalog, new StubTrendSource([]), store).RunOnceAsync();

        var tags = (await store.GetAllAsync()).Select(topic => topic.Tag).ToList();
        Assert.Equal(["生成ai"], tags);
    }

    [Fact]
    public async Task 選択済みなら残骸の行でも消さない()
    {
        // 消すと収集キーワードごと失われる(RemoveAsync の約束)。
        // 表示名が空の行(Display 列より前に書かれた古い行)で確かめる ——
        // 正規化で別のキーになる語は、そもそも選択できない(選択も正規化を通る)
        var store = new InMemoryTopicStore();
        await store.UpsertAsync(
            [new TopicUpdate("ajax", "", null, 0, 0, 0, 0, 0, 0)],
            new DateTimeOffset(2026, 8, 7, 0, 0, 0, TimeSpan.Zero));
        await store.UpdateSelectionAsync(["ajax"]);

        await NewRunner(TopicCatalog.Empty, new StubTrendSource([]), store).RunOnceAsync();

        Assert.Contains("ajax", (await store.GetAllAsync()).Select(topic => topic.Tag));
    }

    [Fact]
    public async Task トレンドで見つかった新語はその回では聞かず次の回に聞く()
    {
        // **押すまで何語 LLM に流れるか分からない**のを避けるため、その場で取得した語は足さない。
        // 今回のトレンドで見つかった語はトピックの行として残り、次の回の候補になる
        var store = new InMemoryTopicStore();
        var classifier = new RecordingClassifier();
        var trend = new StubTrendSource([new TrendTopicCandidate("Kiro", 10, "スタブ")]);

        var runner = NewRunner(TopicCatalog.Empty, trend, store, classifier);
        await runner.RunOnceAsync();

        // 1 回目: 聞く語は無い(kiro は行として残るだけ)
        Assert.Empty(classifier.Asked.SelectMany(asked => asked));
        Assert.Contains("kiro", (await store.GetAllAsync()).Select(topic => topic.Tag));

        // 2 回目: 前回のトレンドで現れた語を聞く
        await NewRunner(TopicCatalog.Empty, trend, store, classifier).RunOnceAsync();

        Assert.Equal(["kiro"], classifier.Asked.SelectMany(asked => asked));
    }
}

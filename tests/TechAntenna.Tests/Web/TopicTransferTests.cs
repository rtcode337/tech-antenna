using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Storage;
using TechAntenna.Infrastructure.Topics;
using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

/// <summary>
/// 語彙と仕分けの持ち出し(<see cref="TopicExporter"/>)と取り込み(<see cref="TopicImporter"/>)。
/// 本番で LLM に仕分けさせた結果を開発サーバーへ運ぶ経路なので、運ぶもの・運ばないものの
/// 線引きをここで固定する。
/// </summary>
public class TopicTransferTests
{
    static readonly DateTimeOffset Now = new(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);

    static TopicExporter NewExporter(ITopicStore topics, ITagStore tags) =>
        new(topics, tags, new FakeTimeProvider(Now));

    static TopicImporter NewImporter(ITopicStore topics, ITagStore tags) =>
        new(
            topics,
            tags,
            new TopicCatalogRefresher(TopicCatalog.Empty, topics, tags),
            NullLogger<TopicImporter>.Instance,
            new FakeTimeProvider(Now));

    /// <summary>持ち出し元の環境をひととおり作る(語彙・別名・トピック外・未仕分け)。</summary>
    static async Task<(InMemoryTopicStore Topics, InMemoryTagStore Tags)> BuildSourceAsync()
    {
        var topics = new InMemoryTopicStore();
        var tags = new InMemoryTagStore();

        await topics.UpsertAsync(
            [
                new Topic
                {
                    Key = "ai",
                    Display = "AI",
                    English = "artificial intelligence",
                    Description = "人工知能。",
                    DecidedBy = DecidedBy.Seed,
                    ArticleCount = 40,
                    TrendScore = 9,
                },
                new Topic
                {
                    Key = "rag",
                    Display = "RAG",
                    Parent = "ai",
                    DecidedBy = DecidedBy.Llm,
                    ArticleCount = 3,
                },
            ],
            Now);
        await topics.UpdateSelectionAsync(["ai"]);

        await tags.ObserveAsync(
            [
                new TagObservation("ai", ArticleCount: 30),
                new TagObservation("人工知能", ArticleCount: 10),
                new TagObservation("rag", ArticleCount: 3),
                new TagObservation("ニュース", ArticleCount: 80),
                new TagObservation("まだ聞いていない語", ArticleCount: 4),
            ],
            Now);
        await tags.DecideAsync(
            [
                new TagDecision("ai", TagStatus.Promoted, "ai", DecidedBy.Seed),
                new TagDecision("人工知能", TagStatus.Alias, "ai", DecidedBy.Seed),
                new TagDecision("rag", TagStatus.Promoted, "rag"),
                new TagDecision("ニュース", TagStatus.NotTopic),
            ],
            Now);

        return (topics, tags);
    }

    [Fact]
    public async Task 持ち出すのは仕分けだけで件数と話題度は含まない()
    {
        // 件数は「その環境が集めたデータ」の話。持ち込むと取り込み先の実データと食い違う
        var (topics, tags) = await BuildSourceAsync();

        var file = await NewExporter(topics, tags).BuildAsync();
        var json = TopicExportJson.Serialize(file);

        Assert.DoesNotContain("articleCount", json);
        Assert.DoesNotContain("trendScore", json);
        // 語彙は正式表記・親・英語・説明・出どころを運ぶ
        var ai = Assert.Single(file.Topics, topic => topic.Key == "ai");
        Assert.Equal("AI", ai.Display);
        Assert.Equal("artificial intelligence", ai.English);
        Assert.Equal("人工知能。", ai.Description);
        Assert.Equal(DecidedBy.Seed, ai.DecidedBy);
        Assert.True(ai.Selected);
        Assert.Equal("ai", Assert.Single(file.Topics, topic => topic.Key == "rag").Parent);
    }

    [Fact]
    public async Task 未仕分けのタグは持ち出さない()
    {
        // まだ何も決まっていないので運ぶ情報が無い(取り込む側は自分のデータから見つける)
        var (topics, tags) = await BuildSourceAsync();

        var file = await NewExporter(topics, tags).BuildAsync();

        Assert.DoesNotContain(file.Tags, tag => tag.Key == "まだ聞いていない語");
        // トピック外の判定は運ぶ —— 運ばないと取り込んだ側が同じ語を LLM に聞き直す
        Assert.Equal(
            TagStatus.NotTopic, Assert.Single(file.Tags, tag => tag.Key == "ニュース").Status);
    }

    [Fact]
    public async Task 持ち出したファイルを空の環境へ取り込むと語彙がそのまま復元される()
    {
        var (source, sourceTags) = await BuildSourceAsync();
        var file = TopicExportJson.Deserialize(
            TopicExportJson.Serialize(await NewExporter(source, sourceTags).BuildAsync()));

        var topics = new InMemoryTopicStore();
        var tags = new InMemoryTagStore();
        var result = await NewImporter(topics, tags).ImportAsync(file);

        Assert.Equal(2, result.Topics);
        Assert.Equal(2, result.TopicsAdded);
        Assert.Equal(4, result.Tags);
        Assert.Equal(4, result.TagsAdded);

        var rag = await topics.GetAsync("rag");
        Assert.Equal("RAG", rag!.Display);
        Assert.Equal("ai", rag.Parent);

        var stored = (await tags.GetAllAsync()).ToDictionary(tag => tag.Key);
        Assert.Equal(TagStatus.Alias, stored["人工知能"].Status);
        Assert.Equal("ai", stored["人工知能"].TopicKey);
        Assert.Equal(TagStatus.NotTopic, stored["ニュース"].Status);
        // 判定日時は持ち出し元のものを保つ(「同じ実行で付けた分類は時刻が揃う」を壊さない)
        Assert.Equal(Now, stored["ニュース"].DecidedAt);
    }

    [Fact]
    public async Task 取り込みは取り込む側の件数を消さない()
    {
        // 件数はファイルに入っていないので、上書きしたら 0 になってしまう
        var (source, sourceTags) = await BuildSourceAsync();
        var file = await NewExporter(source, sourceTags).BuildAsync();

        var topics = new InMemoryTopicStore();
        var tags = new InMemoryTagStore();
        await topics.UpsertAsync(
            [new Topic { Key = "ai", Display = "AI", ArticleCount = 7, TrendScore = 2 }], Now);
        await tags.ObserveAsync([new TagObservation("ai", ArticleCount: 7)], Now);

        await NewImporter(topics, tags).ImportAsync(file);

        var ai = await topics.GetAsync("ai");
        Assert.Equal(7, ai!.ArticleCount);
        Assert.Equal(2, ai.TrendScore);
        Assert.Equal(7, Assert.Single(await tags.GetAllAsync(), tag => tag.Key == "ai").ArticleCount);
    }

    [Fact]
    public async Task 取り込みは取り込む側にしかない語彙を消さない()
    {
        // 取り込みは「別の環境で仕分けた結果を合わせる」操作で、置き換えではない
        var (source, sourceTags) = await BuildSourceAsync();
        var file = await NewExporter(source, sourceTags).BuildAsync();

        var topics = new InMemoryTopicStore();
        var tags = new InMemoryTagStore();
        await topics.UpsertAsync([new Topic { Key = "こちらだけ", Display = "こちらだけ" }], Now);

        await NewImporter(topics, tags).ImportAsync(file);

        Assert.NotNull(await topics.GetAsync("こちらだけ"));
    }

    [Fact]
    public async Task 人が直した仕分けはファイルで上書きしない()
    {
        // 画面からの手直しは LLM より優先する(誤判定を直せる経路として残してある)
        var (source, sourceTags) = await BuildSourceAsync();
        var file = await NewExporter(source, sourceTags).BuildAsync();

        var topics = new InMemoryTopicStore();
        var tags = new InMemoryTagStore();
        await tags.ObserveAsync([new TagObservation("ニュース", ArticleCount: 3)], Now);
        await tags.DecideAsync(
            [new TagDecision("ニュース", TagStatus.Promoted, "ニュース", DecidedBy.Human)], Now);

        var result = await NewImporter(topics, tags).ImportAsync(file);

        var news = Assert.Single(await tags.GetAllAsync(), tag => tag.Key == "ニュース");
        Assert.Equal(TagStatus.Promoted, news.Status);
        Assert.Equal(DecidedBy.Human, news.DecidedBy);
        Assert.True(result.KeptHuman > 0);
    }

    [Fact]
    public async Task 収集対象の選択は指定したときだけ取り込む()
    {
        // 収集キーワードが黙って変わると、イベントと書籍の問い合わせ先が勝手に増減する
        var (source, sourceTags) = await BuildSourceAsync();
        var file = await NewExporter(source, sourceTags).BuildAsync();

        var topics = new InMemoryTopicStore();
        var tags = new InMemoryTagStore();
        await NewImporter(topics, tags).ImportAsync(file);
        Assert.Empty(await topics.GetSelectedAsync());

        var result = await NewImporter(topics, tags).ImportAsync(file, importSelection: true);
        Assert.Equal("ai", Assert.Single(await topics.GetSelectedAsync()).Key);
        Assert.Equal(1, result.Selected);
    }

    [Fact]
    public async Task 実在しない親と寄せ先は落とす()
    {
        // ファイルを信じない。手で編集もできるし、循環したままだとツリーを描く側が延々とたどる
        var file = new TopicExportFile
        {
            Topics =
            [
                new TopicExportEntry("ai", "AI", Parent: "存在しない親"),
                new TopicExportEntry("じぶん", "じぶん", Parent: "じぶん"),
            ],
            Tags =
            [
                new TagExportEntry("人工知能", TagStatus.Alias, "ai"),
                new TagExportEntry("迷子", TagStatus.Alias, "存在しないトピック"),
            ],
        };

        var topics = new InMemoryTopicStore();
        var tags = new InMemoryTagStore();
        var result = await NewImporter(topics, tags).ImportAsync(file);

        Assert.Null((await topics.GetAsync("ai"))!.Parent);
        Assert.Null((await topics.GetAsync("じぶん"))!.Parent);
        Assert.Equal(2, result.DroppedParents);
        Assert.Equal(1, result.DroppedAliases);
        Assert.DoesNotContain(await tags.GetAllAsync(), tag => tag.Key == "迷子");
    }

    [Fact]
    public void 壊れたファイルは例外にする()
    {
        // 黙って空として扱うと「取り込んだのに何も増えない」で原因が分からない
        Assert.Throws<JsonException>(() => TopicExportJson.Deserialize("{ topics: ["));
    }
}

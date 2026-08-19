using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Feeds;

namespace TechAntenna.Tests.Infrastructure;

public class HuggingFacePapersSourceTests
{
    const string Json = """
        [
          {
            "publishedAt": "2026-08-01T20:00:00.000Z",
            "paper": {
              "id": "2608.01492",
              "title": "  Retrieval Augmented Generation for Long Documents  ",
              "publishedAt": "2026-08-02T00:00:00.000Z",
              "summary": "要旨。CC0 なので取り込む",
              "upvotes": 42
            }
          },
          {
            "paper": { "id": "2608.00001", "title": "Generative AI for Code Review" }
          },
          { "paper": { "title": "ID が無いので捨てる" } },
          { "note": "paper が無いので捨てる" }
        ]
        """;

    static HuggingFacePapersSource NewSource(TopicCatalog? catalog = null) =>
        new(new StubHttpClientFactory(Json), catalog);

    [Fact]
    public async Task 話題の論文として取り込む()
    {
        var articles = await NewSource().FetchAsync();

        // ID が無いもの・paper が無いものは落ちる
        Assert.Equal(2, articles.Count);
        var first = articles[0];
        Assert.Equal("Retrieval Augmented Generation for Long Documents", first.Title);
        // リンク先は arXiv の abs ページ —— 読みに行く先はそちらで、重複判定もそろう
        Assert.Equal("https://arxiv.org/abs/2608.01492", first.Url.ToString());
        Assert.Equal(ArticleKind.TrendingPaper, first.Kind);
        Assert.Equal("Hugging Face Daily Papers", first.SourceName);
        Assert.Equal(new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero), first.PublishedAt);
        // 話題の度合いは upvote。はてブ数とは別の列(母集団が違う)
        Assert.Equal(42, first.UpvoteCount);
        // 要旨は取り込む —— arXiv のメタデータは CC0(API Terms of Use に明記)。
        // これがあると論文も要約の対象にできる
        Assert.Equal("要旨。CC0 なので取り込む", first.ContentSnippet);
        // 要約そのものは LLM が後で作る(収集時点では空)
        Assert.Null(first.Summary);
    }

    [Fact]
    public async Task upvoteが無ければ未取得のままにする()
    {
        // null は「この収集元由来でない/取れていない」、0 は「まだ upvote されていない」で別物
        var articles = await NewSource().FetchAsync();

        Assert.Null(articles[1].UpvoteCount);
    }

    [Fact]
    public async Task 論文の公開日が無ければDailyに載った日を使う()
    {
        // 日付が空だと新着順の一覧で最後に沈む
        var articles = await NewSource().FetchAsync();

        Assert.Null(articles[1].PublishedAt);
    }

    [Fact]
    public async Task タイトルからトピックのタグを付ける()
    {
        // この収集元はタグを持たないので、タイトルから拾う(英語のタイトルなので英語別名に当たる)
        var catalog = new TopicCatalog(
        [
            new TopicCatalogEntry("RAG", ["retrieval augmented generation"], null),
            new TopicCatalogEntry("生成AI", ["generative ai"], null),
        ]);

        var articles = await NewSource(catalog).FetchAsync();

        Assert.Equal(["RAG"], articles[0].RawTags);
        Assert.Equal(["rag"], articles[0].Tags);
        Assert.Equal(["生成ai"], articles[1].Tags);
    }
}

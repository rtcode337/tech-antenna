using TechAntenna.Infrastructure.Books;

namespace TechAntenna.Tests.Infrastructure;

public class QiitaBookRecommendationSourceTests
{
    // Qiita API v2 の検索は本文まで返す(記事ごとに引き直さなくてよい)
    const string Response = """
        [
          {
            "url": "https://qiita.com/someone/items/aaaa",
            "title": "エンジニアに読んで欲しい技術書",
            "body": "リーダブルコード https://www.amazon.co.jp/dp/4873115655 と 達人プログラマー https://www.amazon.co.jp/gp/product/4274226298 を薦めます。Kindle 版 https://www.amazon.co.jp/dp/B00KR96M6K もあります。"
          },
          {
            "url": "https://qiita.com/another/items/bbbb",
            "title": "新人におすすめの本",
            "body": "まずは https://www.amazon.co.jp/exec/obidos/ASIN/4873115655/ を読むとよい。同じ本をもう一度 https://www.amazon.co.jp/dp/4873115655 挙げても1票。"
          }
        ]
        """;

    static QiitaBookRecommendationSource NewSource(
        StubHttpClientFactory factory, params IReadOnlyList<string> queries) =>
        new(factory,
            queries.Count > 0 ? queries : ["tag:技術書 stocks:>100"],
            delayBetweenRequests: TimeSpan.Zero);

    [Fact]
    public async Task 複数の記事で薦められた本ほど推薦回数が多くなる()
    {
        var source = NewSource(new StubHttpClientFactory(Response));

        var recommendations = await source.FetchAsync();

        var readable = recommendations.Single(r => r.Isbn13 == "9784873115658");
        Assert.Equal(2, readable.Articles.Count);
        // 同じ記事の中で同じ本が何度出てきても1票
        Assert.Equal(
            ["https://qiita.com/someone/items/aaaa", "https://qiita.com/another/items/bbbb"],
            readable.Articles.Select(article => article.Url));
    }

    [Fact]
    public async Task 書籍でないASINは拾わない()
    {
        var source = NewSource(new StubHttpClientFactory(Response));

        var recommendations = await source.FetchAsync();

        // B00… の Kindle 専売は ISBN-10 として成り立たないので落ちる
        Assert.Equal(2, recommendations.Count);
        Assert.All(recommendations, r => Assert.StartsWith("978", r.Isbn13));
    }

    [Fact]
    public async Task 検索クエリを指定しなければ問い合わせない()
    {
        var factory = new StubHttpClientFactory(Response);
        var source = new QiitaBookRecommendationSource(factory, [" "], delayBetweenRequests: TimeSpan.Zero);

        Assert.Empty(await source.FetchAsync());
        Assert.Empty(factory.RequestedUris);
    }

    [Fact]
    public async Task 同じ記事が複数のクエリに当たっても1票に数える()
    {
        // スタブは全クエリに同じ2記事を返す。記事の URL で重複を落とすので票は増えない
        var factory = new StubHttpClientFactory(Response);
        var source = NewSource(factory, "tag:技術書 stocks:>100", "おすすめ 技術書 stocks:>100");

        var recommendations = await source.FetchAsync();

        Assert.Equal(2, factory.RequestedUris.Count);
        var readable = recommendations.Single(r => r.Isbn13 == "9784873115658");
        Assert.Equal(2, readable.Articles.Count);
    }

    [Fact]
    public async Task ページが埋まらなければ次のページは要求しない()
    {
        // スタブの応答は2記事 < per_page なので、1クエリにつき1リクエストで止まる
        var factory = new StubHttpClientFactory(Response);
        var source = NewSource(factory);

        await source.FetchAsync();

        var uri = Assert.Single(factory.RequestedUris);
        Assert.Contains("page=1", uri.Query);
    }
}

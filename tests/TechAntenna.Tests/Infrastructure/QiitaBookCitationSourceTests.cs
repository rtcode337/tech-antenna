using TechAntenna.Infrastructure.Books;

namespace TechAntenna.Tests.Infrastructure;

public class QiitaBookCitationSourceTests
{
    // トピックの記事(まとめ記事ではない)が、本文で本を引き合いに出している形
    const string Response = """
        [
          {
            "url": "https://qiita.com/someone/items/aaaa",
            "title": "機械学習の前処理でつまずいた話",
            "body": "詳しくは https://www.amazon.co.jp/dp/4873115655 の3章にある。ハードウェアの話 https://www.amazon.co.jp/dp/B00KR96M6K は関係ない。"
          },
          {
            "url": "https://qiita.com/another/items/bbbb",
            "title": "特徴量エンジニアリング入門",
            "body": "同じ本 https://www.amazon.co.jp/gp/product/4873115655 と https://www.amazon.co.jp/dp/4274226298 を参考にした。"
          }
        ]
        """;

    static QiitaBookCitationSource NewSource(
        StubHttpClientFactory factory, params IReadOnlyList<string> templates) =>
        new(new QiitaSearch(factory),
            templates.Count > 0 ? templates : ["tag:{topic} stocks:>50"]);

    [Fact]
    public async Task トピックを検索語にして引用された本を拾う()
    {
        var factory = new StubHttpClientFactory(Response);
        var source = NewSource(factory);

        var citations = await source.FetchAsync("機械学習");

        // 2記事が挙げた本は2票、1記事だけの本は1票
        var shared = citations.Single(citation => citation.Isbn13 == "9784873115658");
        Assert.Equal(2, shared.Articles.Count);
        Assert.Equal(
            ["機械学習の前処理でつまずいた話", "特徴量エンジニアリング入門"],
            shared.Articles.Select(article => article.Title));
        // B0… の ASIN は ISBN として成り立たないので落ちる
        Assert.Equal(2, citations.Count);
    }

    [Fact]
    public async Task クエリの雛形のトピックを置き換えて問い合わせる()
    {
        var factory = new StubHttpClientFactory(Response);
        var source = NewSource(factory, "tag:{topic} stocks:>50", "{topic} 参考書");

        await source.FetchAsync("生成AI");

        Assert.Equal(2, factory.RequestedUris.Count);
        Assert.All(
            factory.RequestedUris,
            uri => Assert.Contains(Uri.EscapeDataString("生成AI"), uri.Query, StringComparison.Ordinal));
    }

    [Fact]
    public async Task トピックが空なら問い合わせない()
    {
        // 選択が空のときにランナーが呼ぶことは無いが、空の検索語で全件を引きに行かせない
        var factory = new StubHttpClientFactory(Response);
        var source = NewSource(factory);

        Assert.Empty(await source.FetchAsync(" "));
        Assert.Empty(factory.RequestedUris);
    }
}

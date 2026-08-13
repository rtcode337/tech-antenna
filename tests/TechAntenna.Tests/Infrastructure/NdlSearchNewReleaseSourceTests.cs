using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Books;

namespace TechAntenna.Tests.Infrastructure;

public class NdlSearchNewReleaseSourceTests
{
    // 実際の応答から要る要素だけを抜いたもの(RSS 2.0 + Dublin Core + dcndl)
    const string Response = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss xmlns:dc="http://purl.org/dc/elements/1.1/"
             xmlns:openSearch="http://a9.com/-/spec/opensearchrss/1.0/"
             xmlns:dcndl="http://ndl.go.jp/dcndl/terms/"
             xmlns:dcterms="http://purl.org/dc/terms/" version="2.0">
          <channel>
            <openSearch:totalResults>2</openSearch:totalResults>
            <item>
              <title>今知っておきたい生成AI厳選100ガイド</title>
              <link>https://ndlsearch.ndl.go.jp/books/R100000002-I034785741</link>
              <dc:publisher>技術評論社</dc:publisher>
              <dc:date>2026</dc:date>
              <dcterms:issued>2026.7</dcterms:issued>
            </item>
            <item>
              <title>Rustではじめる並行プログラミング</title>
              <link>https://ndlsearch.ndl.go.jp/books/R100000002-I034785742</link>
              <dc:publisher>オライリー・ジャパン</dc:publisher>
              <dcterms:issued>2020.3.15</dcterms:issued>
            </item>
          </channel>
        </rss>
        """;

    static readonly TopicCatalog Catalog = new([
        new TopicCatalogEntry("生成AI", [], null),
        new TopicCatalogEntry("Rust", [], null),
    ]);

    static NdlSearchNewReleaseSource NewSource(StubHttpClientFactory factory) =>
        new(factory, TimeProvider.System, Catalog, delayBetweenPages: TimeSpan.Zero);

    [Fact]
    public async Task タイトルからトピックをタグにする()
    {
        // 収集元はタグを持たないので、記事のフィードと同じくタイトルから拾う
        var releases = await NewSource(new StubHttpClientFactory(Response))
            .FetchAsync(new DateOnly(2026, 2, 1));

        var release = Assert.Single(releases);
        Assert.Equal("今知っておきたい生成AI厳選100ガイド", release.Title);
        Assert.Equal(["生成ai"], release.Tags);
        Assert.Equal("技術評論社", release.Publisher);
        Assert.Equal(new DateOnly(2026, 7, 1), release.PublishedOn);
    }

    [Fact]
    public async Task 窓より古い本は落とす()
    {
        // API 側の from でも絞るが、応答に混じることがあるので受け側でも切る
        var releases = await NewSource(new StubHttpClientFactory(Response))
            .FetchAsync(new DateOnly(2026, 2, 1));

        Assert.DoesNotContain(releases, release => release.Title.StartsWith("Rust"));
    }

    [Fact]
    public async Task 分類と刊行日で引く_検索語は投げない()
    {
        // トレンドの軸なので、収集対象に選んだトピックに依存させない
        var factory = new StubHttpClientFactory(Response);

        await NewSource(factory).FetchAsync(new DateOnly(2026, 2, 1));

        var requested = factory.RequestedUris[0];
        Assert.Contains("ndc=007", requested.Query);
        Assert.Contains("from=2026-02-01", requested.Query);
        Assert.DoesNotContain("any=", requested.Query);
    }

    [Theory]
    [InlineData("2026.7", 2026, 7, 1)]
    [InlineData("2026.7.15", 2026, 7, 15)]
    [InlineData("2026-07", 2026, 7, 1)]
    // 月末を超える日付は月末に丸める(書誌の日付は当てにならないことがある)
    [InlineData("2026.2.31", 2026, 2, 28)]
    public void 刊行年月を日付にする(string text, int year, int month, int day) =>
        Assert.Equal(
            new DateOnly(year, month, day), NdlSearchResponseParser.ParseIssued(text));

    [Theory]
    [InlineData("2026")] // 年だけでは窓に入れられない(1月1日の本が大量に並ぶ)
    [InlineData("")]
    [InlineData("不明")]
    public void 年しか分からないものは日付にしない(string text) =>
        Assert.Null(NdlSearchResponseParser.ParseIssued(text));
}

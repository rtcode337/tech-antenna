using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Trends;

namespace TechAntenna.Tests.Infrastructure;

public class HatenaHotentryTrendSourceTests
{
    // hotentry の RSS 1.0 を模したフィード(bookmarkcount と dc:subject を持つ)
    const string Rss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"
                 xmlns="http://purl.org/rss/1.0/"
                 xmlns:dc="http://purl.org/dc/elements/1.1/"
                 xmlns:hatena="http://www.hatena.ne.jp/info/xmlns#">
          <channel rdf:about="https://example.com/hotentry"><title>hotentry</title></channel>
          <item rdf:about="https://example.com/a">
            <title>生成AIの新しい使い方</title>
            <link>https://example.com/a</link>
            <dc:subject>テクノロジー</dc:subject>
            <hatena:bookmarkcount>200</hatena:bookmarkcount>
          </item>
          <item rdf:about="https://example.com/b">
            <title>ブクマ数の無い記事</title>
            <link>https://example.com/b</link>
            <dc:subject>rust</dc:subject>
          </item>
        </rdf:RDF>
        """;

    [Fact]
    public async Task ブックマーク数を重みにタイトルとタグから集計する()
    {
        var catalog = new TopicCatalog([new TopicCatalogEntry("生成AI", [], null)]);
        var source = new HatenaHotentryTrendSource(new StubHttpClientFactory(Rss), catalog);

        var candidates = (await source.FetchAsync()).ToDictionary(c => c.Tag);

        // タイトルから見つけたトピックにブックマーク数が乗る
        Assert.Equal(200, candidates["生成ai"].Score);
        // ブックマーク数の無いエントリは重み 1。dc:subject 由来のタグも数える
        Assert.Equal(1, candidates["rust"].Score);
        // ストップワード(テクノロジー)は正規化で落ちる
        Assert.DoesNotContain("テクノロジー", candidates.Keys);
    }
}

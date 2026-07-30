using TechAntenna.Infrastructure.Feeds;

namespace TechAntenna.Tests.Infrastructure;

public class FeedParserTests
{
    // Zenn が配信している形式を模した RSS 2.0
    const string Rss20 = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:dc="http://purl.org/dc/elements/1.1/">
          <channel>
            <title>サンプルフィード</title>
            <item>
              <title>Blazor Server 入門</title>
              <link>https://example.com/articles/blazor-server</link>
              <pubDate>Tue, 28 Jul 2026 09:30:00 +0900</pubDate>
              <category>Blazor</category>
              <category>C#</category>
              <description>&lt;p&gt;Blazor Server の&lt;strong&gt;基本&lt;/strong&gt;を解説します。&lt;/p&gt;</description>
            </item>
            <item>
              <title>日付なしの記事</title>
              <link>https://example.com/articles/no-date</link>
            </item>
          </channel>
        </rss>
        """;

    // Qiita が配信している形式を模した Atom
    const string AtomFeed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <title>サンプルフィード</title>
          <entry>
            <title>EF Core のマイグレーション運用</title>
            <link rel="alternate" href="https://example.com/items/efcore-migrations"/>
            <published>2026-07-27T21:00:00+09:00</published>
            <updated>2026-07-28T08:00:00+09:00</updated>
            <category term="EFCore"/>
            <category term="PostgreSQL"/>
          </entry>
        </feed>
        """;

    // はてなブックマークが配信している形式を模した RSS 1.0 (RDF)
    const string Rss10 = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"
                 xmlns="http://purl.org/rss/1.0/"
                 xmlns:dc="http://purl.org/dc/elements/1.1/">
          <channel rdf:about="https://example.com/hotentry">
            <title>サンプルフィード</title>
          </channel>
          <item rdf:about="https://example.com/entries/dotnet-10-release">
            <title>.NET 10 リリースノートを読む</title>
            <link>https://example.com/entries/dotnet-10-release</link>
            <dc:date>2026-07-26T12:00:00+09:00</dc:date>
            <dc:subject>dotnet</dc:subject>
            <dc:subject>release</dc:subject>
          </item>
        </rdf:RDF>
        """;

    [Fact]
    public void RSS20を解析できる()
    {
        var entries = FeedParser.Parse(Rss20);

        Assert.Equal(2, entries.Count);
        var first = entries[0];
        Assert.Equal("Blazor Server 入門", first.Title);
        Assert.Equal(new Uri("https://example.com/articles/blazor-server"), first.Url);
        Assert.Equal(new DateTimeOffset(2026, 7, 28, 9, 30, 0, TimeSpan.FromHours(9)), first.PublishedAt);
        Assert.Equal(["Blazor", "C#"], first.Tags);
        // description の HTML はタグを除いたテキストになる
        Assert.Equal("Blazor Server の 基本 を解説します。", first.Summary);
    }

    [Fact]
    public void descriptionが無いエントリはSummaryがnullになる()
    {
        var entries = FeedParser.Parse(Rss20);

        Assert.Null(entries[1].Summary);
    }

    [Fact]
    public void RSS20で日付が無いエントリはPublishedAtがnullになる()
    {
        var entries = FeedParser.Parse(Rss20);

        Assert.Null(entries[1].PublishedAt);
    }

    [Fact]
    public void Atomを解析できる()
    {
        var entries = FeedParser.Parse(AtomFeed);

        var entry = Assert.Single(entries);
        Assert.Equal("EF Core のマイグレーション運用", entry.Title);
        Assert.Equal(new Uri("https://example.com/items/efcore-migrations"), entry.Url);
        // published と updated の両方があるときは published を優先する
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 21, 0, 0, TimeSpan.FromHours(9)), entry.PublishedAt);
        Assert.Equal(["EFCore", "PostgreSQL"], entry.Tags);
    }

    [Fact]
    public void RSS10を解析できる()
    {
        var entries = FeedParser.Parse(Rss10);

        var entry = Assert.Single(entries);
        Assert.Equal(".NET 10 リリースノートを読む", entry.Title);
        Assert.Equal(new Uri("https://example.com/entries/dotnet-10-release"), entry.Url);
        Assert.Equal(new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.FromHours(9)), entry.PublishedAt);
        Assert.Equal(["dotnet", "release"], entry.Tags);
    }

    [Fact]
    public void 未対応の形式はFormatExceptionを投げる()
    {
        Assert.Throws<FormatException>(() => FeedParser.Parse("<html></html>"));
    }
}

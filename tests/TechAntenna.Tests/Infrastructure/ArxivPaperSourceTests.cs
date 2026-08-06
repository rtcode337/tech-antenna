using Microsoft.Extensions.Time.Testing;
using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Feeds;
using TechAntenna.Infrastructure.Storage;

namespace TechAntenna.Tests.Infrastructure;

public class ArxivPaperSourceTests
{
    // arXiv は Atom を返す。abstract は summary に入っているが取り込まない
    const string Response = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <entry>
            <id>http://arxiv.org/abs/2608.02599v1</id>
            <title>Bridging AI and Power Systems Education</title>
            <published>2026-08-03T17:59:09Z</published>
            <link href="https://arxiv.org/abs/2608.02599v1" rel="alternate" type="text/html"/>
            <summary>著者が書いた abstract の本文。</summary>
            <category term="cs.LG"/>
          </entry>
        </feed>
        """;

    static FakeTimeProvider Clock() =>
        new(new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero));

    static async Task<ITopicStore> StoreWith(params string[] displays)
    {
        var store = new InMemoryTopicStore();
        await store.UpsertAsync(
            displays.Select(d => new TopicUpdate(TagNormalizer.ToKey(d), d, null, 1, 1, 1, 0, 0, 0)).ToList(),
            new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero));
        await store.UpdateSelectionAsync(displays);
        return store;
    }

    static TopicCatalog Catalog() => new(
    [
        new TopicCatalogEntry("生成AI", ["generative ai"], null),
        new TopicCatalogEntry("LLM", ["large language model"], "生成AI"),
    ]);

    static ArxivPaperSource NewSource(StubHttpClientFactory factory, ITopicStore topics) =>
        new(factory, Clock(), topics, Catalog(), delayBetweenKeywords: TimeSpan.Zero);

    [Fact]
    public async Task 検索は英語表記でタグは正式表記にする()
    {
        // arXiv は英語の索引なので、日本語の正式表記をそのまま投げると 0 件になる
        var factory = new StubHttpClientFactory(Response);
        var source = NewSource(factory, await StoreWith("生成AI"));

        var paper = Assert.Single(await source.FetchAsync());

        Assert.Contains("generative%20ai", Assert.Single(factory.RequestedUris).Query);
        Assert.Equal(["生成ai"], paper.Tags);
    }

    [Fact]
    public async Task 選択トピックを検索語にして論文として保存する()
    {
        var source = NewSource(new StubHttpClientFactory(Response), await StoreWith("生成AI"));

        var paper = Assert.Single(await source.FetchAsync());

        Assert.Equal(ArticleKind.Paper, paper.Kind);
        Assert.Equal("arXiv", paper.SourceName);
        Assert.Equal(new Uri("https://arxiv.org/abs/2608.02599v1"), paper.Url);
        // 検索語をタグにする(arXiv の分類 cs.LG は使わない)
        Assert.Equal(["生成ai"], paper.Tags);
        Assert.Equal(["生成AI"], paper.RawTags);
    }

    [Fact]
    public async Task abstractは取り込まない()
    {
        // 著者の文章なので保持しない。本文が無いので要約ジョブの対象からも外れる
        var source = NewSource(new StubHttpClientFactory(Response), await StoreWith("生成AI"));

        Assert.Null(Assert.Single(await source.FetchAsync()).ContentSnippet);
    }

    [Fact]
    public async Task 複数のトピックで見つかった論文はタグをまとめる()
    {
        var factory = new StubHttpClientFactory(Response);
        var source = NewSource(factory, await StoreWith("生成AI", "LLM"));

        var paper = Assert.Single(await source.FetchAsync());

        Assert.Equal(2, factory.RequestedUris.Count);
        // 並びは検索した順(選択トピックはキーの昇順で返るので llm が先)
        Assert.Equal(["llm", "生成ai"], paper.Tags);
    }

    [Fact]
    public async Task トピックを選んでいなければ問い合わせない()
    {
        var factory = new StubHttpClientFactory(Response);
        var source = NewSource(factory, new InMemoryTopicStore());

        Assert.Empty(await source.FetchAsync());
        Assert.Empty(factory.RequestedUris);
    }
}

using TechAntenna.Core.Topics;

namespace TechAntenna.Tests.Core;

/// <summary>DB の語彙とタグから、読み取り用のカタログを組む(旧 Extend の置き換え)。</summary>
public class TopicCatalogBuilderTests
{
    static Topic NewTopic(string key, string display, string? parent = null) =>
        new() { Key = key, Display = display, Parent = parent };

    static Tag Alias(string key, string topicKey) =>
        new() { Key = key, Status = TagStatus.Alias, TopicKey = topicKey };

    [Fact]
    public void 別名タグから同義語を組む()
    {
        // **別名の一覧をどこかに二重で持たない** —— 「その語彙へ寄せると決めたタグ」が別名
        var entries = TopicCatalogBuilder.Build(
            [NewTopic("ai", "AI")],
            [Alias("人工知能", "ai"), Alias("ai技術", "ai")]);

        var catalog = new TopicCatalog(entries);

        Assert.Equal("ai", catalog.Resolve("人工知能"));
        Assert.Equal("ai", catalog.Resolve("AI技術"));
        Assert.Equal(["ai技術", "人工知能"], catalog.AliasesOf("ai"));
    }

    [Fact]
    public void 仕分けの済んでいないタグは別名にしない()
    {
        var entries = TopicCatalogBuilder.Build(
            [NewTopic("ai", "AI")],
            [
                new Tag { Key = "未仕分け", Status = TagStatus.Pending },
                new Tag { Key = "トピック外", Status = TagStatus.NotTopic },
                new Tag { Key = "ai", Status = TagStatus.Promoted, TopicKey = "ai" },
            ]);

        Assert.Empty(Assert.Single(entries).Aliases);
        Assert.Equal("未仕分け", new TopicCatalog(entries).Resolve("未仕分け"));
    }

    [Fact]
    public void 親子と説明と英語表記を写す()
    {
        var topics = new List<Topic>
        {
            NewTopic("ai", "AI"),
            new()
            {
                Key = "生成ai",
                Display = "生成AI",
                Parent = "ai",
                English = "generative ai",
                Description = "文章や画像を生成するモデルの総称",
            },
        };

        var catalog = new TopicCatalog(TopicCatalogBuilder.Build(topics, []));

        Assert.Equal("ai", catalog.ParentOf("生成ai"));
        Assert.Equal("generative ai", catalog.EnglishTermOf("生成ai"));
        Assert.Equal("文章や画像を生成するモデルの総称", catalog.DescriptionOf("生成ai"));
    }
}

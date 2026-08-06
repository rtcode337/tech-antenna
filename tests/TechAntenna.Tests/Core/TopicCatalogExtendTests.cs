using TechAntenna.Core.Topics;

namespace TechAntenna.Tests.Core;

public class TopicCatalogExtendTests
{
    static readonly DateTimeOffset At = new(2026, 8, 6, 0, 0, 0, TimeSpan.Zero);

    static TopicCatalog NewCatalog() => new(
    [
        new TopicCatalogEntry("AI", ["人工知能"], null),
        new TopicCatalogEntry("生成AI", [], "AI"),
    ]);

    [Fact]
    public void 同義語の分類で既存トピックへ寄るようになる()
    {
        var catalog = NewCatalog();
        catalog.Extend([
            new TopicClassification
            {
                Tag = "ai技術", Kind = TopicClassificationKind.Alias,
                TargetKey = "ai", ClassifiedAt = At,
            },
        ]);

        Assert.Equal("ai", catalog.Resolve("AI技術"));
        Assert.True(catalog.Contains("ai技術"));
    }

    [Fact]
    public void 新トピックの分類でツリーに親付きで入る()
    {
        var catalog = NewCatalog();
        catalog.Extend([
            new TopicClassification
            {
                Tag = "ai駆動開発", Kind = TopicClassificationKind.NewTopic,
                Display = "AI駆動開発", ParentKey = "生成ai", ClassifiedAt = At,
            },
        ]);

        Assert.Equal("AI駆動開発", catalog.DisplayOf("ai駆動開発"));
        Assert.Equal("生成ai", catalog.ParentOf("ai駆動開発"));
    }

    [Fact]
    public void 既存のキーと衝突する新トピックはJSON側を優先する()
    {
        var catalog = NewCatalog();
        catalog.Extend([
            new TopicClassification
            {
                Tag = "生成ai", Kind = TopicClassificationKind.NewTopic,
                Display = "生成AI(別物)", ParentKey = null, ClassifiedAt = At,
            },
        ]);

        // Display のキーが既存(生成ai)ではないので追加はされるが、既存の生成AI はそのまま
        Assert.Equal("生成AI", catalog.DisplayOf("生成ai"));
    }

    [Fact]
    public void Skipはカタログを変えない()
    {
        var catalog = NewCatalog();
        var before = catalog.Entries.Count;

        catalog.Extend([
            new TopicClassification
            {
                Tag = "あとで読む", Kind = TopicClassificationKind.Skip, ClassifiedAt = At,
            },
        ]);

        Assert.Equal(before, catalog.Entries.Count);
        Assert.False(catalog.Contains("あとで読む"));
    }
}

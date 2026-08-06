using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;

namespace TechAntenna.Tests.Core;

public class TopicClassificationValidatorTests
{
    static readonly DateTimeOffset At = new(2026, 8, 6, 0, 0, 0, TimeSpan.Zero);

    static TopicCatalog NewCatalog() => new(
    [
        new TopicCatalogEntry("AI", ["人工知能"], null),
        new TopicCatalogEntry("生成AI", ["generative ai"], "AI"),
    ]);

    [Fact]
    public void 同義語は寄せ先のキーに解決される()
    {
        var accepted = TopicClassificationValidator.Validate(
            ["ai技術"],
            [new TopicClassifierVerdict(1, "alias", "人工知能", null)],
            NewCatalog(), At);

        var alias = Assert.Single(accepted);
        Assert.Equal(TopicClassificationKind.Alias, alias.Kind);
        // 別名(人工知能)を指してきても正式表記のキー(ai)へ解決する
        Assert.Equal("ai", alias.TargetKey);
    }

    [Fact]
    public void 実在しない寄せ先と自分自身への寄せは捨てる()
    {
        var accepted = TopicClassificationValidator.Validate(
            ["量子コンピュータ", "ai"],
            [
                new TopicClassifierVerdict(1, "alias", "存在しないトピック", null),
                new TopicClassifierVerdict(2, "alias", "AI", null),
            ],
            NewCatalog(), At);

        // 捨てた語は保存されない(次回もう一度 LLM に聞く)
        Assert.Empty(accepted);
    }

    [Fact]
    public void 新トピックは親が実在するときだけ親付きで通る()
    {
        var accepted = TopicClassificationValidator.Validate(
            ["ai駆動開発", "謎の技術"],
            [
                new TopicClassifierVerdict(1, "new", "生成AI", "AI駆動開発"),
                new TopicClassifierVerdict(2, "new", "実在しない親", "謎の技術"),
            ],
            NewCatalog(), At);

        Assert.Equal(2, accepted.Count);
        Assert.Equal("生成ai", accepted[0].ParentKey);
        Assert.Equal("AI駆動開発", accepted[0].Display);
        // 親が実在しなければ、親なしの新トピックとして通す(分類自体は無駄にしない)
        Assert.Null(accepted[1].ParentKey);
    }

    [Fact]
    public void 同じバッチで通る新トピックを親にできる()
    {
        var accepted = TopicClassificationValidator.Validate(
            ["ロボティクス", "ヒューマノイド"],
            [
                new TopicClassifierVerdict(1, "new", null, "ロボティクス"),
                new TopicClassifierVerdict(2, "new", "ロボティクス", "ヒューマノイド"),
            ],
            NewCatalog(), At);

        Assert.Equal("ロボティクス", accepted[1].ParentKey);
    }

    [Fact]
    public void 新トピックと言われても実在する表記なら同義語に読み替える()
    {
        var accepted = TopicClassificationValidator.Validate(
            ["genai"],
            [new TopicClassifierVerdict(1, "new", null, "生成AI")],
            NewCatalog(), At);

        var alias = Assert.Single(accepted);
        Assert.Equal(TopicClassificationKind.Alias, alias.Kind);
        Assert.Equal("生成ai", alias.TargetKey);
    }

    [Fact]
    public void skipは保存され応答に無い番号は保存されない()
    {
        var accepted = TopicClassificationValidator.Validate(
            ["あとで読む", "回答の無い語"],
            [new TopicClassifierVerdict(1, "skip", null, null)],
            NewCatalog(), At);

        var skip = Assert.Single(accepted);
        Assert.Equal(TopicClassificationKind.Skip, skip.Kind);
        Assert.Equal("あとで読む", skip.Tag);
    }

    [Fact]
    public void unknownは保存しない()
    {
        // skip(トピックでないと確信)と違い、「分からない」は確定させず次回もう一度聞く
        // —— 新語は時間が経てば分類できるようになる
        var accepted = TopicClassificationValidator.Validate(
            ["新しすぎる語"],
            [new TopicClassifierVerdict(1, "unknown", null, null)],
            NewCatalog(), At);

        Assert.Empty(accepted);
    }
}

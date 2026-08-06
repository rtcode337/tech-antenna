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
    public void 実在しない寄せ先と自分自身への寄せは期限付きのUnknownになる()
    {
        var accepted = TopicClassificationValidator.Validate(
            ["量子コンピュータ", "ai"],
            [
                new TopicClassifierVerdict(1, "alias", "存在しないトピック", null),
                new TopicClassifierVerdict(2, "alias", "AI", null),
            ],
            NewCatalog(), At);

        // ツリーには入れないが保存はする(毎回聞き直さない。期限が切れたら再挑戦)
        Assert.Equal(2, accepted.Count);
        Assert.All(accepted, c => Assert.Equal(TopicClassificationKind.Unknown, c.Kind));
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
    public void skipは確定し応答に無い番号は期限付きのUnknownになる()
    {
        var accepted = TopicClassificationValidator.Validate(
            ["あとで読む", "回答の無い語"],
            [new TopicClassifierVerdict(1, "skip", null, null)],
            NewCatalog(), At);

        Assert.Equal(2, accepted.Count);
        Assert.Equal(TopicClassificationKind.Skip, accepted[0].Kind);
        Assert.Equal("あとで読む", accepted[0].Tag);
        Assert.Equal(TopicClassificationKind.Unknown, accepted[1].Kind);
        Assert.Equal("回答の無い語", accepted[1].Tag);
    }

    [Fact]
    public void unknownは期限付きで保存される()
    {
        // skip(トピックでないと確信)と違い「分からない」は確定させない —— が、
        // 保存はする。保存しないと毎回同じ語を聞き直して LLM の枠を無駄にする
        // (期限が切れたら未分類に戻す判定は収集側)
        var accepted = TopicClassificationValidator.Validate(
            ["新しすぎる語"],
            [new TopicClassifierVerdict(1, "unknown", null, null)],
            NewCatalog(), At);

        var unknown = Assert.Single(accepted);
        Assert.Equal(TopicClassificationKind.Unknown, unknown.Kind);
        Assert.Equal(At, unknown.ClassifiedAt);
    }
}

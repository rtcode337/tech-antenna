using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;

namespace TechAntenna.Tests.Core;

public class TopicClassificationValidatorTests
{
    static readonly DateTimeOffset At = new(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);

    const int RetryDays = 7;

    static TopicCatalog NewCatalog() => new(
    [
        new TopicCatalogEntry("AI", ["人工知能"], null),
        new TopicCatalogEntry("生成AI", ["generative ai"], "AI"),
    ]);

    static TopicClassification Validate(
        IReadOnlyList<string> tags, IReadOnlyList<TopicClassifierVerdict> verdicts) =>
        TopicClassificationValidator.Validate(tags, verdicts, NewCatalog(), At, RetryDays);

    [Fact]
    public void 同義語は寄せ先のキーに解決される()
    {
        var result = Validate(
            ["ai技術"], [new TopicClassifierVerdict(1, "alias", "人工知能", null)]);

        var decision = Assert.Single(result.Decisions);
        Assert.Equal(TagStatus.Alias, decision.Status);
        // 別名(人工知能)を指してきても正式表記のキー(ai)へ解決する
        Assert.Equal("ai", decision.TopicKey);
        Assert.Empty(result.NewTopics);
    }

    [Fact]
    public void 実在しない寄せ先と自分自身への寄せは期限付きの保留になる()
    {
        var result = Validate(
            ["量子コンピュータ", "ai"],
            [
                new TopicClassifierVerdict(1, "alias", "存在しないトピック", null),
                new TopicClassifierVerdict(2, "alias", "AI", null),
            ]);

        // 語彙には入れないが状態は残す(毎回聞き直さない。期限が切れたら再挑戦)
        Assert.Equal(2, result.Decisions.Count);
        Assert.All(result.Decisions, decision =>
        {
            Assert.Equal(TagStatus.Unresolved, decision.Status);
            Assert.Equal(At.AddDays(RetryDays), decision.RetryAfter);
        });
    }

    [Fact]
    public void 新トピックは親が実在するときだけ親付きで通る()
    {
        var result = Validate(
            ["ai駆動開発", "謎の技術"],
            [
                new TopicClassifierVerdict(1, "new", "生成AI", "AI駆動開発"),
                new TopicClassifierVerdict(2, "new", "実在しない親", "謎の技術"),
            ]);

        var topics = result.NewTopics.ToDictionary(topic => topic.Key);
        Assert.Equal("生成ai", topics["ai駆動開発"].Parent);
        Assert.Equal("AI駆動開発", topics["ai駆動開発"].Display);
        // 親が実在しなければ、親なしの新トピックとして通す(仕分け自体は無駄にしない)
        Assert.Null(topics["謎の技術"].Parent);
        Assert.All(result.Decisions, decision => Assert.Equal(TagStatus.Promoted, decision.Status));
    }

    [Fact]
    public void 新トピックどうしの親子を同じバッチで許す()
    {
        var result = Validate(
            ["自作言語", "パーサ"],
            [
                new TopicClassifierVerdict(1, "new", null, "自作言語"),
                new TopicClassifierVerdict(2, "new", "自作言語", "パーサ"),
            ]);

        Assert.Equal("自作言語", result.NewTopics.Single(t => t.Key == "パーサ").Parent);
    }

    [Fact]
    public void 正式表記がタグと違う新トピックはタグを別名として寄せる()
    {
        // タグ `生成ai技術` に対して正式表記 `生成AI技術` が返るような場合。
        // タグの側は正式表記のキーへ寄せないと、同じ語が 2 つの行に割れる
        var result = Validate(
            ["ai駆動"], [new TopicClassifierVerdict(1, "new", null, "AI駆動開発")]);

        var decision = Assert.Single(result.Decisions);
        Assert.Equal(TagStatus.Alias, decision.Status);
        Assert.Equal("ai駆動開発", decision.TopicKey);
        Assert.Equal("ai駆動開発", Assert.Single(result.NewTopics).Key);
    }

    [Fact]
    public void 既にあるトピックの表記を新トピックと言ってきたら寄せ先にする()
    {
        var result = Validate(
            ["ai技術"], [new TopicClassifierVerdict(1, "new", null, "AI")]);

        var decision = Assert.Single(result.Decisions);
        Assert.Equal(TagStatus.Alias, decision.Status);
        Assert.Equal("ai", decision.TopicKey);
        Assert.Empty(result.NewTopics);
    }

    [Fact]
    public void トピックでない語はトピック外として確定する()
    {
        var result = Validate(["ニュース"], [new TopicClassifierVerdict(1, "skip", null, null)]);

        var decision = Assert.Single(result.Decisions);
        Assert.Equal(TagStatus.NotTopic, decision.Status);
        Assert.Null(decision.RetryAfter);
    }

    [Fact]
    public void 応答に無い番号も期限付きの保留にする()
    {
        // 保存しないと毎回同じ語を聞き直して LLM の枠を無駄にする
        var result = Validate(
            ["返ってこなかった語", "rag"],
            [new TopicClassifierVerdict(2, "new", null, "RAG")]);

        Assert.Equal(2, result.Decisions.Count);
        var missing = result.Decisions[0];
        Assert.Equal(TagStatus.Unresolved, missing.Status);
        Assert.Equal(At.AddDays(RetryDays), missing.RetryAfter);
    }

    [Fact]
    public void 説明と英語表記も新トピックに写す()
    {
        // どちらも分類の応答に相乗りしている(呼び出しを増やさないため)
        var result = Validate(
            ["rag"],
            [new TopicClassifierVerdict(
                1, "new", "生成AI", "RAG", "検索で引いた文書を添えて答えさせる手法",
                "retrieval augmented generation")]);

        var topic = Assert.Single(result.NewTopics);
        Assert.Equal("検索で引いた文書を添えて答えさせる手法", topic.Description);
        Assert.Equal("retrieval augmented generation", topic.English);
        Assert.Equal(DecidedBy.Llm, topic.DecidedBy);
    }
}

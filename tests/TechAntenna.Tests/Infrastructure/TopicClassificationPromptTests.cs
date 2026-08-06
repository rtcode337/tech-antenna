using System.Text.Json;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Topics;

namespace TechAntenna.Tests.Infrastructure;

public class TopicClassificationPromptTests
{
    [Fact]
    public void 入力に既存ツリーと番号付きのタグが載る()
    {
        var input = TopicClassificationPrompt.ForTags(
            ["ai駆動開発", "vibe coding"],
            [
                new TopicCatalogEntry("AI", [], null),
                new TopicCatalogEntry("生成AI", [], "AI"),
            ]);

        Assert.Contains("- AI", input);
        Assert.Contains("- 生成AI(親: AI)", input);
        Assert.Contains("1. ai駆動開発", input);
        Assert.Contains("2. vibe coding", input);
    }

    [Fact]
    public void 応答から分類を読み取る()
    {
        using var doc = JsonDocument.Parse("""
            {"classifications": [
              {"index": 1, "kind": "alias", "target": "生成AI"},
              {"index": 2, "kind": "new", "display": "Vibe Coding", "target": "AIエージェント"},
              {"index": 3, "kind": "skip"}
            ]}
            """);

        var verdicts = TopicClassificationPrompt.ReadVerdicts(doc.RootElement);

        Assert.Equal(3, verdicts.Count);
        Assert.Equal(new[] { "alias", "new", "skip" }, verdicts.Select(v => v.Kind));
        Assert.Equal("Vibe Coding", verdicts[1].Display);
        Assert.Equal("AIエージェント", verdicts[1].Target);
    }

    [Fact]
    public void 新トピックの一言説明も読む()
    {
        // 説明は**分類の応答に相乗り**させている(説明のために呼び出しを増やさないため)
        using var doc = JsonDocument.Parse(
            """
            {"classifications": [
              {"index":1,"kind":"new","display":"RAG","target":"生成AI",
               "description":"検索で引いた文書を添えて答えさせる手法"},
              {"index":2,"kind":"skip","description":"  "}
            ]}
            """);

        var verdicts = TopicClassificationPrompt.ReadVerdicts(doc.RootElement);

        Assert.Equal("検索で引いた文書を添えて答えさせる手法", verdicts[0].Description);
        // 空文字は説明なしとして扱う(知らない語は空で返させている)
        Assert.Null(verdicts[1].Description);
    }

    [Fact]
    public void 形の崩れた要素は読み飛ばす()
    {
        using var doc = JsonDocument.Parse("""
            {"classifications": [
              {"kind": "skip"},
              {"index": "one", "kind": "skip"},
              {"index": 2, "kind": "skip"}
            ]}
            """);

        var verdict = Assert.Single(TopicClassificationPrompt.ReadVerdicts(doc.RootElement));
        Assert.Equal(2, verdict.Index);
    }

    [Fact]
    public void classificationsが無ければFormatExceptionを投げる()
    {
        using var doc = JsonDocument.Parse("""{"result": "..."}""");

        Assert.Throws<FormatException>(
            () => TopicClassificationPrompt.ReadVerdicts(doc.RootElement));
    }

    [Fact]
    public void スキーマはJSONとして妥当()
    {
        using var doc = JsonDocument.Parse(TopicClassificationPrompt.Schema);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }
}

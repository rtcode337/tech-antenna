using System.Text.Json;
using TechAntenna.Infrastructure.Topics;

namespace TechAntenna.Tests.Infrastructure;

public class TopicDescriptionPromptTests
{
    static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void 番号と説明の対応を読む()
    {
        var verdicts = TopicDescriptionPrompt.ReadDescriptions(Parse("""
            {"descriptions":[
              {"index":1,"text":"検索で引いた文書を添えて答えさせる手法"},
              {"index":2,"text":"型を付けた JavaScript"}
            ]}
            """));

        Assert.Equal([1, 2], verdicts.Select(v => v.Index));
        Assert.Equal("型を付けた JavaScript", verdicts[1].Text);
    }

    [Fact]
    public void 空の説明と形の崩れた要素は捨てる()
    {
        // 知らない語は空文字で返させているので、空を捨てることが「説明を付けない」の実現になる
        var verdicts = TopicDescriptionPrompt.ReadDescriptions(Parse("""
            {"descriptions":[
              {"index":1,"text":""},
              {"index":2,"text":"   "},
              {"index":3},
              {"text":"番号が無い"},
              {"index":5,"text":"生きている説明"}
            ]}
            """));

        Assert.Single(verdicts);
        Assert.Equal(5, verdicts[0].Index);
    }

    [Fact]
    public void 配列が無ければ例外にする()
    {
        // 呼び出し側が「説明 0 件」と取り違えないよう、形が違うことは失敗として伝える
        Assert.Throws<FormatException>(() => TopicDescriptionPrompt.ReadDescriptions(Parse("{}")));
    }

    [Fact]
    public void スキーマはJSONとして妥当()
    {
        // 手で組んだ文字列なので、壊れたまま claude へ渡さないよう形を見ておく
        using var doc = JsonDocument.Parse(TopicDescriptionPrompt.Schema);

        Assert.True(doc.RootElement.TryGetProperty("properties", out var properties));
        Assert.True(properties.TryGetProperty("descriptions", out _));
    }

    [Fact]
    public void 長すぎる説明は切り詰める()
    {
        // 長さは指示だけに任せない(超えた応答をそのまま持つとツールチップが読めない)
        var text = new string('あ', TopicDescriptionPrompt.MaxLength + 50);

        var trimmed = TopicDescriptionPrompt.Trim(text);

        Assert.Equal(TopicDescriptionPrompt.MaxLength + 1, trimmed!.Length);
        Assert.EndsWith("…", trimmed);
    }

    [Fact]
    public void 改行は空白に畳む()
    {
        // title 属性では改行が表示できず、一覧の行送りも崩れる
        Assert.Equal("前half 後half", TopicDescriptionPrompt.Trim("前half\n後half"));
        Assert.Null(TopicDescriptionPrompt.Trim(null));
        Assert.Null(TopicDescriptionPrompt.Trim(" "));
    }
}

using System.Text;
using System.Text.Json;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;

namespace TechAntenna.Infrastructure.Topics;

/// <summary>
/// 同義トピックの統合の指示文・入力・応答の読み取り。Claude Code 版と Anthropic API 版で
/// 同じ文面とスキーマを使う(<see cref="TopicClassificationPrompt"/> と同じ役割)。
/// </summary>
public static class TopicMergePrompt
{
    public const string System =
        "あなたは技術トピックの語彙の整理役。渡された一覧の中から、" +
        "**同じものを指している重複**を見つけて、どちらへ寄せるかを答える。" +
        "into には残すほうのトピックの表記をそのまま書く。" +
        "**粒度の違いは重複ではない**(`AI` と `生成AI`、`クラウド` と `AWS` は別のトピックとして残す)。" +
        "関連しているだけの語も寄せない(`Docker` と `Kubernetes` は別)。" +
        "表記が違うだけで同じものを指す場合(`AI` と `人工知能`、`k8s` と `Kubernetes`)だけ寄せる。" +
        "**寄せる必要が無いトピックは応答に含めない。** 迷ったら含めない —— " +
        "誤って寄せると別の話題がひとつに潰れて元に戻せない。";

    /// <summary>構造化出力のスキーマ。番号と寄せ先の対応で返させる。</summary>
    // 波括弧だらけなので、生文字列の補間ではなく素直に連結する
    public const string Schema =
        "{\"type\":\"object\",\"properties\":{\"merges\":{\"type\":\"array\",\"items\":"
        + "{\"type\":\"object\",\"properties\":{\"index\":{\"type\":\"integer\"},"
        + "\"into\":{\"type\":\"string\"}},"
        + "\"required\":[\"index\",\"into\"]}}},\"required\":[\"merges\"]}";

    /// <summary>語彙の一覧。1 始まりの番号で参照させ、親も添えて粒度が分かるようにする。</summary>
    public static string ForTopics(IReadOnlyList<TopicCatalogEntry> topics)
    {
        var builder = new StringBuilder();
        builder.AppendLine("### トピックの一覧(表記、括弧内は1つ上の粒度と別名)");
        for (var i = 0; i < topics.Count; i++)
        {
            var topic = topics[i];
            builder.Append($"{i + 1}. {topic.Display}");
            if (topic.Parent is { Length: > 0 } parent)
            {
                builder.Append($"(親: {parent})");
            }

            if (topic.Aliases.Count > 0)
            {
                builder.Append($"(別名: {string.Join("・", topic.Aliases.Take(5))})");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>応答(スキーマに沿った JSON のルート)から統合の候補を読み取る。</summary>
    public static IReadOnlyList<TopicMergeVerdict> ReadMerges(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("merges", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("応答に merges 配列が無い。");
        }

        var verdicts = new List<TopicMergeVerdict>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("index", out var index)
                || index.ValueKind != JsonValueKind.Number
                || !item.TryGetProperty("into", out var into)
                || into.ValueKind != JsonValueKind.String
                || into.GetString() is not { Length: > 0 } target)
            {
                continue;
            }

            verdicts.Add(new TopicMergeVerdict(index.GetInt32(), target.Trim()));
        }

        return verdicts;
    }
}

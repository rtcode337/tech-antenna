using System.Text;
using System.Text.Json;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;

namespace TechAntenna.Infrastructure.Topics;

/// <summary>
/// トピック分類の指示文・入力・応答の読み取り。Claude Code 版と Anthropic API 版で
/// 同じ文面とスキーマを使い、実装を切り替えても分類の基準が変わらないようにする。
/// </summary>
public static class TopicClassificationPrompt
{
    public const string System =
        "あなたは技術トピックの分類器。収集した記事・イベント・書籍のタグのうち、" +
        "トピック一覧にまだ無い語を、既存のトピックツリーと突き合わせて分類する。" +
        "各タグについて kind をちょうど1つ選ぶ。" +
        "alias = 既存トピックと同じものを指す別表記(target に既存トピックの表記をそのまま書く)。" +
        "粒度が違うだけの語(上位概念・下位概念)は alias にしない。" +
        "new = 新しい技術トピック(display に画面へ出す正式表記、" +
        "target に1つ上の粒度のトピックの表記。最上位なら target を書かない)。" +
        "skip = 技術トピックでないと確信できる語(メディア名・イベント名・読み手の行動・一般語)。" +
        "unknown = その語を知らない、または新しすぎて判断できない。" +
        "迷ったら unknown にする —— 誤った分類はツリーを壊し、誤った skip はその語を" +
        "二度と分類しなくなるが、unknown は次の機会にもう一度判断される。";

    /// <summary>構造化出力のスキーマ。番号と分類の対応で返させる(タグの表記を写させない)。</summary>
    // 波括弧だらけなので、生文字列の補間ではなく素直に連結する
    public const string Schema =
        "{\"type\":\"object\",\"properties\":{\"classifications\":{\"type\":\"array\",\"items\":"
        + "{\"type\":\"object\",\"properties\":{\"index\":{\"type\":\"integer\"},"
        + "\"kind\":{\"type\":\"string\",\"enum\":[\"alias\",\"new\",\"skip\",\"unknown\"]},"
        + "\"target\":{\"type\":\"string\"},\"display\":{\"type\":\"string\"}},"
        + "\"required\":[\"index\",\"kind\"]}}},\"required\":[\"classifications\"]}";

    /// <summary>既存ツリーと未知タグをまとめた入力。タグは 1 始まりの番号で参照させる。</summary>
    public static string ForTags(
        IReadOnlyList<string> tags, IReadOnlyList<TopicCatalogEntry> existingTopics)
    {
        var builder = new StringBuilder();

        builder.AppendLine("### 既存のトピック(表記、括弧内は1つ上の粒度)");
        foreach (var entry in existingTopics)
        {
            builder.AppendLine(entry.Parent is { Length: > 0 } parent
                ? $"- {entry.Display}(親: {parent})"
                : $"- {entry.Display}");
        }

        builder.AppendLine();
        builder.AppendLine("### 分類するタグ");
        for (var i = 0; i < tags.Count; i++)
        {
            builder.AppendLine($"{i + 1}. {tags[i]}");
        }

        return builder.ToString();
    }

    /// <summary>
    /// 応答(スキーマに沿った JSON のルート)から分類を読み取る。
    /// 形の崩れた要素は読み飛ばす(検証は <see cref="TopicClassificationValidator"/> の仕事)。
    /// </summary>
    public static IReadOnlyList<TopicClassifierVerdict> ReadVerdicts(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("classifications", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("応答に classifications 配列が無い。");
        }

        var verdicts = new List<TopicClassifierVerdict>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("index", out var index)
                || index.ValueKind != JsonValueKind.Number
                || !item.TryGetProperty("kind", out var kind)
                || kind.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            verdicts.Add(new TopicClassifierVerdict(
                index.GetInt32(),
                kind.GetString() ?? "",
                GetString(item, "target"),
                GetString(item, "display")));
        }

        return verdicts;
    }

    static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

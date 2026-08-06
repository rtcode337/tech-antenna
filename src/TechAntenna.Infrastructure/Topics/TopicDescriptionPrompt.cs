using System.Text;
using System.Text.Json;
using TechAntenna.Core.Abstractions;

namespace TechAntenna.Infrastructure.Topics;

/// <summary>
/// 用語の一言説明の指示文・入力・応答の読み取り。Claude Code 版と Anthropic API 版で
/// 同じ文面とスキーマを使い、実装を切り替えても説明の口調が変わらないようにする
/// (<see cref="TopicClassificationPrompt"/> と同じ役割)。
/// </summary>
public static class TopicDescriptionPrompt
{
    /// <summary>説明の長さの上限(全角)。一覧のツールチップに収まる長さにとどめる。</summary>
    public const int MaxLength = 120;

    // 文字数の上限(int の定数)を埋め込むので const にはできない
    public static readonly string System =
        "あなたは技術用語の説明者。与えられた技術トピックの用語に、日本語で一言説明を付ける。" +
        "説明は1文か2文、全角 " + MaxLength + " 文字以内。" +
        "「〜とは」「〜です」のような前置きや丁寧語は使わず、名詞で終わる簡潔な説明にする。" +
        "何のための技術・概念なのかを最初に書く(例: 生成AI → " +
        "「文章や画像などを生成するモデルの総称。学習した分布から新しいデータを作る」)。" +
        "知らない用語・複数の意味があって特定できない用語は、推測で書かずに説明を空文字にする。";

    /// <summary>構造化出力のスキーマ。番号と説明の対応で返させる(用語の表記を写させない)。</summary>
    // 波括弧だらけなので、生文字列の補間ではなく素直に連結する
    public const string Schema =
        "{\"type\":\"object\",\"properties\":{\"descriptions\":{\"type\":\"array\",\"items\":"
        + "{\"type\":\"object\",\"properties\":{\"index\":{\"type\":\"integer\"},"
        + "\"text\":{\"type\":\"string\"}},"
        + "\"required\":[\"index\",\"text\"]}}},\"required\":[\"descriptions\"]}";

    /// <summary>説明させる用語の一覧。1 始まりの番号で参照させる。</summary>
    public static string ForTerms(IReadOnlyList<string> terms)
    {
        var builder = new StringBuilder();
        builder.AppendLine("### 説明する用語");
        for (var i = 0; i < terms.Count; i++)
        {
            builder.AppendLine($"{i + 1}. {terms[i]}");
        }

        return builder.ToString();
    }

    /// <summary>
    /// 応答(スキーマに沿った JSON のルート)から説明を読み取る。
    /// 形の崩れた要素・空の説明は読み飛ばす(**知らない語は空で返させている**ので、
    /// 空を捨てることが「説明を付けない」の実現になる)。
    /// </summary>
    public static IReadOnlyList<TopicDescriptionVerdict> ReadDescriptions(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("descriptions", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("応答に descriptions 配列が無い。");
        }

        var verdicts = new List<TopicDescriptionVerdict>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("index", out var index)
                || index.ValueKind != JsonValueKind.Number
                || !item.TryGetProperty("text", out var text)
                || text.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var trimmed = Trim(text.GetString());
            if (trimmed is null)
            {
                continue;
            }

            verdicts.Add(new TopicDescriptionVerdict(index.GetInt32(), trimmed));
        }

        return verdicts;
    }

    /// <summary>
    /// 説明を画面に出せる形にそろえる。**長さは指示だけに任せない** ——
    /// 上限を超えた応答をそのまま持つと、一覧のツールチップが読めない長さになる。
    /// </summary>
    public static string? Trim(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        // 改行は入れさせない(title 属性では表示できず、一覧の行送りも崩れる)
        trimmed = trimmed.ReplaceLineEndings(" ").Replace("  ", " ");

        return trimmed.Length <= MaxLength ? trimmed : trimmed[..MaxLength].TrimEnd() + "…";
    }
}

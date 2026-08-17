using System.Text.Json;

namespace TechAntenna.Infrastructure.Summarization;

/// <summary>
/// LLM の応答(テキスト)から結果を取り出す。
/// 要約とタイトル翻訳は同じ形(番号 + 文字列の配列)を使うので、
/// 配列と値のプロパティ名だけを差し替えられるようにしてある。
///
/// **応答は素のテキスト**なので、JSON の部分を自分で切り出す —— ブリッジ経由では
/// 構造化出力(`--json-schema`)を通せず、モデルが前置きやコードフェンスを付けることが
/// あるため。切り出せない・形が違うときは例外にする(**誤った結果を紐づけない**)。
/// </summary>
public static class ClaudeCodeResponseParser
{
    /// <summary>結果1件。<c>Index</c> は入力で振った 1 始まりの記事番号。</summary>
    public record Entry(int Index, string Text);

    /// <summary>応答から要約(番号 + 文字列の配列)を取り出す。</summary>
    public static IReadOnlyList<Entry> Parse(
        string text, string arrayName = "summaries", string valueName = "summary") =>
        ReadJson(text, root => ReadEntries(root, arrayName, valueName));

    /// <summary>JSON から「番号 + 文字列」の配列を読む(要約・タイトル翻訳で共通)。</summary>
    public static IReadOnlyList<Entry> ReadEntries(
        JsonElement root, string arrayName, string valueName)
    {
        if (!root.TryGetProperty(arrayName, out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException($"応答の JSON に {arrayName} が無い。");
        }

        return items.EnumerateArray()
            .Select(item => ToEntry(item, valueName))
            .OfType<Entry>()
            .ToList();
    }

    /// <summary>
    /// 応答から JSON を切り出し、その中身を <paramref name="read"/> に渡す。要約もトピック分類も
    /// 外側は同じなので、スキーマごとの読み取りだけを差し替えられるようにしてある。
    /// </summary>
    public static T ReadJson<T>(string text, Func<JsonElement, T> read)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(ExtractJson(text));
        }
        catch (JsonException ex)
        {
            throw new FormatException($"応答が JSON として読めない: {Excerpt(text)}", ex);
        }

        using (doc)
        {
            return read(doc.RootElement);
        }
    }

    /// <summary>
    /// 本文から JSON オブジェクトの部分を切り出す。
    ///
    /// **前置きとコードフェンスを許す。** プロンプトでは「JSON だけ」と指示しているが、
    /// 説明を1行添えてくる応答は実際にあり、そこで丸ごと捨てると1バッチ分の要約が消える。
    /// 取るのは**最初の <c>{</c> から最後の <c>}</c> まで** —— 途中に文字列として
    /// 波括弧が入っていても、外側の対応は保たれる。
    /// </summary>
    public static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        return start >= 0 && end > start ? text[start..(end + 1)] : text.Trim();
    }

    /// <summary>例外メッセージにそのまま載せられる長さに切る。</summary>
    static string Excerpt(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300] + "…";
    }

    static Entry? ToEntry(JsonElement element, string valueName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("index", out var index)
            || index.ValueKind != JsonValueKind.Number
            || !element.TryGetProperty(valueName, out var value))
        {
            return null;
        }

        return value.GetString()?.Trim() is { Length: > 0 } text
            ? new Entry(index.GetInt32(), text)
            : null;
    }
}

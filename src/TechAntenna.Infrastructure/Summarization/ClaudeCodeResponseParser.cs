using System.Text.Json;

namespace TechAntenna.Infrastructure.Summarization;

/// <summary>`claude -p --output-format json` の応答から要約を取り出す。</summary>
public static class ClaudeCodeResponseParser
{
    /// <summary>要約1件。<c>Index</c> は入力で振った 1 始まりの記事番号。</summary>
    public record Entry(int Index, string Summary);

    /// <summary>
    /// 応答 JSON から要約を取り出す。実行自体が失敗していた場合(<c>is_error</c>)は例外にして、
    /// 呼び出し側がバッチごと再試行できるようにする。
    /// </summary>
    public static IReadOnlyList<Entry> Parse(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("claude の応答が JSON として読めない。", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.TryGetProperty("is_error", out var isError)
                && isError.ValueKind == JsonValueKind.True)
            {
                throw new InvalidOperationException(
                    $"claude がエラーを返した: {Describe(root) ?? "詳細不明"}");
            }

            // --json-schema を渡しているので構造化出力に入る。text にフォールバックはしない
            // (形式が崩れた応答を無理に読むと、誤った要約を記事に紐づけてしまうため)
            if (!root.TryGetProperty("structured_output", out var output)
                || !output.TryGetProperty("summaries", out var summaries)
                || summaries.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("claude の応答に structured_output.summaries が無い。");
            }

            return summaries.EnumerateArray()
                .Select(ToEntry)
                .OfType<Entry>()
                .ToList();
        }
    }

    /// <summary>
    /// 失敗したときの原因を応答から取り出す。読めなければ null。
    ///
    /// **claude は失敗の詳細を stderr ではなく stdout の JSON に書く**(認証エラーなら
    /// result に「Failed to authenticate. API Error: 401 …」、api_error_status に 401)。
    /// 終了コードだけを見ていると原因が分からないので、呼び出し側はこれを使う。
    /// </summary>
    public static string? DescribeError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Describe(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static string? Describe(JsonElement root)
    {
        var message = root.TryGetProperty("result", out var result) ? result.GetString() : null;
        var status = root.TryGetProperty("api_error_status", out var s)
            && s.ValueKind == JsonValueKind.Number
                ? s.GetInt32()
                : (int?)null;

        return (message, status) switch
        {
            (null, null) => null,
            (null, { } code) => $"HTTP {code}",
            ({ } text, null) => text,
            ({ } text, { } code) => $"{text}(HTTP {code})",
        };
    }

    static Entry? ToEntry(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("index", out var index)
            || index.ValueKind != JsonValueKind.Number
            || !element.TryGetProperty("summary", out var summary))
        {
            return null;
        }

        return summary.GetString()?.Trim() is { Length: > 0 } text
            ? new Entry(index.GetInt32(), text)
            : null;
    }
}

using System.Globalization;
using System.Text.Json;

namespace TechAntenna.Infrastructure.Books;

/// <summary>openBD のレスポンスから取り出した1冊分の書誌情報。</summary>
public record OpenBdEntry(
    string Isbn13,
    string? Title,
    string? Publisher,
    DateOnly? PublishedOn,
    string? Author,
    Uri? CoverUrl);

/// <summary>
/// openBD の /v1/get レスポンスを解析する。
/// レスポンスは要求した ISBN と同じ順の配列で、見つからなかった ISBN の要素は null になる。
/// 詳細な onix ではなく、扱いやすい summary を読む。
/// </summary>
public static class OpenBdResponseParser
{
    public static IReadOnlyList<OpenBdEntry> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("openBD レスポンスが配列でない。");
        }

        return doc.RootElement.EnumerateArray()
            // 見つからなかった ISBN の要素は null
            .Where(e => e.ValueKind == JsonValueKind.Object)
            .Select(ParseEntry)
            .OfType<OpenBdEntry>()
            .ToList();
    }

    static OpenBdEntry? ParseEntry(JsonElement element)
    {
        if (!element.TryGetProperty("summary", out var summary))
        {
            return null;
        }

        var isbn = GetString(summary, "isbn");
        if (isbn is not { Length: > 0 })
        {
            return null;
        }

        return new OpenBdEntry(
            isbn,
            GetString(summary, "title"),
            GetString(summary, "publisher"),
            ParsePubdate(GetString(summary, "pubdate")),
            GetString(summary, "author"),
            ParseUri(GetString(summary, "cover")));
    }

    // pubdate は "20130301" が基本だが、"201303" や "2013-03-01" も見かける
    static DateOnly? ParsePubdate(string? text)
    {
        if (text is not { Length: > 0 })
        {
            return null;
        }

        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        return digits.Length switch
        {
            8 => TryExact(digits, "yyyyMMdd"),
            6 => TryExact(digits + "01", "yyyyMMdd"),
            4 => TryExact(digits + "0101", "yyyyMMdd"),
            _ => null,
        };
    }

    static DateOnly? TryExact(string value, string format) =>
        DateOnly.TryParseExact(value, format, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() is { Length: > 0 } s ? s : null
            : null;

    static Uri? ParseUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
}

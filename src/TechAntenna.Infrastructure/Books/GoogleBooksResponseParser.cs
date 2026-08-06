using System.Text.Json;
using TechAntenna.Core;

namespace TechAntenna.Infrastructure.Books;

/// <summary>Google Books のレスポンスから取り出した1冊分。</summary>
public record GoogleBookEntry(
    string Title,
    string? Isbn13,
    IReadOnlyList<string> Authors,
    string? Publisher,
    DateOnly? PublishedOn,
    Uri? Url,
    Uri? CoverUrl);

/// <summary>
/// Google Books API の /books/v1/volumes レスポンスを解析する。
/// 取り出すのは書誌事実(タイトル・著者・出版社・刊行日・ISBN・リンク)だけで、
/// description や textSnippet といった出版社の著作物は取り込まない。
/// </summary>
public static class GoogleBooksResponseParser
{
    public static IReadOnlyList<GoogleBookEntry> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);

        // 検索結果が0件のとき items 自体が省略される
        if (!doc.RootElement.TryGetProperty("items", out var items))
        {
            return [];
        }

        return items.EnumerateArray().Select(ParseVolume).ToList();
    }

    static GoogleBookEntry ParseVolume(JsonElement volume)
    {
        var info = volume.TryGetProperty("volumeInfo", out var v) ? v : default;

        return new GoogleBookEntry(
            GetString(info, "title") ?? "",
            FindIsbn13(info),
            GetStringArray(info, "authors"),
            GetString(info, "publisher"),
            ParsePublishedDate(GetString(info, "publishedDate")),
            ParseUri(GetString(info, "infoLink")),
            ParseUri(GetString(
                info.TryGetProperty("imageLinks", out var links) ? links : default,
                "thumbnail")));
    }

    static string? FindIsbn13(JsonElement info)
    {
        if (info.ValueKind != JsonValueKind.Object
            || !info.TryGetProperty("industryIdentifiers", out var ids))
        {
            return null;
        }

        return ids.EnumerateArray()
            .Where(id => GetString(id, "type") == "ISBN_13")
            .Select(id => GetString(id, "identifier"))
            .FirstOrDefault(value => value is { Length: > 0 });
    }

    // publishedDate は "2013"、"2013-03"、"2013-03-01" のいずれもありうる
    static DateOnly? ParsePublishedDate(string? text) => text switch
    {
        { Length: 4 } year when int.TryParse(year, out var y) => new DateOnly(y, 1, 1),
        { Length: 7 } month when DateOnly.TryParse($"{month}-01", out var m) => m,
        { Length: 10 } day when DateOnly.TryParse(day, out var d) => d,
        _ => null,
    };

    static IReadOnlyList<string> GetStringArray(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Select(item => item.GetString() ?? "")
                .Where(s => s.Length > 0)
                .ToList()
            : [];

    static string? GetString(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // http/https 以外(javascript: 等)は href/img src に出すと XSS になるため WebUrl で弾く
    static Uri? ParseUri(string? value) =>
        WebUrl.TryCreate(value, out var uri) ? uri : null;
}

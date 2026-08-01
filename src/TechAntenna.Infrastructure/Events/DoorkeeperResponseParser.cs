using System.Text.Json;

namespace TechAntenna.Infrastructure.Events;

/// <summary>
/// Doorkeeper API のレスポンスから取り出した1イベント。
/// 取り込むのは事実情報だけで、<c>description</c> は取らない —— API 経由のコンテンツは
/// Doorkeeper とその顧客に帰属する(API Terms of Use の Ownership)ため。
/// </summary>
public record DoorkeeperEventEntry(
    string Title,
    Uri Url,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? VenueName,
    string? Address);

/// <summary>
/// Doorkeeper API の /events レスポンス(JSON)を解析する。
/// 各要素は {"event": {...}} で包まれるが、包まれていない形にも保険として対応する。
/// </summary>
public static class DoorkeeperResponseParser
{
    public static IReadOnlyList<DoorkeeperEventEntry> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Doorkeeper レスポンスが配列でない。");
        }

        return doc.RootElement.EnumerateArray()
            .Select(Unwrap)
            .Where(e => e.ValueKind == JsonValueKind.Object)
            .Select(ParseEvent)
            .OfType<DoorkeeperEventEntry>()
            .ToList();
    }

    static JsonElement Unwrap(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("event", out var inner)
            ? inner
            : element;

    static DoorkeeperEventEntry? ParseEvent(JsonElement e)
    {
        // public_url が無いイベントは辿れないので取り込まない
        if (!Uri.TryCreate(GetString(e, "public_url"), UriKind.Absolute, out var url))
        {
            return null;
        }

        return new DoorkeeperEventEntry(
            GetString(e, "title") ?? "",
            url,
            GetDate(e, "starts_at"),
            GetDate(e, "ends_at"),
            GetString(e, "venue_name"),
            GetString(e, "address"));
    }

    static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() is { Length: > 0 } s ? s : null
            : null;

    static DateTimeOffset? GetDate(JsonElement e, string name) =>
        DateTimeOffset.TryParse(GetString(e, name), out var parsed) ? parsed : null;
}

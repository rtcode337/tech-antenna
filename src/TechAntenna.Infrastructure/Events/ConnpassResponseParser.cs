using System.Text.Json;
using TechAntenna.Core;

namespace TechAntenna.Infrastructure.Events;

/// <summary>connpass API のレスポンスから取り出した1イベント。</summary>
public record ConnpassEventEntry(
    string Title,
    Uri Url,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? Place,
    string? Address,
    string? HashTag);

/// <summary>
/// connpass API v2 の /api/v2/events/ レスポンス(JSON)を解析する。
/// v2 のフィールド名を基本とし、v1 系の別名(event_url 等)にも保険として対応する。
/// </summary>
public static class ConnpassResponseParser
{
    public static IReadOnlyList<ConnpassEventEntry> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("events", out var events))
        {
            throw new FormatException("connpass レスポンスに events 配列がない。");
        }

        return events.EnumerateArray().Select(ParseEvent).ToList();
    }

    static ConnpassEventEntry ParseEvent(JsonElement e)
    {
        var url = GetString(e, "url") ?? GetString(e, "event_url")
            ?? throw new FormatException("イベントに url が無い。");

        return new ConnpassEventEntry(
            GetString(e, "title") ?? "",
            // http/https 以外(javascript: 等)は href に出すと XSS になるため WebUrl で弾く
            WebUrl.Require(url),
            GetDate(e, "started_at"),
            GetDate(e, "ended_at"),
            GetString(e, "place"),
            GetString(e, "address"),
            GetString(e, "hash_tag"));
    }

    static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    static DateTimeOffset? GetDate(JsonElement e, string name) =>
        DateTimeOffset.TryParse(GetString(e, name), out var parsed) ? parsed : null;
}

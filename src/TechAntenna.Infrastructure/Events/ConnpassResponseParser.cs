using System.Text.Json;
using TechAntenna.Core;

namespace TechAntenna.Infrastructure.Events;

/// <summary>connpass API のレスポンスから取り出した1イベント。</summary>
/// <param name="Organizer">主催グループ名(<c>group.title</c>)。個人開催ではグループが無いので管理者の表示名で代える。</param>
/// <param name="ParticipantCount">参加者数(<c>accepted</c>)。補欠(<c>waiting</c>)は数えない。</param>
public record ConnpassEventEntry(
    string Title,
    Uri Url,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? Place,
    string? Address,
    string? HashTag,
    string? Organizer = null,
    int? ParticipantCount = null);

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
            GetString(e, "hash_tag"),
            // グループを持たない個人開催のイベントもあるので、無ければ管理者の表示名で代える
            // (公式かどうかの判定材料なので、名前が1つも無いより名乗りがあるほうがよい)
            GetGroupTitle(e) ?? GetString(e, "owner_display_name"),
            // 補欠(waiting)は足さない —— 定員に達したイベントの規模は accepted で足りるうえ、
            // 補欠まで数えると「定員 10 人・補欠 200 人」が大規模イベントとして扱われる
            GetInt(e, "accepted"));
    }

    /// <summary>主催グループ名。v2 では <c>group</c> がオブジェクトで、無いイベントは null。</summary>
    static string? GetGroupTitle(JsonElement e) =>
        e.TryGetProperty("group", out var group) && group.ValueKind == JsonValueKind.Object
            ? GetString(group, "title")
            : null;

    static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    static DateTimeOffset? GetDate(JsonElement e, string name) =>
        DateTimeOffset.TryParse(GetString(e, name), out var parsed) ? parsed : null;

    /// <summary>数値。**欠けている項目は null のまま返す**(0 と混ぜない)。</summary>
    static int? GetInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var parsed)
            ? parsed
            : null;
}

using System.Text.Json;
using TechAntenna.Core;

namespace TechAntenna.Infrastructure.Events;

/// <summary>
/// Doorkeeper API のレスポンスから取り出した1イベント。
/// 取り込むのは事実情報だけで、<c>description</c> は取らない —— API 経由のコンテンツは
/// Doorkeeper とその顧客に帰属する(API Terms of Use の Ownership)ため。
/// </summary>
/// <param name="Organizer">
/// 主催グループ名。<c>group</c> は既定では数値の ID なので、<c>expand[]=group</c> を
/// 付けて問い合わせたときだけ名前が取れる(付いていなければ null)。
/// </param>
/// <param name="ParticipantCount">参加者数(<c>participants</c>)。補欠(<c>waitlisted</c>)は数えない。</param>
public record DoorkeeperEventEntry(
    string Title,
    Uri Url,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    string? VenueName,
    string? Address,
    string? Organizer = null,
    int? ParticipantCount = null);

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
        // public_url が無いイベントは辿れないので取り込まない(http/https 以外も WebUrl が弾く)
        if (!WebUrl.TryCreate(GetString(e, "public_url"), out var url))
        {
            return null;
        }

        return new DoorkeeperEventEntry(
            GetString(e, "title") ?? "",
            url,
            GetDate(e, "starts_at"),
            GetDate(e, "ends_at"),
            GetString(e, "venue_name"),
            GetString(e, "address"),
            GetGroupName(e),
            GetInt(e, "participants"));
    }

    /// <summary>
    /// 主催グループ名。<c>expand[]=group</c> を付けるとオブジェクトで返るので <c>name</c> を読み、
    /// 付いていない(数値の ID が返る)ときは null にする —— **ID から名前を引き直しはしない**。
    /// グループ 1 件につき 1 リクエスト増えるうえ、API は alpha 扱いで形が変わりうるため、
    /// 名前が取れなければ「公式かどうか分からない」で済ませる。
    /// </summary>
    static string? GetGroupName(JsonElement e) =>
        e.TryGetProperty("group", out var group) && group.ValueKind == JsonValueKind.Object
            ? GetString(group, "name")
            : null;

    static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() is { Length: > 0 } s ? s : null
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

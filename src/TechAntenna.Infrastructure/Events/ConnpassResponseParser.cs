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
    public static IReadOnlyList<ConnpassEventEntry> Parse(string json) => ParsePage(json).Events;

    /// <summary>
    /// 1ページぶん。<b>件数まで返す</b>のは、月ごとの面掃き
    /// (<see cref="ConnpassSweepEventSource"/>)が続きを取りに行くかを決めるため ——
    /// 返ってきた件数だけを見ていると、ちょうど 100 件の月で「まだあるのか」が分からない。
    /// </summary>
    /// <param name="Available">その条件に該当する総件数(<c>results_available</c>)。分からなければ null。</param>
    public record Page(IReadOnlyList<ConnpassEventEntry> Events, int? Available);

    public static Page ParsePage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("events", out var events))
        {
            throw new FormatException("connpass レスポンスに events 配列がない。");
        }

        return new Page(
            events.EnumerateArray().Select(ParseEvent).ToList(),
            GetInt(doc.RootElement, "results_available"));
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

    /// <summary>
    /// <c>/api/v2/groups/?subdomain=…</c> のレスポンスから<b>シリーズ ID</b> を取り出す。
    /// 見つからなければ null(名簿の打ち間違い・非公開のグループ)。
    ///
    /// サブドメインで引けるようにするための解決で、購読の名簿を人が書けるようにするもの ——
    /// 数字のシリーズ ID は connpass の画面に出てこないが、サブドメインは
    /// グループの URL(<c>https://&lt;ここ&gt;.connpass.com/</c>)を見れば分かる。
    /// </summary>
    public static string? ParseGroupId(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("groups", out var groups)
            || groups.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var group in groups.EnumerateArray())
        {
            // id は数値で返る。文字列で返すのは、そのままクエリに載せる値だから
            if (group.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number
                && id.TryGetInt64(out var parsed))
            {
                return parsed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return null;
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

    /// <summary>数値。欠けている項目は null のまま返す(0 と混ぜない)。</summary>
    static int? GetInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var parsed)
            ? parsed
            : null;
}

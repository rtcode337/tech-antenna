using System.Globalization;
using System.Text.Json;
using TechAntenna.Core;

namespace TechAntenna.Infrastructure.Books;

/// <summary>楽天ブックスから取り出した1冊分(レビューと書影)。</summary>
public record RakutenBookInfo(string Isbn, int ReviewCount, double? ReviewAverage, Uri? CoverUrl);

/// <summary>
/// 楽天ブックス書籍検索API のレスポンスを解析する。
///
/// 取り出すのは**レビュー件数・平均評価と書影の URL だけ**。商品説明や価格まで取り込むと
/// 表示に別の条件が付いてくるし、書誌情報(タイトル・著者)は Google Books と openBD で足りる。
///
/// **書影を読むのは openBD が技術書の書影をほとんど持っていないため**
/// (実測: リーダブルコード・達人プログラマー・SQL アンチパターン等 10 冊すべて `cover` が空)。
/// ISBN から書誌を起こす定番の書籍は、これが無いと表紙の出ない一覧になる。
/// </summary>
public static class RakutenBooksResponseParser
{
    public static IReadOnlyList<RakutenBookInfo> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("Items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return items.EnumerateArray()
            .Select(ParseItem)
            .OfType<RakutenBookInfo>()
            .ToList();
    }

    static RakutenBookInfo? ParseItem(JsonElement element)
    {
        // 楽天の API は要素を {"Item": {...}} で包む形と、包まない形の両方がある
        var item = element.TryGetProperty("Item", out var wrapped) ? wrapped : element;

        if (item.ValueKind != JsonValueKind.Object
            || GetString(item, "isbn") is not { Length: > 0 } isbn)
        {
            return null;
        }

        return new RakutenBookInfo(
            isbn,
            GetInt(item, "reviewCount") ?? 0,
            GetDouble(item, "reviewAverage"),
            // 一覧の書影は 48px 幅なので中サイズ(128px)で足りる。
            // 大・小は保険 —— 商品によって欠けている段があるため
            ParseUri(GetString(item, "mediumImageUrl")
                ?? GetString(item, "largeImageUrl")
                ?? GetString(item, "smallImageUrl")));
    }

    // http/https 以外(javascript: 等)は img src に出すと XSS になるため WebUrl で弾く
    static Uri? ParseUri(string? value) =>
        WebUrl.TryCreate(value, out var uri) ? uri : null;

    static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    static int? GetInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.Number => value.GetInt32(),
                // 数値が文字列で返ることがある
                JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
                _ => null,
            }
            : null;

    // reviewAverage は "4.5" のように文字列で返る。レビューが無いときは "0.0"
    static double? GetDouble(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var value))
        {
            return null;
        }

        var parsed = value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(
                value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => (double?)null,
        };

        return parsed is > 0 ? parsed : null;
    }
}

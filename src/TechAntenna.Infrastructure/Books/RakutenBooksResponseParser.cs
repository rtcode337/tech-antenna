using System.Globalization;
using System.Text.Json;

namespace TechAntenna.Infrastructure.Books;

/// <summary>楽天ブックスから取り出した1冊分のレビュー。</summary>
public record RakutenReview(string Isbn, int ReviewCount, double? ReviewAverage);

/// <summary>
/// 楽天ブックス書籍検索API のレスポンスを解析する。
///
/// 取り出すのは**レビュー件数と平均評価だけ**。書誌情報は Google Books と openBD で
/// 足りているうえ、楽天の商品情報(商品説明・価格)まで取り込むと、
/// 表示に別の条件が付いてくるため。
/// </summary>
public static class RakutenBooksResponseParser
{
    public static IReadOnlyList<RakutenReview> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("Items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return items.EnumerateArray()
            .Select(ParseItem)
            .OfType<RakutenReview>()
            .ToList();
    }

    static RakutenReview? ParseItem(JsonElement element)
    {
        // 楽天の API は要素を {"Item": {...}} で包む形と、包まない形の両方がある
        var item = element.TryGetProperty("Item", out var wrapped) ? wrapped : element;

        if (item.ValueKind != JsonValueKind.Object
            || GetString(item, "isbn") is not { Length: > 0 } isbn)
        {
            return null;
        }

        return new RakutenReview(isbn, GetInt(item, "reviewCount") ?? 0, GetDouble(item, "reviewAverage"));
    }

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

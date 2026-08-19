using System.Globalization;
using System.Xml.Linq;
using TechAntenna.Core;

namespace TechAntenna.Infrastructure.Books;

/// <summary>NDL サーチから取り出した 1 冊分。</summary>
public record NdlSearchEntry(string Title, Uri Url, string? Publisher, DateOnly? PublishedOn);

/// <summary>
/// 国立国会図書館サーチ(NDL サーチ)の OpenSearch レスポンスを解析する。
///
/// 形は RSS 2.0 に Dublin Core と `dcndl:` を足したもの。標準の要素だけでは足りないので
/// (刊行年月は `dcterms:issued`、出版者は `dc:publisher`)、記事用の `FeedParser` とは別実装。
/// 取り込むのは<b>書誌事実だけ</b>(タイトル・リンク・出版者・刊行年月)——
/// `description` には読み仮名や責任表示が HTML で入っているが、使わない。
/// </summary>
public static class NdlSearchResponseParser
{
    static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    static readonly XNamespace Dcterms = "http://purl.org/dc/terms/";
    static readonly XNamespace OpenSearch = "http://a9.com/-/spec/opensearchrss/1.0/";

    /// <summary>その検索条件に何件あるか(ページングの打ち切りに使う)。無ければ 0。</summary>
    public static int TotalResults(string xml)
    {
        var root = XDocument.Parse(xml).Root;
        var value = root?.Element("channel")?.Element(OpenSearch + "totalResults")?.Value;

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var total)
            ? total
            : 0;
    }

    public static IReadOnlyList<NdlSearchEntry> Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root;
        var items = root?.Element("channel")?.Elements("item") ?? [];

        return items.Select(ParseItem).OfType<NdlSearchEntry>().ToList();
    }

    static NdlSearchEntry? ParseItem(XElement item)
    {
        var title = Value(item, "title") ?? Value(item, Dc + "title");
        // http/https 以外は画面の href に出せない(WebUrl で弾く)
        if (title is not { Length: > 0 } || !WebUrl.TryCreate(Value(item, "link"), out var url))
        {
            return null;
        }

        return new NdlSearchEntry(
            title,
            url,
            Value(item, Dc + "publisher"),
            // 刊行年月。`dcterms:issued` は "2026.7" や "2026.7.15"、`dc:date` は "2026" のことが多い
            ParseIssued(Value(item, Dcterms + "issued") ?? Value(item, Dc + "date")));
    }

    /// <summary>
    /// 刊行年月を日付にする。日が分からなければその月の 1 日にする ——
    /// 集計の窓(直近 N か月)を切れれば十分で、日の精度は要らない。
    /// 年しか分からないものは null(窓に入れると 1 月 1 日の本が大量に並ぶ)。
    /// </summary>
    public static DateOnly? ParseIssued(string? text)
    {
        if (text is not { Length: > 0 })
        {
            return null;
        }

        var parts = text.Split(['.', '-', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => new string(part.Where(char.IsAsciiDigit).ToArray()))
            .Where(part => part.Length > 0)
            .Select(part => int.Parse(part, CultureInfo.InvariantCulture))
            .ToList();

        if (parts.Count < 2 || parts[0] is < 1900 or > 2999 || parts[1] is < 1 or > 12)
        {
            return null;
        }

        var day = parts.Count > 2 && parts[2] is >= 1 and <= 31 ? parts[2] : 1;

        return new DateOnly(parts[0], parts[1], Math.Min(day, DateTime.DaysInMonth(parts[0], parts[1])));
    }

    static string? Value(XElement item, XName name) =>
        item.Element(name)?.Value is { Length: > 0 } value ? value : null;
}

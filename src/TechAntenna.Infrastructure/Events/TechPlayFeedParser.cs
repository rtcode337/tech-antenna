using System.Globalization;
using System.Xml.Linq;
using TechAntenna.Core;

namespace TechAntenna.Infrastructure.Events;

/// <summary>TECH PLAY の RSS から取り出した1イベント。カテゴリは未正規化のまま返す。</summary>
public record TechPlayEventEntry(
    string Title,
    Uri Url,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    string? Place,
    string? Address,
    IReadOnlyList<string> Categories);

/// <summary>
/// TECH PLAY のイベント RSS を解析する。
///
/// RSS 2.0 だが、開催日時・会場・住所は独自名前空間(<c>tp:</c>)の要素に入っていて
/// 標準の要素からは取れない。記事用の <c>FeedParser</c> と別実装にしているのはこのため。
/// </summary>
public static class TechPlayFeedParser
{
    /// <summary>TECH PLAY 独自要素の名前空間。</summary>
    static readonly XNamespace Tp = "https://rss.techplay.jp/";

    /// <summary>tp: の日時には時差の表記が無く、日本時間で書かれている。</summary>
    static readonly TimeSpan Jst = JapanTime.Offset;

    /// <summary>tp: の日時に現れる形式。時刻が無い <c>eventDate</c> も同じ経路で読む。</summary>
    static readonly string[] DateTimeFormats =
        ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd"];

    public static IReadOnlyList<TechPlayEventEntry> Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root
            ?? throw new FormatException("XML にルート要素がない。");

        if (root.Name.LocalName != "rss")
        {
            throw new FormatException($"未対応のフィード形式: <{root.Name.LocalName}>");
        }

        return (root.Element("channel")?.Elements("item") ?? [])
            .Select(ParseItem)
            .OfType<TechPlayEventEntry>()
            .ToList();
    }

    static TechPlayEventEntry? ParseItem(XElement item)
    {
        // 辿れないもの・開催日時が読めないものは取り込まない(http/https 以外も WebUrl が弾く)
        if (!WebUrl.TryCreate((string?)item.Element("link"), out var url))
        {
            return null;
        }

        // eventStartTime が欠けていても eventDate があれば当日 0 時として扱う
        var startsAt = ParseJst((string?)item.Element(Tp + "eventStartTime"))
            ?? ParseJst((string?)item.Element(Tp + "eventDate"));
        if (startsAt is not { } start)
        {
            return null;
        }

        return new TechPlayEventEntry(
            ((string?)item.Element("title") ?? "").Trim(),
            url,
            start,
            ParseJst((string?)item.Element(Tp + "eventEndTime")),
            Trimmed(item.Element(Tp + "eventPlace")),
            Trimmed(item.Element(Tp + "eventAddress")),
            item.Elements("category")
                .Select(c => c.Value.Trim())
                .Where(c => c.Length > 0)
                .ToList());
    }

    static string? Trimmed(XElement? element) =>
        element?.Value.Trim() is { Length: > 0 } value ? value : null;

    /// <summary>日本時間として読み、保存側に合わせて UTC に正規化する。</summary>
    static DateTimeOffset? ParseJst(string? text) =>
        DateTime.TryParseExact(
            (text ?? "").Trim(), DateTimeFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? new DateTimeOffset(parsed, Jst).ToUniversalTime()
            : null;
}

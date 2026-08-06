using System.Globalization;
using System.Xml.Linq;
using TechAntenna.Core;

namespace TechAntenna.Infrastructure.Feeds;

/// <summary>フィードから取り出した1エントリ。タグは未正規化のまま返す。</summary>
public record FeedEntry(
    string Title,
    Uri Url,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<string> Tags,
    string? Summary);

/// <summary>
/// RSS 2.0 / RSS 1.0 (RDF) / Atom のフィードを解析する。
/// 主要な収集元に対応するには3形式が必要(Zenn: RSS 2.0、Qiita: Atom、
/// はてなブックマーク: RSS 1.0)。
/// </summary>
public static class FeedParser
{
    static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    static readonly XNamespace Rss10 = "http://purl.org/rss/1.0/";
    static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";

    public static IReadOnlyList<FeedEntry> Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root
            ?? throw new FormatException("XML にルート要素がない。");

        return root.Name.LocalName switch
        {
            "feed" => ParseAtom(root),
            "rss" => ParseRss20(root),
            "RDF" => ParseRss10(root),
            _ => throw new FormatException($"未対応のフィード形式: <{root.Name.LocalName}>"),
        };
    }

    static List<FeedEntry> ParseAtom(XElement root) =>
        root.Elements(Atom + "entry")
            .Select(entry => new FeedEntry(
                (string?)entry.Element(Atom + "title") ?? "",
                RequireUrl((string?)entry.Elements(Atom + "link")
                    .FirstOrDefault(l => (string?)l.Attribute("rel") is null or "alternate")
                    ?.Attribute("href")),
                ParseDate((string?)entry.Element(Atom + "published")
                    ?? (string?)entry.Element(Atom + "updated")),
                entry.Elements(Atom + "category")
                    .Select(c => (string?)c.Attribute("term") ?? "")
                    .ToList(),
                HtmlText.Strip((string?)entry.Element(Atom + "content")
                    ?? (string?)entry.Element(Atom + "summary"))))
            .ToList();

    static List<FeedEntry> ParseRss20(XElement root) =>
        (root.Element("channel")?.Elements("item") ?? [])
            .Select(item => new FeedEntry(
                (string?)item.Element("title") ?? "",
                RequireUrl((string?)item.Element("link")),
                ParseDate((string?)item.Element("pubDate")
                    ?? (string?)item.Element(Dc + "date")),
                item.Elements("category").Select(c => c.Value).ToList(),
                HtmlText.Strip((string?)item.Element("description"))))
            .ToList();

    static List<FeedEntry> ParseRss10(XElement root) =>
        root.Elements(Rss10 + "item")
            .Select(item => new FeedEntry(
                (string?)item.Element(Rss10 + "title") ?? "",
                RequireUrl((string?)item.Element(Rss10 + "link")),
                ParseDate((string?)item.Element(Dc + "date")),
                item.Elements(Dc + "subject").Select(s => s.Value).ToList(),
                HtmlText.Strip((string?)item.Element(Rss10 + "description"))))
            .ToList();

    // http/https 以外(javascript: 等)は href に出すと XSS になるため WebUrl で弾く
    static Uri RequireUrl(string? value) => WebUrl.Require(value);

    // pubDate は RFC 822 形式("Tue, 29 Jul 2026 12:34:56 +0900")、
    // Atom / dc:date は ISO 8601 形式。どちらも DateTimeOffset が解釈できる。
    // 解釈できた値は UTC に正規化する —— Npgsql は timestamptz へ UTC 以外の
    // オフセットを書けず(Qiita の pubDate は +09:00 なので保存時に例外になる)、
    // DB 側は元のオフセットを保持しないため、ここで揃えても情報は落ちない
    static DateTimeOffset? ParseDate(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
}

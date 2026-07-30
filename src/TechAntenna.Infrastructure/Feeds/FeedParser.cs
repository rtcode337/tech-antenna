using System.Globalization;
using System.Xml.Linq;

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
/// はてなブックマーク: RSS 1.0)。学習目的のため既成のパーサーは使わず
/// XLinq で自前実装している。
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

    static Uri RequireUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url)
            ? url
            : throw new FormatException($"エントリの link が絶対 URL でない: '{value}'");

    // pubDate は RFC 822 形式("Tue, 29 Jul 2026 12:34:56 +0900")、
    // Atom / dc:date は ISO 8601 形式。どちらも DateTimeOffset が解釈できる
    static DateTimeOffset? ParseDate(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}

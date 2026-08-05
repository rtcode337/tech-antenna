using System.Globalization;
using System.Xml.Linq;

namespace TechAntenna.Infrastructure.Feeds;

/// <summary>J-STAGE から取り出した論文1件。</summary>
public record JstageArticle(string Title, Uri Url, DateTimeOffset? PublishedAt, string? JournalTitle);

/// <summary>
/// J-STAGE 検索 API(service=3)の応答を解析する。
///
/// **Atom だが記事用の <see cref="FeedParser"/> では読めない。** entry の中身が独自要素
/// (`article_title` / `article_link` / `pubyear`)で、標準の `title`・`link` を持たないため
/// (TECH PLAY を別実装にしているのと同じ事情)。
///
/// 和文と英文の両方が入っているので**和題を優先**する(無ければ英題)。
/// </summary>
public static class JstageResponseParser
{
    static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    public static IReadOnlyList<JstageArticle> Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root
            ?? throw new FormatException("XML にルート要素がない。");

        return root.Elements(Atom + "entry")
            .Select(Convert)
            .OfType<JstageArticle>()
            .ToList();
    }

    static JstageArticle? Convert(XElement entry)
    {
        var title = Localized(entry.Element(Atom + "article_title"));
        var link = Localized(entry.Element(Atom + "article_link"));

        if (title is not { Length: > 0 }
            || !Uri.TryCreate(link, UriKind.Absolute, out var url))
        {
            return null;
        }

        return new JstageArticle(title, url, PublishedAt(entry), Localized(entry.Element(Atom + "material_title")));
    }

    /// <summary>和文を優先し、無ければ英文を返す。</summary>
    static string? Localized(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        var ja = ((string?)element.Element(Atom + "ja"))?.Trim();
        return ja is { Length: > 0 } ? ja : ((string?)element.Element(Atom + "en"))?.Trim();
    }

    /// <summary>
    /// 公開日時。`updated` は時差付きの ISO 8601 なのでそれを使い、
    /// 無ければ `pubyear` からその年の 1 月 1 日として扱う(年しか分からないため)。
    /// </summary>
    static DateTimeOffset? PublishedAt(XElement entry)
    {
        if (DateTimeOffset.TryParse((string?)entry.Element(Atom + "updated"),
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var updated))
        {
            return updated.ToUniversalTime();
        }

        return int.TryParse((string?)entry.Element(Atom + "pubyear"), out var year)
            ? new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero)
            : null;
    }
}

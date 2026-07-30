using System.Net;
using System.Text.RegularExpressions;

namespace TechAntenna.Infrastructure.Feeds;

/// <summary>フィードの description 等に含まれる HTML をプレーンテキストに変換する。</summary>
public static partial class HtmlText
{
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    /// <summary>タグ除去・実体参照の復元・空白の圧縮を行い、最大 <paramref name="maxLength"/> 文字に切り詰める。</summary>
    public static string? Strip(string? html, int maxLength = 2000)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = TagPattern().Replace(html, " ");
        text = WebUtility.HtmlDecode(text);
        text = WhitespacePattern().Replace(text, " ").Trim();

        if (text.Length == 0)
        {
            return null;
        }

        return text.Length <= maxLength ? text : text[..maxLength];
    }
}

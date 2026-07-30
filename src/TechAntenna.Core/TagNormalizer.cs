namespace TechAntenna.Core;

/// <summary>
/// 収集元ごとに異なるタグの表記(大文字小文字・前後の空白・重複)をそろえる。
/// 記事・イベント・書籍を横断してタグで突き合わせるため、保存前に必ずここを通す。
/// </summary>
public static class TagNormalizer
{
    public static IReadOnlyList<string> Normalize(IEnumerable<string> tags) =>
        tags.Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}

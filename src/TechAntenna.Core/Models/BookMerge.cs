namespace TechAntenna.Core.Models;

/// <summary>
/// 同じ本を指す複数件を1つにまとめる。
///
/// 書籍は**トピックごとに検索する**ので、1冊が複数のトピックで見つかる(`AI` でも `LLM` でも
/// 当たる本がある)。重複を捨てるだけだと最初に見つかったトピックのタグしか付かず、
/// その本は他のトピックの一覧に出てこない。**捨てる側のタグを残す側へ足す**のがここの仕事。
/// </summary>
public static class BookMerge
{
    /// <summary>
    /// <paramref name="incoming"/> のタグを <paramref name="stored"/> へ足す。足すものがあれば true。
    /// **書誌情報そのものは上書きしない** —— 既に保存してある値を、後から来た検索結果で
    /// 壊さないため。
    /// </summary>
    public static bool MergeTags(Book stored, Book incoming)
    {
        var tags = Union(stored.Tags, incoming.Tags);
        var rawTags = Union(stored.RawTags, incoming.RawTags);

        // 和集合なので、件数が変わらない = 増えていない
        if (tags.Count == stored.Tags.Count && rawTags.Count == stored.RawTags.Count)
        {
            return false;
        }

        stored.Tags = tags;
        stored.RawTags = rawTags;
        return true;
    }

    /// <summary>
    /// 同じキー(<see cref="BookKey"/>)の本をまとめて、1キーにつき1件にする。
    /// 保存する前に呼ぶ —— 1回の保存に同じ本が複数入っていても取りこぼさないため。
    /// </summary>
    public static IReadOnlyList<Book> Coalesce(IEnumerable<Book> books)
    {
        var byKey = new Dictionary<string, Book>(StringComparer.Ordinal);

        foreach (var book in books)
        {
            var key = BookKey.For(book);
            if (byKey.TryGetValue(key, out var stored))
            {
                MergeTags(stored, book);
                continue;
            }

            byKey[key] = book;
        }

        return byKey.Values.ToList();
    }

    static IReadOnlyList<string> Union(IReadOnlyList<string> stored, IReadOnlyList<string> incoming) =>
        stored.Concat(incoming).Distinct(StringComparer.Ordinal).ToList();
}

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
    /// <paramref name="incoming"/> の内容を <paramref name="stored"/> へ取り込む。変化があれば true。
    ///
    /// **書誌情報そのものは上書きしない**(既に保存してある値を後から来た検索結果で壊さないため)。
    /// 例外は**レビュー**で、これは時間とともに増える数値なので新しい値で上書きする。
    /// **欠けている書影は埋める**(上書きはしない)—— 補完で取れた値をここで捨てると、
    /// 既に保存済みの本には表紙が永久に付かない。
    /// </summary>
    public static bool Merge(Book stored, Book incoming)
    {
        var changed = MergeTags(stored, incoming);

        // レビューは「今どれだけ読まれているか」なので、取れたら最新の値に差し替える。
        // 取れなかった(null)ときに上書きすると、取得元が一時的に落ちただけで指標が消える
        if (incoming.ReviewCount is { } count && count != stored.ReviewCount)
        {
            stored.ReviewCount = count;
            stored.ReviewAverage = incoming.ReviewAverage;
            changed = true;
        }

        // **書影は「欠けているときだけ」埋める。** 上書きはしない(書誌情報と同じ扱い)が、
        // 埋めないと**取り直した書影が保存の合流で捨てられる** —— 書影の補完を足す前に
        // 保存した本は書影が null のまま残っているので、収集のたびに Google Books へ
        // 問い合わせては捨てることになり、画面には表紙が出ないままになる
        if (stored.CoverUrl is null && incoming.CoverUrl is { } cover)
        {
            stored.CoverUrl = cover;
            changed = true;
        }

        // 推薦は積み上がる情報なので和集合。同じ記事を二重に数えないよう URL で重複を落とす
        var recommendedBy = Union(stored.RecommendedBy, incoming.RecommendedBy);
        if (recommendedBy.Count != stored.RecommendedBy.Count)
        {
            stored.RecommendedBy = recommendedBy;
            changed = true;
        }

        return changed;
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
                Merge(stored, book);
                continue;
            }

            byKey[key] = book;
        }

        return byKey.Values.ToList();
    }

    static bool MergeTags(Book stored, Book incoming)
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

    static IReadOnlyList<string> Union(IReadOnlyList<string> stored, IReadOnlyList<string> incoming) =>
        stored.Concat(incoming).Distinct(StringComparer.Ordinal).ToList();
}

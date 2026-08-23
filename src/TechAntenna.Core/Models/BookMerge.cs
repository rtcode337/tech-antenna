using TechAntenna.Core.Abstractions;

namespace TechAntenna.Core.Models;

/// <summary>
/// 同じ本を指す複数件を1つにまとめる。
///
/// 書籍はトピックごとに検索するので、1冊が複数のトピックで見つかる(`AI` でも `LLM` でも
/// 当たる本がある)。重複を捨てるだけだと最初に見つかったトピックのタグしか付かず、
/// その本は他のトピックの一覧に出てこない。捨てる側のタグを残す側へ足すのがここの仕事。
/// </summary>
public static class BookMerge
{
    /// <summary>
    /// <paramref name="incoming"/> の内容を <paramref name="stored"/> へ取り込む。変化があれば true。
    ///
    /// 書誌情報そのものは上書きしない(既に保存してある値を後から来た検索結果で壊さないため)。
    /// 例外はレビューで、これは時間とともに増える数値なので新しい値で上書きする。
    /// 欠けている書影は埋める(上書きはしない)—— 補完で取れた値をここで捨てると、
    /// 既に保存済みの本には表紙が永久に付かない。
    ///
    /// 「読んだ」の印(<see cref="Book.ReadAt"/>)はここで一切触らない。収集元から
    /// 来る本の <c>ReadAt</c> は常に null なので、写すと再収集のたびに印が消える ——
    /// あれは外から取れる情報ではなく本人の記録で、<see cref="Abstractions.IBookStore.SetReadAsync"/>
    /// だけが書き換える。
    /// </summary>
    public static bool Merge(Book stored, Book incoming)
    {
        var changed = MergeTags(stored, incoming);

        // 書影は「欠けているときだけ」埋める。上書きはしない(書誌情報と同じ扱い)が、
        // 埋めないと取り直した書影が保存の合流で捨てられる —— 書影の補完を足す前に
        // 保存した本は書影が null のまま残っているので、収集のたびに Google Books へ
        // 問い合わせては捨てることになり、画面には表紙が出ないままになる
        if (stored.CoverUrl is null && incoming.CoverUrl is { } cover)
        {
            stored.CoverUrl = cover;
            changed = true;
        }

        // 推薦は積み上がる情報なので和集合。同じ記事を二重に数えないよう URL で重複を落とす
        var recommendedBy = UnionSources(stored.RecommendedBy, incoming.RecommendedBy);
        // 件数だけでは足りない。題名を後から得た(同じ URL で Title が埋まった)ときは
        // 件数が変わらないので、中身を見て入れ替える —— でないと画面はいつまでも URL のまま
        if (!recommendedBy.SequenceEqual(stored.RecommendedBy))
        {
            stored.RecommendedBy = recommendedBy;
            changed = true;
        }

        // 引用も同じく積み上がる。トピックごとに記事を読むので、同じ本が別のトピックの記事でも
        // 引用される —— 上書きにすると最後に回したトピックの分しか残らない
        var citedBy = UnionSources(stored.CitedBy, incoming.CitedBy);
        if (!citedBy.SequenceEqual(stored.CitedBy))
        {
            stored.CitedBy = citedBy;
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

    /// <summary>
    /// 出典を積み上げる。同一性は URL で見て、題名を持っているほうを残す ——
    /// 題名は後から埋まることがある(この列より前に集めた分は null で入っている)ので、
    /// 単純な Distinct だと先に入った題名なしの行が居座る。
    /// </summary>
    static IReadOnlyList<SourceArticle> UnionSources(
        IReadOnlyList<SourceArticle> stored, IReadOnlyList<SourceArticle> incoming)
    {
        var byUrl = new Dictionary<string, SourceArticle>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var article in stored.Concat(incoming))
        {
            if (!byUrl.TryGetValue(article.Url, out var kept))
            {
                byUrl[article.Url] = article;
                order.Add(article.Url);
                continue;
            }

            if (string.IsNullOrWhiteSpace(kept.Title) && !string.IsNullOrWhiteSpace(article.Title))
            {
                byUrl[article.Url] = article;
            }
        }

        return order.Select(url => byUrl[url]).ToList();
    }

    static IReadOnlyList<string> Union(IReadOnlyList<string> stored, IReadOnlyList<string> incoming) =>
        stored.Concat(incoming).Distinct(StringComparer.Ordinal).ToList();
}

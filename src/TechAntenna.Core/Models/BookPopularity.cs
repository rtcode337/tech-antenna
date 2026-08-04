namespace TechAntenna.Core.Models;

/// <summary>
/// 「その分野で読んでおくべき本」の度合いを、レビュー件数と平均評価から出す。
///
/// **件数と評価のどちらか片方では順位にならない。** 件数だけだと、評価の低い話題書が
/// 定番書を押しのける。評価だけだと、レビュー 1 件で星 5 の本が最上位に来る。
/// そこで**評価をベイズ平均で件数に応じて割り引き**、それに**件数の対数**を掛ける。
/// 対数にするのは、レビュー 1000 件の本を 100 件の本の 10 倍には扱わないため
/// (桁が違えば差は付くが、上位が 1 冊で埋まらない)。
/// </summary>
public static class BookPopularity
{
    /// <summary>ベイズ平均の事前分布の重み(件数)。これ以下のレビュー数では評価をあまり信用しない。</summary>
    const double PriorCount = 5;

    /// <summary>ベイズ平均の事前分布の平均(5点満点の平均的な評価)。</summary>
    const double PriorAverage = 3.0;

    /// <summary>
    /// 読んでおくべき度。**レビュー情報が取れていない本は null**(0 ではない) ——
    /// 「読まれていない」と「分からない」を混ぜると、取得元を設定する前の本が
    /// まとめて最下位に沈むため。
    /// </summary>
    public static double? Score(Book book)
    {
        if (book.ReviewCount is not { } count)
        {
            return null;
        }

        if (count <= 0)
        {
            return 0;
        }

        var average = book.ReviewAverage ?? PriorAverage;
        var bayesian = ((count * average) + (PriorCount * PriorAverage)) / (count + PriorCount);

        return Math.Log10(1 + count) * bayesian;
    }

    /// <summary>読んでおくべき度の高い順に並べる。取れていない本(null)は後ろへ。</summary>
    public static IOrderedEnumerable<Book> ByPopularity(this IEnumerable<Book> books) =>
        books
            .OrderByDescending(book => Score(book) is not null)
            .ThenByDescending(book => Score(book) ?? 0)
            .ThenByDescending(book => book.CollectedAt);
}

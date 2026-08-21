using TechAntenna.Core.Abstractions;

namespace TechAntenna.Core.Models;

/// <summary>
/// 「その分野で読んでおくべき本」の度合いを、レビュー件数と平均評価から出す。
///
/// 件数と評価のどちらか片方では順位にならない。件数だけだと、評価の低い話題書が
/// 定番書を押しのける。評価だけだと、レビュー 1 件で星 5 の本が最上位に来る。
/// そこで評価をベイズ平均で件数に応じて割り引き、それに件数の対数を掛ける。
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
    /// 読んでおくべき度。レビュー情報が取れていない本は null(0 ではない) ——
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

    /// <summary>
    /// 読んだ本を後ろへ回す。並びの規則が何であれ最初に効かせる第一のキーで、
    /// これだけを掛ければ元の並びは<b>各グループの中でそのまま残る</b>
    /// (LINQ の <c>OrderBy</c> は安定なので、収集日時順で取ってきた一覧に
    /// 後から掛けても未読・既読それぞれの中の順序は変わらない)。
    ///
    /// 消さずに沈めるだけなのが要点 —— 読んだ本も「何を読んだか」の記録として
    /// 一覧に残っていてほしいし、間違えて付けた印をその場で戻せる必要がある。
    /// </summary>
    public static IOrderedEnumerable<Book> ReadLast(this IEnumerable<Book> books) =>
        books.OrderBy(book => book.IsRead);

    /// <summary>
    /// 記事に名指しされた票数。推薦(「読むべき技術書」のまとめ記事)と
    /// 引用(選んだトピックについて書かれた記事での言及)を<b>1票ずつ合算する</b>。
    ///
    /// 列は分けたまま合算するのが要点(<see cref="Book.RecommendedBy"/> /
    /// <see cref="Book.CitedBy"/>)—— 画面では別のバッジで出し、並べ替えのときだけ
    /// 1つの数にする。イベントの注目度が公式・参加者数・記事の言及数を別々に持ったまま
    /// 1つのスコアにするのと同じ扱いで、混ぜて保存すると「まとめ記事が薦めたのか、
    /// トピックの記事が触れたのか」を後から分けられなくなる。
    ///
    /// 重みを変えていないのは、どちらも「1本の記事がその本を名指しした」という
    /// 同じ形の根拠だから。まとめ記事のほうが強い(または弱い)と決める材料が今は無い。
    ///
    /// <b>数えるのは記事の URL で重複を落とした数。</b> 同じ記事が両方に入ることがある ——
    /// 「読むべき技術書100選」のようなまとめ記事は技術書のタグと個別分野のタグを両方持つので、
    /// 推薦の固定クエリにも引用のトピック検索にも当たる(実測: `tag:機械学習` の上位に
    /// 書籍紹介記事が並ぶ)。単純に足すと、その1本が2票になる。
    /// </summary>
    public static int Endorsements(Book book) =>
        book.RecommendedBy
            .Concat(book.CitedBy)
            .Select(article => article.Url)
            .Distinct(StringComparer.Ordinal)
            .Count();

    /// <summary>
    /// 読んでおくべき度の高い順に並べる。
    ///
    /// まず読んだ本を後ろへ回す(<see cref="ReadLast"/>)—— 読み終えた本がいつまでも
    /// 上位を占めると、この一覧の用途(次に読む本を選ぶ)を果たさない。
    /// そのうえで記事に名指しされた票数(<see cref="Endorsements"/>)を最優先にする ——
    /// レビュー数は「読まれた量」で一般向けの本ほど有利になるが、名指しは
    /// 「詳しい人が本文で挙げた」ぶん精度が高い。
    /// 同数ならレビューの指標で、それも取れていない本(null)は後ろへ。
    /// </summary>
    public static IOrderedEnumerable<Book> ByPopularity(this IEnumerable<Book> books) =>
        books
            .ReadLast()
            .ThenByDescending(Endorsements)
            .ThenByDescending(book => Score(book) is not null)
            .ThenByDescending(book => Score(book) ?? 0)
            .ThenByDescending(book => book.CollectedAt);
}

using TechAntenna.Core.Abstractions;

namespace TechAntenna.Core.Models;

/// <summary>
/// 「その分野で読んでおくべき本」の並べ替え。
///
/// <b>かつてはレビュー(件数・平均評価)も材料にしていた</b>が、取得元だった
/// 楽天ブックスの連携を外したときに一緒に落とした —— 他に日本語書籍のレビューを
/// 引ける無料の口が無く(Google Books の <c>ratingsCount</c> は日本語書籍にほぼ
/// 入っていない)、永久に null の列で並べ替えても順位に出ないため。
/// いま残っているのは<b>記事に名指しされた票数</b>で、これは
/// 「詳しい人が本文で挙げたか」を測る —— レビュー(読まれた量)より一般向けの本に
/// 偏りにくい。レビューを戻すなら、取得元と一緒に <see cref="ByPopularity"/> へ足す。
/// </summary>
public static class BookPopularity
{
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
    /// そのうえで記事に名指しされた票数(<see cref="Endorsements"/>)で並べ、
    /// 同数なら新しく集めたものを先に出す。
    /// </summary>
    public static IOrderedEnumerable<Book> ByPopularity(this IEnumerable<Book> books) =>
        books
            .ReadLast()
            .ThenByDescending(Endorsements)
            .ThenByDescending(book => book.CollectedAt);
}

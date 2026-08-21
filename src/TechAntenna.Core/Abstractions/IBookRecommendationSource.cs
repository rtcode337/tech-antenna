namespace TechAntenna.Core.Abstractions;

/// <summary>
/// この本の出どころになった記事1本。推薦(まとめ記事の名指し)と引用
/// (トピックの記事での言及)で同じ形を使う —— 持つものが「記事の URL と題名」で同じなので、
/// record を2つに割ると、出典を積み上げる規則も画面の出し方も二重に書くことになる。
///
/// 題名も持つ。番号(1・2・3)だけを並べていた頃は、押す前に「どこで薦められているのか」が
/// 分からなかった —— どこで薦められたかは本を選ぶ材料そのもの。題名は<b>収集元の応答に
/// 既に入っている</b>ので、これを持つために追加のリクエストは要らない。
/// 本文は今も保存しない(複製にしないため。取り込むのは題名と URL だけ)。
/// </summary>
/// <param name="Url">記事の URL。出典の同一性はこれで見る(同じ記事は1票)。</param>
/// <param name="Title">記事の題名。取れなかった収集元・この列より前に集めた分は null。</param>
public record SourceArticle(string Url, string? Title = null);

/// <summary>記事で薦められていた本1冊分。</summary>
/// <param name="Isbn13">本を特定するキー。</param>
/// <param name="Articles">薦めていた記事(出典)。同じ本を薦めた記事の数が「推薦回数」。</param>
public record BookRecommendation(string Isbn13, IReadOnlyList<SourceArticle> Articles);

/// <summary>
/// 「この本を読むべき」と書かれた記事から、薦められている本を拾う。
///
/// レビュー数(どれだけ読まれたか)とは別軸の指標で、こちらは「詳しい人が薦めたか」。
/// 取り込むのは抽出した ISBN と出典記事の URL・題名だけで、記事の本文は保存しない
/// (書籍で書誌事実だけを取り込むのと同じ方針)。
///
/// <b>検索語を持たない</b>のが <see cref="IBookCitationSource"/> との違い ——
/// あちらは選んだトピックを検索語にする興味トピックの軸で、こちらは固定クエリで
/// 定評を掘る定番の軸(トピックの選択に依存しない)。
/// </summary>
public interface IBookRecommendationSource
{
    /// <summary>収集元の名前(ログに出す)。</summary>
    string Name { get; }

    Task<IReadOnlyList<BookRecommendation>> FetchAsync(CancellationToken cancellationToken = default);
}

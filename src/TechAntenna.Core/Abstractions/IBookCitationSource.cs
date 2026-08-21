namespace TechAntenna.Core.Abstractions;

/// <summary>トピックの記事で引用されていた本1冊分。</summary>
/// <param name="Isbn13">本を特定するキー。</param>
/// <param name="Articles">その本に触れていた記事(出典)。記事の数が「引用回数」。</param>
public record BookCitation(string Isbn13, IReadOnlyList<SourceArticle> Articles);

/// <summary>
/// 選んだトピックの記事から、そこで推薦・引用されている本を拾う。
///
/// 推薦(<see cref="IBookRecommendationSource"/>)とは母集団が違う。あちらは
/// 「読むべき技術書」を挙げた<b>まとめ記事</b>を固定クエリで掘る定番の軸で、こちらは
/// <b>そのトピックについて書かれた普通の記事</b>が本を名指ししているかを見る興味トピックの軸
/// —— だから検索語(トピック)を受け取るし、集まる本もトピックの選択で変わる。
///
/// 材料は<b>収集済みの記事からは取れない</b>。<c>Article.ContentSnippet</c> は HTML を
/// 除去した抜粋なのでリンクが残っておらず、フィード自体も本文を全部は配信しない
/// (実測: Qiita の feed は 30 件で 27KB、Zenn は 18 件で 41KB。どちらも抜粋で
/// Amazon リンクは 0 件)。本文まで返す検索 API を持つ収集元だけがここを実装できる。
///
/// 取り込むのは抽出した ISBN と出典記事の URL・題名だけで、記事の本文は保存しない。
/// </summary>
public interface IBookCitationSource
{
    /// <summary>収集元の名前(ログに出す)。</summary>
    string Name { get; }

    /// <summary>トピック1つ分の記事を読んで、そこで引用されている本を返す。</summary>
    /// <param name="topic">検索語にするトピックの正式表記(例: 生成AI)。</param>
    Task<IReadOnlyList<BookCitation>> FetchAsync(
        string topic, CancellationToken cancellationToken = default);
}

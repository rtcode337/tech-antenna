namespace TechAntenna.Core.Abstractions;

/// <summary>記事で薦められていた本1冊分。</summary>
/// <param name="Isbn13">本を特定するキー。</param>
/// <param name="ArticleUrls">薦めていた記事の URL(出典)。同じ本を薦めた記事の数が「推薦回数」。</param>
public record BookRecommendation(string Isbn13, IReadOnlyList<string> ArticleUrls);

/// <summary>
/// 「この本を読むべき」と書かれた記事から、薦められている本を拾う。
///
/// **レビュー数(どれだけ読まれたか)とは別軸の指標**で、こちらは「詳しい人が薦めたか」。
/// 取り込むのは**抽出した ISBN と出典記事の URL だけ**で、記事の本文は保存しない
/// (書籍で書誌事実だけを取り込むのと同じ方針)。
/// </summary>
public interface IBookRecommendationSource
{
    /// <summary>収集元の名前(ログに出す)。</summary>
    string Name { get; }

    Task<IReadOnlyList<BookRecommendation>> FetchAsync(CancellationToken cancellationToken = default);
}

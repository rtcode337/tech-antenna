using TechAntenna.Core.Abstractions;

namespace TechAntenna.Infrastructure.Books;

/// <summary>
/// 選んだトピックについて書かれた Qiita の記事から、そこで推薦・引用されている本を拾う。
///
/// 経路は推薦本(<see cref="QiitaBookRecommendationSource"/>)と同じだが、<b>母集団が違う</b> ——
/// あちらは「読むべき技術書」を挙げたまとめ記事を固定クエリで掘るのに対し、こちらは
/// トピックを検索語にして、そのトピックの記事が本文で本を名指ししているかを見る。
/// 「その分野の記事が引き合いに出す本」なので、興味トピックの軸に置いてある。
///
/// クエリは <paramref name="queryTemplates"/> の <c>{topic}</c> をトピックの正式表記
/// (`生成ai` ではなく `生成AI`)で置き換えて組む。既定はタグ検索 +
/// ストック数の下限 —— 誰にも読まれていない記事の名指しまで数えると指標が薄まる。
///
/// <b>トピックによって濃さがまるで違う。</b> 実測では `tag:機械学習 stocks:&gt;50` の 50 記事中
/// 9 記事が本のリンクを含んでいた(異なる ASIN 33 個)のに対し、`tag:LLM stocks:&gt;50` は 1 記事
/// —— 教科書のある古い分野ほど厚く、新しい分野では数件しか取れない。0 件でも異常ではない。
/// </summary>
public class QiitaBookCitationSource(
    QiitaSearch search,
    IReadOnlyList<string> queryTemplates,
    int maxArticlesPerQuery = 100) : IBookCitationSource
{
    /// <summary>クエリの雛形でトピックに置き換わる場所。</summary>
    public const string TopicPlaceholder = "{topic}";

    public string Name => "Qiita(トピックの記事)";

    public async Task<IReadOnlyList<BookCitation>> FetchAsync(
        string topic, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return [];
        }

        var queries = queryTemplates
            .Where(template => !string.IsNullOrWhiteSpace(template))
            .Select(template => template.Replace(TopicPlaceholder, topic, StringComparison.Ordinal))
            .ToList();

        var articles = await search.SearchAsync(queries, maxArticlesPerQuery, cancellationToken);

        return AmazonBookLinks.ByIsbn(articles)
            .Select(found => new BookCitation(found.Isbn13, found.Articles))
            .ToList();
    }
}

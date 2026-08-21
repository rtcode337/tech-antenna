using TechAntenna.Core.Abstractions;

namespace TechAntenna.Infrastructure.Books;

/// <summary>
/// Qiita の「おすすめ技術書まとめ」系の記事から、薦められている本を拾う。
///
/// 公式 API(v2)を使う(<see cref="QiitaSearch"/>)。レンダリング済み HTML を掻き集めるより
/// 壊れにくく、検索が本文まで返すので記事ごとに引き直さなくてよい。
///
/// クエリは<b>固定</b>で、トピックの選択に依存しない —— これが定番の軸の意味そのもの。
/// 選んだトピックの記事から拾うのは <see cref="QiitaBookCitationSource"/> の仕事。
/// ストック数の下限で絞るのが肝で、誰も読んでいない記事の推薦まで数えると指標が薄まる。
/// タグ検索だけだとタグ無しの「読むべき本」系記事を取りこぼすので、本文検索のクエリも混ぜる。
///
/// 保存するのは ISBN と出典記事の URL・題名だけで、記事本文は保存しない。
/// </summary>
public class QiitaBookRecommendationSource(
    QiitaSearch search,
    IReadOnlyList<string> queries,
    int maxArticlesPerQuery = 200) : IBookRecommendationSource
{
    public string Name => "Qiita";

    public async Task<IReadOnlyList<BookRecommendation>> FetchAsync(
        CancellationToken cancellationToken = default)
    {
        var articles = await search.SearchAsync(queries, maxArticlesPerQuery, cancellationToken);

        return AmazonBookLinks.ByIsbn(articles)
            .Select(found => new BookRecommendation(found.Isbn13, found.Articles))
            .ToList();
    }
}

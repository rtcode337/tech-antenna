using System.Text.RegularExpressions;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Books;

/// <summary>
/// 記事の本文に貼られた Amazon の商品リンクから本を拾う。
///
/// 本の特定は ASIN から。書籍の ASIN は ISBN-10 そのものなので ISBN-13 に直せる
/// (<see cref="Isbn.FromAsin"/> がチェックディジットまで検算するので、`B0…` で始まる
/// Kindle 専売や電子機器は落ちる)。この検算がノイズを絞るので、本の話でない記事まで
/// 当たる検索語を混ぜても、関係ない記事は自然に落ちる。
///
/// 推薦(定番のまとめ記事)と引用(トピックの記事)の<b>どちらもここを通る</b> ——
/// 拾い方が同じなのに2か所に書くと、片方だけ直したときに票の数え方がずれる。
/// </summary>
public static partial class AmazonBookLinks
{
    /// <summary>
    /// Amazon の商品リンク。`/dp/ASIN`・`/gp/product/ASIN`・`/exec/obidos/ASIN/ASIN` の
    /// どの書き方でも拾えるようにしてある(記事によってまちまちなため)。
    /// </summary>
    [GeneratedRegex(@"amazon\.co\.jp/(?:[^/\s)""]+/)*(?:dp|gp/product|exec/obidos/ASIN)/([0-9A-Za-z]{10})")]
    private static partial Regex Link();

    /// <summary>本文に出てくる本の ISBN-13。同じ記事の中で同じ本が何度出てきても1つ。</summary>
    public static IEnumerable<string> IsbnsIn(string body) =>
        Link().Matches(body)
            .Select(match => Isbn.FromAsin(match.Groups[1].Value))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal);

    /// <summary>
    /// 記事の集まりを「本 → その本に触れていた記事」に組み替える。記事1本が1票で、
    /// 同じ記事が複数の検索語に当たっても、記事の URL で重複を落としてある前提
    /// (<see cref="QiitaSearch"/> がその形で返す)。
    /// </summary>
    public static IReadOnlyList<(string Isbn13, IReadOnlyList<SourceArticle> Articles)> ByIsbn(
        IEnumerable<QiitaArticle> articles)
    {
        var byIsbn = new Dictionary<string, List<SourceArticle>>(StringComparer.Ordinal);

        foreach (var article in articles)
        {
            foreach (var isbn in IsbnsIn(article.Body))
            {
                if (!byIsbn.TryGetValue(isbn, out var sources))
                {
                    sources = [];
                    byIsbn[isbn] = sources;
                }

                sources.Add(new SourceArticle(article.Url, article.Title));
            }
        }

        return byIsbn
            .Select(pair => (pair.Key, (IReadOnlyList<SourceArticle>)pair.Value))
            .ToList();
    }
}

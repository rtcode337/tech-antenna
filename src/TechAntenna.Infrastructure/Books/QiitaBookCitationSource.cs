using TechAntenna.Core;
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
/// クエリは <paramref name="queryTemplates"/> の差し込み口を置き換えて組む。口は2つあり、
/// <c>{topic}</c> がトピックの正式表記(`生成ai` ではなく `生成AI`)、
/// <c>{tag}</c> が<b>Qiita のタグ表記</b>(区切りを落とした小文字。`Claude Code` → `claudecode`)。
/// 既定はタグ検索 + ストック数の下限 —— 誰にも読まれていない記事の名指しまで数えると指標が薄まる。
///
/// <b><c>tag:</c> には正式表記を入れてはいけない。</b> Qiita のタグに空白は入らず、
/// 検索構文の空白は語の区切りなので、`tag:Claude Code` は「タグ Claude かつ本文に Code」と
/// 読まれる —— <b>0 件にはならないぶん気づけない</b>。実測では
/// `tag:Claude Code stocks:&gt;50` が 67 記事(本のリンクは 0 件)、
/// 正しい `tag:claudecode stocks:&gt;50` は 170 記事(本 4 冊)だった。
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
    /// <summary>クエリの雛形でトピックの正式表記に置き換わる場所。</summary>
    public const string TopicPlaceholder = "{topic}";

    /// <summary>クエリの雛形で Qiita のタグ表記に置き換わる場所。</summary>
    public const string TagPlaceholder = "{tag}";

    public string Name => "Qiita(トピックの記事)";

    public async Task<IReadOnlyList<BookCitation>> FetchAsync(
        string topic, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return [];
        }

        // タグ表記は突き合わせキーと同じ作り方(空白・`-`・`・` を落として小文字)。
        // 語彙の正式表記から機械的に作れるので、トピックごとにタグ名を持たなくてよい
        var tag = TagNormalizer.ToKey(topic);

        var queries = queryTemplates
            .Where(template => !string.IsNullOrWhiteSpace(template))
            // タグ表記が空になる語(記号だけの語)で `tag:` を組むと全件を引きに行くので落とす
            .Where(template => tag.Length > 0
                || !template.Contains(TagPlaceholder, StringComparison.Ordinal))
            .Select(template => template
                .Replace(TagPlaceholder, tag, StringComparison.Ordinal)
                .Replace(TopicPlaceholder, topic, StringComparison.Ordinal))
            .ToList();

        var articles = await search.SearchAsync(queries, maxArticlesPerQuery, cancellationToken);

        return AmazonBookLinks.ByIsbn(articles)
            .Select(found => new BookCitation(found.Isbn13, found.Articles))
            .ToList();
    }
}

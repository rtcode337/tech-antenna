using TechAntenna.Core.Models;

using TechAntenna.Core.Topics;

namespace TechAntenna.Core.Abstractions;

/// <summary>収集した記事の保存先。</summary>
public interface IArticleStore
{
    /// <summary>記事を追加する。URL が既存と重複するものは無視し、実際に追加した件数を返す。</summary>
    Task<int> AddRangeAsync(IEnumerable<Article> articles, CancellationToken cancellationToken = default);

    /// <summary>
    /// 公開日時(無ければ収集日時)の新しい順に最大 <paramref name="count"/> 件返す。
    /// <paramref name="kind"/> を渡すとその種別だけ。種別ごとに引くのは件数を分けて確保するため
    /// —— 混ぜて上位 N 件を取ると、更新の速いニュースが記事を押し出してしまう。
    /// </summary>
    Task<IReadOnlyList<Article>> GetRecentAsync(
        int count, ArticleKind? kind = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 要約が未生成の記事を、新しい順に最大 <paramref name="count"/> 件返す。
    /// 論文は要旨(<see cref="Article.ContentSnippet"/>)がある分だけ返す ——
    /// 材料が無い行にタイトルだけ渡しても LLM の枠を使うだけになる
    /// (arXiv のメタデータは CC0 なので要旨を取り込める。J-STAGE は取り込んでいない)。
    /// </summary>
    Task<IReadOnlyList<Article>> GetUnsummarizedAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>記事の要約を保存する。</summary>
    Task UpdateSummaryAsync(Guid articleId, string summary, CancellationToken cancellationToken = default);

    /// <summary>
    /// 訳題が未処理の論文を、新しい順に最大 <paramref name="count"/> 件返す。
    /// 対象は論文だけ —— 記事やニュースは日本語の収集元が中心で、訳す必要が薄い。
    /// </summary>
    Task<IReadOnlyList<Article>> GetUntranslatedPapersAsync(
        int count, CancellationToken cancellationToken = default);

    /// <summary>訳題を保存する。訳さないと決めたものは空文字で確定させる。</summary>
    Task UpdateTitleJaAsync(Guid articleId, string titleJa, CancellationToken cancellationToken = default);

    /// <summary>
    /// ブックマーク数をまとめて更新し、値が変わった件数を返す。
    /// 件数は時間とともに増えるので、取れたら常に新しい値で上書きする。
    /// </summary>
    Task<int> UpdateBookmarkCountsAsync(
        IReadOnlyList<(Guid ArticleId, int Count)> counts, CancellationToken cancellationToken = default);

    /// <summary>タグ <paramref name="tag"/> が付いたものを公開日時(無ければ収集日時)の新しい順に最大 <paramref name="count"/> 件返す。</summary>
    Task<IReadOnlyList<Article>> GetByTagAsync(string tag, int count, CancellationToken cancellationToken = default);

    /// <summary>タグごとの件数を返す。</summary>
    Task<IReadOnlyList<TagCount>> GetTagCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存済みの生タグ(<c>RawTags</c>)から <c>Tags</c> を作り直し、更新した件数を返す。
    /// 正規化の規則やストップワードを変えたときに、過去のデータを追従させるために使う。
    /// </summary>
    Task<int> RenormalizeTagsAsync(TopicCatalog catalog, CancellationToken cancellationToken = default);
}

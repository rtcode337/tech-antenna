using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>収集した記事の保存先。</summary>
public interface IArticleStore
{
    /// <summary>記事を追加する。URL が既存と重複するものは無視し、実際に追加した件数を返す。</summary>
    Task<int> AddRangeAsync(IEnumerable<Article> articles, CancellationToken cancellationToken = default);

    /// <summary>公開日時(無ければ収集日時)の新しい順に最大 <paramref name="count"/> 件返す。</summary>
    Task<IReadOnlyList<Article>> GetRecentAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>要約が未生成の記事を、新しい順に最大 <paramref name="count"/> 件返す。</summary>
    Task<IReadOnlyList<Article>> GetUnsummarizedAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>記事の要約を保存する。</summary>
    Task UpdateSummaryAsync(Guid articleId, string summary, CancellationToken cancellationToken = default);
}

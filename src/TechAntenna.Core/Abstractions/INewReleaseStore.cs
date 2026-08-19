using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>最近出た本(観測)の保存先。</summary>
public interface INewReleaseStore
{
    /// <summary>
    /// 追加し、実際に追加した件数を返す。重複判定は <see cref="NewRelease.Url"/>。
    /// 既にある行は<b>タグと書誌を上書きする</b> —— 同じ窓を毎回引き直す表なので、
    /// 正規化の規則やカタログを変えたら次の収集で揃ってほしい(記事・イベント・書籍の
    /// 「既存は上書きしない」とは方針が逆。読ませるための行ではなく観測だから)。
    /// </summary>
    Task<int> AddRangeAsync(
        IEnumerable<NewRelease> releases, CancellationToken cancellationToken = default);

    /// <summary>
    /// <paramref name="since"/> 以降に刊行されたものを、刊行日の新しい順に最大
    /// <paramref name="count"/> 件返す。刊行日が分からない行は含めない
    /// (窓を切れないものを混ぜると「最近のテーマ」でなくなる)。
    /// </summary>
    Task<IReadOnlyList<NewRelease>> GetPublishedSinceAsync(
        DateOnly since, int count, CancellationToken cancellationToken = default);
}

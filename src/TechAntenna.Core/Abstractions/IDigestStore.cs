using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>生成したダイジェスト(今日のサマリー)の保存先。</summary>
public interface IDigestStore
{
    /// <summary>ダイジェストを1件保存する。過去の分は消さない(生成履歴として残る)。</summary>
    Task SaveAsync(Digest digest, CancellationToken cancellationToken = default);

    /// <summary>最新の1件を返す。まだ無ければ null。ホームはこれだけを出す。</summary>
    Task<Digest?> GetLatestAsync(CancellationToken cancellationToken = default);
}

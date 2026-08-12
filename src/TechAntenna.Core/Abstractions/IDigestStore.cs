using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>生成したダイジェスト(今日のサマリー)の保存先。</summary>
public interface IDigestStore
{
    /// <summary>ダイジェストを1件保存する。過去の分は消さない(生成履歴として残る)。</summary>
    Task SaveAsync(Digest digest, CancellationToken cancellationToken = default);

    /// <summary>
    /// 守備範囲ごとの最新の1件を返す。まだ無ければ null。ホームはこれだけを出す
    /// (**範囲をまたいだ「最新の1件」は使わない** —— 全体と興味トピックは
    /// 生成される件数が違うので、混ぜて並べると片方が出てこなくなる)。
    /// </summary>
    Task<Digest?> GetLatestAsync(DigestScope scope, CancellationToken cancellationToken = default);
}

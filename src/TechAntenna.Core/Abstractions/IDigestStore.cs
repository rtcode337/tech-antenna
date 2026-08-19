using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>生成したダイジェスト(今日のサマリー)の保存先。</summary>
public interface IDigestStore
{
    /// <summary>ダイジェストを1件保存する。過去の分は消さない(生成履歴として残る)。</summary>
    Task SaveAsync(Digest digest, CancellationToken cancellationToken = default);

    /// <summary>
    /// 守備範囲ごとの最新の1件を返す。まだ無ければ null。ホームはこれだけを出す
    /// (範囲をまたいだ「最新の1件」は使わない —— 全体と興味トピックは
    /// 生成される件数が違うので、混ぜて並べると片方が出てこなくなる)。
    /// </summary>
    Task<Digest?> GetLatestAsync(DigestScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// 守備範囲ごとに最新の回で作ったものを全部返す(メインが先頭)。
    /// メイン以外の AI でも作っているとき、ホームはこれを並べて読み比べさせる。
    ///
    /// 回(<see cref="Digest.RunId"/>)で寄せる —— 生成時刻で寄せると、
    /// 今日失敗した AI の前日ぶんが今日のものと並んでしまう。
    /// </summary>
    Task<IReadOnlyList<Digest>> GetLatestRunAsync(
        DigestScope scope, CancellationToken cancellationToken = default);
}

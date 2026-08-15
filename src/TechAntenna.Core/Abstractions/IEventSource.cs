using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>イベントの収集元(connpass / Doorkeeper 等)。</summary>
public interface IEventSource
{
    string Name { get; }

    /// <summary>
    /// <b>トピックの選択が空でも集めるものがあるか。</b>
    ///
    /// イベントの収集は「選んだトピックを検索語にする」のが基本なので、選択が空なら
    /// 何も集まらない —— そのときは相手を叩かずに理由を出して終わるのが正しい。
    /// ただし<b>グループの購読</b>と<b>参加者数での面掃き</b>は検索語を使わないので、
    /// この2つを持つ収集元は選択が空でも走らせる必要がある。既定は false(検索でだけ集める)。
    /// </summary>
    bool WorksWithoutTopics => false;

    Task<IReadOnlyList<TechEvent>> FetchAsync(CancellationToken cancellationToken = default);
}

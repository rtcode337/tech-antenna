using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>
/// 最近出た本(新刊・ムック)を拾う収集元。
///
/// 検索語を取らないのが要点 —— これはトレンドの軸(外で何が起きているか)なので、
/// 収集対象に選んだトピックに依存させない。ジャンルと日付で引く。
/// </summary>
public interface INewReleaseSource
{
    /// <summary>収集元の名前(画面とログに出す)。</summary>
    string Name { get; }

    /// <summary><paramref name="since"/> 以降に刊行された本を拾う。</summary>
    Task<IReadOnlyList<NewRelease>> FetchAsync(
        DateOnly since, CancellationToken cancellationToken = default);
}

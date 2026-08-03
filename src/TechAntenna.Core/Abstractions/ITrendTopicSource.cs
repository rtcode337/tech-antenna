using TechAntenna.Core.Trends;

namespace TechAntenna.Core.Abstractions;

/// <summary>技術トレンドから話題のトピック候補を取得する。</summary>
public interface ITrendTopicSource
{
    string Name { get; }

    Task<IReadOnlyList<TrendTopicCandidate>> FetchAsync(CancellationToken cancellationToken = default);
}

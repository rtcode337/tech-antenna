using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>イベントの収集元(connpass / Doorkeeper 等)。</summary>
public interface IEventSource
{
    string Name { get; }

    Task<IReadOnlyList<TechEvent>> FetchAsync(CancellationToken cancellationToken = default);
}

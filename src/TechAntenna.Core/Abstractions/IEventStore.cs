using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>収集したイベントの保存先。</summary>
public interface IEventStore
{
    /// <summary>イベントを追加する。URL が既存と重複するものは無視し、実際に追加した件数を返す。</summary>
    Task<int> AddRangeAsync(IEnumerable<TechEvent> events, CancellationToken cancellationToken = default);

    /// <summary><paramref name="from"/> 以降に開催されるイベントを開始日時の早い順に最大 <paramref name="count"/> 件返す。</summary>
    Task<IReadOnlyList<TechEvent>> GetUpcomingAsync(DateTimeOffset from, int count, CancellationToken cancellationToken = default);
}

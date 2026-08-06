using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;

namespace TechAntenna.Infrastructure.Storage;

/// <summary>トピックの一言説明のメモリ上の実装。接続文字列なしのお試し起動とテストで使う。</summary>
public class InMemoryTopicDescriptionStore : ITopicDescriptionStore
{
    readonly object _gate = new();
    readonly Dictionary<string, TopicDescription> _byKey = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<TopicDescription>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<TopicDescription>>(_byKey.Values.ToList());
        }
    }

    public Task UpsertAsync(
        IReadOnlyList<TopicDescription> descriptions,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            foreach (var description in descriptions)
            {
                _byKey[description.Key] = description;
            }

            return Task.CompletedTask;
        }
    }
}

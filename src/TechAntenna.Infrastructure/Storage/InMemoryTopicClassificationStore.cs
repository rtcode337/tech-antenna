using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;

namespace TechAntenna.Infrastructure.Storage;

/// <summary>LLM によるタグ分類のメモリ上の実装。接続文字列なしのお試し起動とテストで使う。</summary>
public class InMemoryTopicClassificationStore : ITopicClassificationStore
{
    readonly object _gate = new();
    readonly Dictionary<string, TopicClassification> _byTag = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<TopicClassification>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<TopicClassification>>(_byTag.Values.ToList());
        }
    }

    public Task UpsertAsync(
        IReadOnlyList<TopicClassification> classifications,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            foreach (var classification in classifications)
            {
                _byTag[classification.Tag] = classification;
            }

            return Task.CompletedTask;
        }
    }
}

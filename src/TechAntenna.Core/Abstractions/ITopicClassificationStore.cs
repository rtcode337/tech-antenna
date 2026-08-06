using TechAntenna.Core.Topics;

namespace TechAntenna.Core.Abstractions;

/// <summary>LLM によるタグ分類の保存先。カタログ(JSON)と合成して使う。</summary>
public interface ITopicClassificationStore
{
    /// <summary>保存済みの分類を全件返す(起動時のカタログ合成と、分類済みタグの除外に使う)。</summary>
    Task<IReadOnlyList<TopicClassification>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>分類を追加・更新する(キーはタグ)。</summary>
    Task UpsertAsync(
        IReadOnlyList<TopicClassification> classifications,
        CancellationToken cancellationToken = default);
}

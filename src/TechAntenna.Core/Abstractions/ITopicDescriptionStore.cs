using TechAntenna.Core.Topics;

namespace TechAntenna.Core.Abstractions;

/// <summary>LLM が付けたトピックの一言説明の保存先。カタログ(JSON)と合成して使う。</summary>
public interface ITopicDescriptionStore
{
    /// <summary>保存済みの説明を全件返す(起動時のカタログ合成と、聞き直しの除外に使う)。</summary>
    Task<IReadOnlyList<TopicDescription>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>説明を追加・更新する(キーはトピックのキー)。</summary>
    Task UpsertAsync(
        IReadOnlyList<TopicDescription> descriptions,
        CancellationToken cancellationToken = default);
}

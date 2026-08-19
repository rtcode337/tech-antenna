namespace TechAntenna.Core.Topics;

/// <summary>
/// DB の語彙(<see cref="Topic"/>)と別名タグ(<see cref="Tag"/>)から、読み取り用の
/// カタログを組む。別名は「その語彙へ寄せると決めたタグ」そのもの ——
/// 別名の一覧をどこかに二重で持たず、タグの状態から導出する。
/// </summary>
public static class TopicCatalogBuilder
{
    public static IReadOnlyList<TopicCatalogEntry> Build(
        IReadOnlyList<Topic> topics, IReadOnlyList<Tag> tags)
    {
        var aliases = tags
            .Where(tag => tag.Status == TagStatus.Alias
                && tag.TopicKey is { Length: > 0 }
                && tag.TopicKey != tag.Key)
            .GroupBy(tag => tag.TopicKey!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(tag => tag.Key)
                    .OrderBy(key => key, StringComparer.Ordinal)
                    .ToList(),
                StringComparer.Ordinal);

        return topics
            .Select(topic => new TopicCatalogEntry(
                topic.Display,
                aliases.GetValueOrDefault(topic.Key, []),
                topic.Parent,
                topic.Description,
                topic.English))
            .ToList();
    }
}

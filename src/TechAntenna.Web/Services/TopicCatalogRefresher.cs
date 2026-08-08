using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;

namespace TechAntenna.Web.Services;

/// <summary>
/// DB から語彙のスナップショット(<see cref="TopicCatalog"/>)を組み直す。
///
/// **カタログは DI で収集元に配られたまま中身だけ差し替わる**ので、起動時と、
/// 語彙が変わったあと(再編成・画面からの手直し)に呼べば全体に効く。
/// </summary>
public class TopicCatalogRefresher(
    TopicCatalog catalog,
    ITopicStore topicStore,
    ITagStore tagStore)
{
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var topics = await topicStore.GetAllAsync(cancellationToken);
        var tags = await tagStore.GetAllAsync(cancellationToken);

        catalog.Replace(TopicCatalogBuilder.Build(topics, tags));
    }
}

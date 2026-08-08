using Microsoft.Extensions.Options;
using TechAntenna.Core.Abstractions;

namespace TechAntenna.Web.Services;

/// <summary>登録されたイベントソースを1巡し、ストアへ保存する。</summary>
public class EventCollectionRunner(
    IEnumerable<IEventSource> sources,
    IEventStore store,
    ITopicStore topicStore,
    IOptions<CollectionOptions> options,
    ILogger<EventCollectionRunner> logger) : JobRunner
{
    readonly IReadOnlyList<IEventSource> _sources = sources.ToList();

    public override string Name => "イベントの収集";

    public override bool IsConfigured => _sources.Count > 0;

    public Task<CollectionRunResult> RunOnceAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => CollectAsync(cancellationToken),
            CollectionRunResult.Nothing, cancellationToken);

    async Task<CollectionRunResult> CollectAsync(CancellationToken cancellationToken)
    {
        // 収集先へ同時アクセスしないよう、並列化せず1本ずつ間隔を空けて読む
        var delay = TimeSpan.FromSeconds(options.Value.DelayBetweenSourcesSeconds);
        // connpass と Doorkeeper は選択トピックを検索語として自分で引くが、TECH PLAY の RSS は
        // 検索できないため、ここで絞る。比べる相手はイベントの正規化済みタグなのでキーを使う
        var selectedTags = (await topicStore.GetSelectedAsync(cancellationToken))
            .Select(topic => topic.Key).ToList();
        if (selectedTags.Count == 0)
        {
            return CollectionRunResult.Nothing;
        }
        int fetched = 0, added = 0, failed = 0;

        for (var i = 0; i < _sources.Count; i++)
        {
            var source = _sources[i];
            try
            {
                var events = await source.FetchAsync(cancellationToken);
                events = events.Where(techEvent => techEvent.Tags.Intersect(selectedTags, StringComparer.Ordinal).Any()).ToList();
                var newlyAdded = await store.AddRangeAsync(events, cancellationToken);
                fetched += events.Count;
                added += newlyAdded;
                logger.LogInformation(
                    "{Source}: {Fetched} 件取得、うち {Added} 件を新規追加",
                    source.Name, events.Count, newlyAdded);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 1ソースの失敗で巡回全体を止めない
                failed++;
                logger.LogError(ex, "{Source} の収集に失敗", source.Name);
            }

            // 最後の収集元の後は待たない(手動実行で無駄に待たせないため)
            if (i < _sources.Count - 1 && delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        return new CollectionRunResult(fetched, added, failed);
    }
}

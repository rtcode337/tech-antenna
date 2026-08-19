using Microsoft.Extensions.Options;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Events;

namespace TechAntenna.Web.Services;

/// <summary>登録されたイベントソースを1巡し、ストアへ保存する。</summary>
public class EventCollectionRunner(
    IEnumerable<IEventSource> sources,
    SourceToggles toggles,
    IEventStore store,
    ITopicStore topicStore,
    TopicCatalog catalog,
    TagObserver tagObserver,
    EventMentionRefresher mentionRefresher,
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
        // 検索できないため、ここで絞る。比べる相手はイベントの正規化済みタグなのでキーを使う。
        // 絞りは配下込み —— 検索語を配下へ広げないのはリクエスト数の話で、巡回で
        // 流れてきた分まで捨てると、表示対象(選んだトピック+配下)のイベントが入らなくなる
        var selectedTags = catalog.ExpandWithDescendants(
            (await topicStore.GetSelectedAsync(cancellationToken)).Select(topic => topic.Key));

        // 選択が空でも、検索語を使わない経路(グループの購読・面掃き)があれば走らせる。
        // 逆にその経路を持たない収集元は、選択が空なら結果が全部捨てられると分かっているので
        // 叩きに行かない —— 集まらないと分かっている相手にリクエストを投げない
        // 止めた収集元は叩きに行かない。実行のたびに読むので、画面の切り替えは
        // 再起動なしで効く
        var enabled = toggles.Enabled(_sources, SourceToggles.Event, source => source.Name);
        if (enabled.Count == 0)
        {
            return CollectionRunResult.AllDisabled("イベント");
        }

        IReadOnlyList<IEventSource> usable = selectedTags.Count > 0
            ? enabled
            : enabled.Where(source => source.WorksWithoutTopics).ToList();
        if (usable.Count == 0)
        {
            // 何も集まらない理由を文言にする(論文・書籍と同じ扱い)
            return CollectionRunResult.NoTopics("イベント");
        }
        int fetched = 0, added = 0, failed = 0;

        for (var i = 0; i < usable.Count; i++)
        {
            var source = usable[i];
            try
            {
                var events = await source.FetchAsync(cancellationToken);
                events = events.Where(Wanted).ToList();
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
            if (i < usable.Count - 1 && delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        // 見つけたタグをタグの一覧へ反映する(状態は触らない)

        await tagObserver.ObserveAsync(cancellationToken: cancellationToken);

        // 注目度の3つめの材料(記事の言及数)をここで数え直す。外部は叩かず、
        // 集めてある記事と突き合わせるだけ。記事は後から増えるので、
        // 新しく取れたイベントだけでなく手元のイベント全体を数え直す
        var mentioned = await mentionRefresher.RefreshAsync(cancellationToken);
        if (mentioned > 0)
        {
            logger.LogInformation("記事の言及数を {Count} 件のイベントで更新", mentioned);
        }

        return new CollectionRunResult(fetched, added, failed);

        // 購読と面掃きで入ったものはトピックの絞りを通さない。検索語で見つけたのでは
        // ないので選んだトピックのタグを持っておらず、ここで落とすと経路ごと無効になる
        bool Wanted(TechEvent techEvent) =>
            techEvent.PickedBy is { Length: > 0 }
            || techEvent.Tags.Intersect(selectedTags, StringComparer.Ordinal).Any();
    }
}

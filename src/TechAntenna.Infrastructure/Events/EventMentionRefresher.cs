using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;

namespace TechAntenna.Infrastructure.Events;

/// <summary>
/// イベントが記事で何本言及されているかを数え直してストアへ保存する
/// (数え方は <see cref="EventMentions"/>)。
///
/// <b>外部を一切叩かない。</b> 材料は手元に集めてある記事とイベントだけなので、
/// レート制限も待ち時間も要らない —— このアプリが記事とイベントを同じ場所に
/// 集めているからこそ持てる注目度の指標で、イベント専門のサービスには真似しにくい。
///
/// 呼ぶのは<b>イベント収集の最後</b>。定期実行の並びでは記事の収集がイベントより先に走るので、
/// その日集めた記事まで数に入る(<c>ScheduledJobs.InOrder</c>)。
/// </summary>
public class EventMentionRefresher(
    IEventStore eventStore,
    IArticleStore articleStore,
    TopicCatalog catalog,
    TimeProvider timeProvider)
{
    /// <summary>
    /// 数え直す対象を、<b>過ぎたものも含めて</b>この日数ぶん遡って拾う ——
    /// 記事の多くは開催後(参加レポート)に書かれるので、終わったイベントを対象から
    /// 外すと「言及数が伸びるのはこれから」というものばかりになる。
    /// カレンダーで前の月を開いたときの並びにも効く。
    /// </summary>
    const int PastDays = 90;

    /// <summary>同上、先の側。年1回のカンファレンスは半年以上前に告知される。</summary>
    const int FutureDays = 400;

    /// <summary>1回に見るイベント数の上限(個人運用の規模を前提にした歯止め)。</summary>
    const int EventLimit = 2000;

    /// <summary>
    /// 突き合わせる記事の本数(新しい順)。<b>全件は見ない</b> ——
    /// イベント数 × 記事数の総当たりなので、青天井にすると収集の最後で待たされる。
    /// </summary>
    const int ArticleLimit = 2000;

    /// <summary>言及数を数え直し、値が変わった件数を返す。</summary>
    public async Task<int> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var events = await eventStore.GetInRangeAsync(
            now.AddDays(-PastDays), now.AddDays(FutureDays), EventLimit, cancellationToken);
        if (events.Count == 0)
        {
            return 0;
        }

        var articles = await articleStore.GetRecentAsync(ArticleLimit, cancellationToken: cancellationToken);
        if (articles.Count == 0)
        {
            return 0;
        }

        var updates = new List<(Guid, int)>();
        foreach (var techEvent in events)
        {
            // 照合語を作れないイベントは触らない(null のまま = 「測っていない」)。
            // 0 で埋めると「誰も書いていない」に見えるが、実際には測っていないだけ
            if (EventMentions.KeyFor(techEvent, catalog) is not { } key)
            {
                continue;
            }

            updates.Add((techEvent.Id, EventMentions.Count(key, articles)));
        }

        return await eventStore.UpdateMentionCountsAsync(updates, cancellationToken);
    }
}

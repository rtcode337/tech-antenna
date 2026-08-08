using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;

namespace TechAntenna.Web.Services;

/// <summary>
/// トピックを別のトピックへ寄せる(統合)。**画面からの手直しと LLM の統合パスで共有する** ——
/// どちらも「同義のトピックが 2 つある」を直す操作で、やることは同じ。
///
/// 寄せるときにやることが 3 つある。1 つでも漏らすと語彙が壊れる:
///
/// 1. **寄せ元を指していたタグを寄せ先へ付け替える**(件数が寄せ先へ合算されるように)
/// 2. **寄せ元の子を寄せ先の子にする**(親が消えて孤児になるのを防ぐ)
/// 3. **寄せ元のトピックの行を消し、そのタグを別名にする**
/// </summary>
public class TopicMerger(
    ITagStore tagStore,
    ITopicStore topicStore,
    TopicCatalogRefresher catalogRefresher,
    ILogger<TopicMerger> logger,
    TimeProvider clock)
{
    /// <summary>
    /// <paramref name="from"/> を <paramref name="into"/> へ寄せる。寄せられなければ false。
    ///
    /// **選択済み(収集対象)のトピックは寄せない。** 収集キーワードが黙って変わるため ——
    /// 寄せたいときは先に選択を外してもらう。
    /// </summary>
    public async Task<bool> MergeAsync(
        string from,
        string into,
        DecidedBy decidedBy,
        CancellationToken cancellationToken = default)
    {
        if (from == into || from.Length == 0 || into.Length == 0)
        {
            return false;
        }

        var target = await topicStore.GetAsync(into, cancellationToken);
        if (target is null)
        {
            return false;
        }

        var source = await topicStore.GetAsync(from, cancellationToken);
        if (source is { IsSelected: true })
        {
            logger.LogInformation("収集対象に選ばれているので寄せない: {From} → {Into}", from, into);

            return false;
        }

        var now = clock.GetUtcNow();

        // 1. 寄せ元を指していたタグ(寄せ元自身を含む)を寄せ先の別名にする
        var moved = (await tagStore.GetAllAsync(cancellationToken))
            .Where(tag => tag.TopicKey == from || tag.Key == from)
            .Select(tag => new TagDecision(tag.Key, TagStatus.Alias, into, decidedBy))
            .ToList();
        await tagStore.DecideAsync(moved, now, cancellationToken);

        // 2. 寄せ元の子を寄せ先へ付け替える
        foreach (var child in (await topicStore.GetAllAsync(cancellationToken))
            .Where(topic => topic.Parent == from))
        {
            child.Parent = into;
            await topicStore.SaveAsync(child, now, cancellationToken);
        }

        // 3. 寄せ元のトピックを消す(選択済みは RemoveAsync が守る。ここまで来たら選択は無い)
        if (source is not null)
        {
            await topicStore.RemoveAsync([from], cancellationToken);
        }

        await catalogRefresher.RefreshAsync(cancellationToken);
        logger.LogInformation("トピックを寄せた: {From} → {Into}(タグ {Moved} 件)", from, into, moved.Count);

        return true;
    }

    /// <summary>
    /// トピックだったタグを語彙から外す(トピック外・仕分けまちへ戻すとき)。
    /// **トピックの行も消す** —— 残すと、どのタグにも紐づかない行がツリーに居座る。
    /// </summary>
    public async Task DemoteAsync(string key, CancellationToken cancellationToken = default)
    {
        // 子がいれば親を切る(消える親を指したままにしない)
        var now = clock.GetUtcNow();
        foreach (var child in (await topicStore.GetAllAsync(cancellationToken))
            .Where(topic => topic.Parent == key))
        {
            child.Parent = null;
            await topicStore.SaveAsync(child, now, cancellationToken);
        }

        await topicStore.RemoveAsync([key], cancellationToken);
        await catalogRefresher.RefreshAsync(cancellationToken);
    }
}

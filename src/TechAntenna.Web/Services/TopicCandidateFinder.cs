using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;

namespace TechAntenna.Web.Services;

/// <summary>新規トピックの候補1語。Count は集めた記事・イベント・書籍に付いている回数。</summary>
public record TopicCandidate(string Tag, int Count);

/// <summary>
/// 新規トピックの候補を、**保存済みデータから導出する**。
///
/// 候補は「集めた記事などのタグのうち、カタログに無く、まだ分類も確定していない語」。
/// 記事・イベント・書籍を収集するたびにタグとして自然に溜まるので、
/// 専用の収集や保存は要らない —— 見つける行為(語彙)と、話題度(鮮度が要る)は性質が別で、
/// 候補集めのために外部へ聞きに行く必要はない。
///
/// 設定画面の「新規トピック候補」の表示と、トピックの再編成(LLM 分類の入力)の両方で使う。
/// </summary>
public class TopicCandidateFinder(
    TopicCatalog catalog,
    IArticleStore articleStore,
    IEventStore eventStore,
    IBookStore bookStore,
    ITopicClassificationStore classificationStore,
    TimeProvider clock)
{
    /// <summary>候補にする件数の下限。1〜2件の語は誤記や一過性のタグが多く、
    /// LLM の枠を使ってまで整理する価値が無い(平置きのまま残る)。</summary>
    public const int MinCount = 3;

    /// <summary>Unknown(判断できなかった語)を再挑戦させるまでの日数。
    /// 短いと毎回同じ語で枠を使い、無期限だと新語が永久に平置きのまま残る。</summary>
    public const int UnknownRetryDays = 7;

    /// <summary>候補を件数の多い順に返す。</summary>
    public async Task<IReadOnlyList<TopicCandidate>> FindAsync(CancellationToken cancellationToken = default)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var store in (Func<CancellationToken, Task<IReadOnlyList<TagCount>>>[])
            [articleStore.GetTagCountsAsync, eventStore.GetTagCountsAsync, bookStore.GetTagCountsAsync])
        {
            foreach (var tagCount in await store(cancellationToken))
            {
                counts[tagCount.Tag] = counts.GetValueOrDefault(tagCount.Tag) + tagCount.Count;
            }
        }

        // 分類済みの語(Skip 含む)は候補にしない —— 同じ語を毎回聞き直すと LLM の枠を無駄にする。
        // Unknown だけは期限付き: 期限内は除き、過ぎたら候補に戻してもう一度聞く
        var retryBefore = clock.GetUtcNow().AddDays(-UnknownRetryDays);
        var classified = (await classificationStore.GetAllAsync(cancellationToken))
            .Where(c => c.Kind != TopicClassificationKind.Unknown || c.ClassifiedAt >= retryBefore)
            .Select(c => c.Tag)
            .ToHashSet(StringComparer.Ordinal);

        return counts
            .Where(pair => pair.Value >= MinCount
                && !catalog.Contains(pair.Key)
                && !classified.Contains(pair.Key))
            .Select(pair => new TopicCandidate(pair.Key, pair.Value))
            .OrderByDescending(candidate => candidate.Count)
            .ThenBy(candidate => candidate.Tag, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>この語は分類済み(または期限内の Unknown)として除外すべきか、の判定に使う集合を返す。</summary>
    public async Task<HashSet<string>> GetExcludedAsync(CancellationToken cancellationToken = default)
    {
        var retryBefore = clock.GetUtcNow().AddDays(-UnknownRetryDays);

        return (await classificationStore.GetAllAsync(cancellationToken))
            .Where(c => c.Kind != TopicClassificationKind.Unknown || c.ClassifiedAt >= retryBefore)
            .Select(c => c.Tag)
            .ToHashSet(StringComparer.Ordinal);
    }
}

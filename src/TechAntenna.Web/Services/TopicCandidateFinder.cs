using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;

namespace TechAntenna.Web.Services;

/// <summary>
/// 次の再編成で LLM に聞く候補1語。
/// </summary>
/// <param name="Tag">正規化済みのタグ。</param>
/// <param name="Count">集めた記事・イベント・書籍に付いている回数。</param>
/// <param name="TrendScore">前回までの再編成で外部トレンドから付いた話題度(トレンド由来の語はこちらだけ入る)。</param>
public record TopicCandidate(string Tag, int Count, double TrendScore = 0);

/// <summary>
/// 次の再編成で LLM に聞く語を、**保存済みのデータだけから導出する**。
/// 画面の表示(「次の再編成で LLM に聞く語」)と再編成の入力が<b>同じ 1 か所</b>から出るように
/// してあるのが要点 —— 別々に組むと、画面に 1 語しか出ていないのに実行すると 30 語聞く、
/// という食い違いが起きる(実際に起きた)。
///
/// 材料は 2 つで、どちらも DB を読むだけで決まる:
///
/// 1. **集めたデータのタグ** —— カタログに無く、まだ分類も確定していない語のうち
///    <see cref="MinCount"/> 回以上付いたもの。記事などを収集するたびに自然に溜まる
/// 2. **前回までのトレンドで現れた語** —— トピック一覧に行はあるが語彙に入っていない語。
///    **その回のトレンドをその場で足さない**のが肝 —— 足すと「押すまで何語 LLM に流れるか
///    分からない」状態になる。今回のトレンドで見つかった語は行として残り、次の回の候補になる
/// </summary>
public class TopicCandidateFinder(
    TopicCatalog catalog,
    ITopicStore topicStore,
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

    /// <summary>
    /// 候補を「目立つ順」(件数 + 話題度)に返す。**上限は掛けない** ——
    /// 1 回に何語聞くかは再編成側の枠(<c>MaxTagsPerClassification</c>)で決め、
    /// 画面では枠に収まらない分も「次回に回る語」として見せたいため。
    /// </summary>
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

        var excluded = await GetExcludedAsync(cancellationToken);
        bool Wanted(string tag) => !catalog.Contains(tag) && !excluded.Contains(tag);

        // 1. 集めたデータのタグ(下限あり)
        var candidates = counts
            .Where(pair => pair.Value >= MinCount && Wanted(pair.Key))
            .ToDictionary(pair => pair.Key, pair => new TopicCandidate(pair.Key, pair.Value),
                StringComparer.Ordinal);

        // 2. 前回までのトレンドで現れた語(話題度が付いている = 外部で見つかった語)。
        //    件数の下限は掛けない —— 手元のデータには 1 件も無いのが普通で、
        //    「外で話題になっている新語」こそツリーへ入れたい
        foreach (var topic in await topicStore.GetAllAsync(cancellationToken))
        {
            if (topic.TrendScore <= 0 && topic.SourceCount <= 0)
            {
                continue;
            }

            if (!Wanted(topic.Tag))
            {
                continue;
            }

            candidates[topic.Tag] = candidates.TryGetValue(topic.Tag, out var found)
                ? found with { TrendScore = topic.TrendScore }
                : new TopicCandidate(topic.Tag, counts.GetValueOrDefault(topic.Tag), topic.TrendScore);
        }

        return candidates.Values
            .OrderByDescending(candidate => candidate.Count + candidate.TrendScore)
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

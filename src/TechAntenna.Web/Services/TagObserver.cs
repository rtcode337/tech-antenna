using TechAntenna.Core.Abstractions;

namespace TechAntenna.Web.Services;

/// <summary>
/// 保存済みデータのタグを数え直して <see cref="ITagStore"/> へ書き込む。
///
/// **収集のたびに呼ぶ。** 収集は `Articles.Tags` などを書くだけで、タグの行には触らないので、
/// これが無いと<b>集めたのにタグの画面が変わらない</b>(仕分けまちの語が増えない)。
/// 以前はトピック一覧を再編成でしか作っていなかったので気づかなかったが、
/// タグを画面が直接見るようになって表に出た。
///
/// **状態は触らない。** 件数と話題度だけを書き替える —— 収集のたびに仕分けが巻き戻ると、
/// 同じ語を何度も LLM に聞くことになる。
/// </summary>
public class TagObserver(
    ITagStore tagStore,
    IArticleStore articleStore,
    IEventStore eventStore,
    IBookStore bookStore,
    TimeProvider clock)
{
    /// <summary>
    /// 件数を数え直して書き込み、観測したタグの数を返す。
    ///
    /// <paramref name="resetMissing"/> は「渡さなかったタグの件数を 0 にするか」。
    /// **収集からは false で呼ぶ** —— 収集は 1 種類(記事だけ等)しか触らないので、
    /// 全消しにするとイベントや書籍の件数まで 0 になる。再編成は 3 種すべてを数えるので true。
    /// </summary>
    public async Task<int> ObserveAsync(
        Dictionary<string, (double Score, int Sources)>? trends = null,
        bool resetMissing = false,
        CancellationToken cancellationToken = default)
    {
        var counts = new Dictionary<string, (int Articles, int Events, int Books)>(StringComparer.Ordinal);

        foreach (var tagCount in await articleStore.GetTagCountsAsync(cancellationToken))
        {
            var current = counts.GetValueOrDefault(tagCount.Tag);
            counts[tagCount.Tag] = (current.Articles + tagCount.Count, current.Events, current.Books);
        }

        foreach (var tagCount in await eventStore.GetTagCountsAsync(cancellationToken))
        {
            var current = counts.GetValueOrDefault(tagCount.Tag);
            counts[tagCount.Tag] = (current.Articles, current.Events + tagCount.Count, current.Books);
        }

        foreach (var tagCount in await bookStore.GetTagCountsAsync(cancellationToken))
        {
            var current = counts.GetValueOrDefault(tagCount.Tag);
            counts[tagCount.Tag] = (current.Articles, current.Events, current.Books + tagCount.Count);
        }

        var scores = trends ?? [];
        var observations = counts.Keys.Concat(scores.Keys)
            .Distinct(StringComparer.Ordinal)
            .Select(tag =>
            {
                var count = counts.GetValueOrDefault(tag);
                var trend = scores.GetValueOrDefault(tag);

                return new TagObservation(
                    tag, count.Articles, count.Events, count.Books, trend.Score, trend.Sources);
            })
            .ToList();

        await tagStore.ObserveAsync(
            observations, clock.GetUtcNow(), resetMissing, cancellationToken);

        return observations.Count;
    }
}

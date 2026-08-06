using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Core.Trends;

namespace TechAntenna.Web.Services;

/// <summary>
/// トピック一覧を作り直す。**語彙・話題度・在庫の 3 つを 1 回で組み立てる**。
///
/// 1. 語彙 —— `topic-catalog.json` のトピック(名前と別名の対応表)
/// 2. 話題度 —— 外部トレンド(<see cref="ITrendTopicSource"/>)。カタログに無い語もここから入る
/// 3. 在庫 —— 自分が集めた記事・イベント・書籍の件数
///
/// **1 本のジョブにしてあるのは、分けると互いの結果を消し合うから。** 以前はカタログ生成が
/// 全行を削除し、話題度の更新が全行を 0 にしてから書き戻していたため、押した順番で結果が
/// 変わっていた。ここでまとめて作り、ストアには upsert する。
///
/// **在庫は順位に足さない。** 収集するのは選択したトピックだけなので、在庫で加点すると
/// 選択済みのものが上位に張り付き、新しいトピックが永久に浮上しなくなる(表示はする)。
///
/// **カタログに無い語は LLM(<see cref="ITopicClassifier"/>)で分類してツリーへ入れる**
/// (同義語なら既存トピックへ寄せ、粒度の違いなら親付きの新トピックにする)。
/// 表記ゆれの判定は機械的にできるが、意味の同一と粒度の上下は語を知らないと判定できない。
/// LLM が未設定でも収集は動く(未知の語が平置きのまま残るだけ)。
/// </summary>
public class TopicCollectionRunner(
    TopicCatalog catalog,
    IEnumerable<ITrendTopicSource> sources,
    ITopicStore topicStore,
    IArticleStore articleStore,
    IEventStore eventStore,
    IBookStore bookStore,
    ITopicClassificationStore classificationStore,
    TagRenormalizationRunner renormalizationRunner,
    ILogger<TopicCollectionRunner> logger,
    TimeProvider clock,
    ITopicClassifier? classifier = null) : JobRunner
{
    /// <summary>1回の実行で LLM に渡す未知タグの上限(入力が際限なく膨らまないように)。</summary>
    const int MaxTagsPerClassification = 200;

    readonly IReadOnlyList<ITrendTopicSource> _sources = sources.ToList();

    int _failedSources;

    public override string Name => "トピックを収集";

    // カタログだけでも一覧は作れる(外部トレンドが無ければ話題度が 0 になるだけ)
    public override bool IsConfigured => catalog.Entries.Count > 0 || _sources.Count > 0;

    public Task<TopicCollectionResult> RunOnceAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => CollectAsync(cancellationToken), TopicCollectionResult.Nothing, cancellationToken);

    async Task<TopicCollectionResult> CollectAsync(CancellationToken cancellationToken)
    {
        var trends = await FetchTrendsAsync(cancellationToken);
        var counts = await FetchCountsAsync(cancellationToken);

        // 未知の語を LLM で分類し、通ったぶんでカタログとデータを追従させる
        var classified = await ClassifyUnknownAsync(
            trends.Keys.Concat(counts.Keys), cancellationToken);
        if (classified > 0)
        {
            // 別名が増えたので、保存済みデータのタグと在庫の集計を作り直す。
            // トレンドの語も新しいカタログで寄せ直す(`ai駆動開発` が別名になったら合算する)
            await renormalizationRunner.RunOnceAsync(cancellationToken);
            counts = await FetchCountsAsync(cancellationToken);
            trends = Remap(trends);
        }

        // カタログの語彙 + トレンドで見つかった語 + 既に在庫がある語
        var tags = catalog.Entries.Select(entry => entry.Key)
            .Concat(trends.Keys)
            .Concat(counts.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var topics = tags
            .Select(tag =>
            {
                var trend = trends.GetValueOrDefault(tag);
                var count = counts.GetValueOrDefault(tag);

                return new TopicUpdate(
                    tag,
                    catalog.DisplayOf(tag),
                    catalog.ParentOf(tag),
                    trend.Score,
                    trend.Sources,
                    count.Articles,
                    count.Events,
                    count.Books);
            })
            .ToList();

        await topicStore.UpsertAsync(topics, clock.GetUtcNow(), cancellationToken);

        return new TopicCollectionResult(topics.Count, trends.Count, _failedSources, classified);
    }

    /// <summary>
    /// カタログに無く、まだ分類していない語を LLM に渡し、検証を通った分類の件数を返す。
    /// **失敗しても収集は続ける**(分類は次の実行でやり直せるが、トピック一覧が
    /// 作られないと選択まで狂う)。
    /// </summary>
    async Task<int> ClassifyUnknownAsync(
        IEnumerable<string> candidates, CancellationToken cancellationToken)
    {
        if (classifier is null)
        {
            return 0;
        }

        try
        {
            // 分類済みの語(Skip 含む)は除く —— 同じ語を毎回聞き直すと LLM の枠を無駄にする
            var alreadyClassified = (await classificationStore.GetAllAsync(cancellationToken))
                .Select(c => c.Tag)
                .ToHashSet(StringComparer.Ordinal);
            var unknown = candidates
                .Distinct(StringComparer.Ordinal)
                .Where(tag => !catalog.Contains(tag) && !alreadyClassified.Contains(tag))
                .Order(StringComparer.Ordinal)
                .Take(MaxTagsPerClassification)
                .ToList();

            if (unknown.Count == 0)
            {
                return 0;
            }

            var verdicts = await classifier.ClassifyAsync(unknown, catalog.Entries, cancellationToken);
            var accepted = TopicClassificationValidator.Validate(
                unknown, verdicts, catalog, clock.GetUtcNow());

            await classificationStore.UpsertAsync(accepted, cancellationToken);
            catalog.Extend(accepted);

            var effective = accepted.Count(c => c.Kind != TopicClassificationKind.Skip);
            logger.LogInformation(
                "{Classifier} が未知の語 {Unknown} 件を分類: 反映 {Effective} 件(対象外 {Skipped} 件)",
                classifier.Name, unknown.Count, effective,
                accepted.Count(c => c.Kind == TopicClassificationKind.Skip));

            return effective;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "未知タグの分類に失敗(収集は続ける)");
            return 0;
        }
    }

    /// <summary>拡張後のカタログでトレンドの語を寄せ直す(別名になった語のスコアを合算する)。</summary>
    Dictionary<string, (double Score, int Sources)> Remap(
        Dictionary<string, (double Score, int Sources)> trends)
    {
        var remapped = new Dictionary<string, (double Score, int Sources)>(StringComparer.Ordinal);
        foreach (var (tag, value) in trends)
        {
            var resolved = catalog.Resolve(tag);
            var current = remapped.GetValueOrDefault(resolved);
            remapped[resolved] = (current.Score + value.Score, Math.Max(current.Sources, value.Sources));
        }

        return remapped;
    }

    /// <summary>
    /// 収集元ごとの話題度を、**そのソース内でのシェア**(合計に対する割合 × 100)に直してから合算する。
    /// 生の値のまま足すと、桁の大きい収集元が常に勝つ —— 全期間の質問数(10^6)と直近のいいね数(10^1)を
    /// 同じ列に入れると、後者は事実上無視される。
    /// </summary>
    async Task<Dictionary<string, (double Score, int Sources)>> FetchTrendsAsync(CancellationToken cancellationToken)
    {
        var merged = new Dictionary<string, (double Score, int Sources)>(StringComparer.Ordinal);
        _failedSources = 0;

        foreach (var source in _sources)
        {
            IReadOnlyList<TrendTopicCandidate> candidates;
            try
            {
                candidates = await source.FetchAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 1 つの収集元が落ちても他は使う(トピックが丸ごと空になるほうが困る)
                _failedSources++;
                logger.LogError(ex, "{Source} のトレンド取得に失敗", source.Name);
                continue;
            }

            // 別名をカタログの正式表記へ寄せてから集計する(`人工知能` を `ai` に寄せる)
            var byTag = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                var tag = catalog.Resolve(candidate.Tag);
                byTag[tag] = byTag.GetValueOrDefault(tag) + Math.Max(0, candidate.Score);
            }

            var total = byTag.Values.Sum();
            if (total <= 0)
            {
                continue;
            }

            foreach (var (tag, score) in byTag)
            {
                var current = merged.GetValueOrDefault(tag);
                merged[tag] = (current.Score + (score / total * 100), current.Sources + 1);
            }
        }

        return merged;
    }

    async Task<Dictionary<string, (int Articles, int Events, int Books)>> FetchCountsAsync(
        CancellationToken cancellationToken)
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

        return counts;
    }
}

/// <summary>トピック収集の結果。</summary>
/// <param name="Count">一覧に載ったトピックの数。</param>
/// <param name="Trending">そのうち話題度が付いた(外部トレンドに現れた)数。</param>
/// <param name="FailedSources">取得に失敗した収集元の数。</param>
/// <param name="Classified">今回 LLM の分類でツリーに入った語の数(同義語 + 新トピック)。</param>
public record TopicCollectionResult(int Count, int Trending, int FailedSources, int Classified = 0)
{
    public static readonly TopicCollectionResult Nothing = new(0, 0, 0);
}

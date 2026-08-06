using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Core.Trends;

namespace TechAntenna.Web.Services;

/// <summary>
/// トピックを再編成する。**語彙・話題度・収集済み件数の 3 つを 1 回で組み立てる**。
/// 「収集」ではなく「再編成」なのは、材料の性質が分かれているから ——
/// **新規トピックの候補は記事などの収集で自然に溜まる**(語彙の問題。ここでは集めない)。
/// このジョブは溜まった候補を LLM でツリーへ組み込み、**話題度だけをその場で取り直す**
/// (鮮度の問題)。
///
/// 1. 語彙 —— `topic-catalog.json` のトピック + 候補(<see cref="TopicCandidateFinder"/>)
/// 2. 話題度 —— 外部トレンド(<see cref="ITrendTopicSource"/>)。Qiita の新着(いいね重み)と
///    はてブ人気エントリー RSS(ブックマーク数重み)を実行時にその場から取得する
/// 3. 収集済み件数 —— 自分が集めた記事・イベント・書籍にそのタグが何回付いているか
///
/// **1 本のジョブにしてあるのは、分けると互いの結果を消し合うから。** 以前はカタログ生成が
/// 全行を削除し、話題度の更新が全行を 0 にしてから書き戻していたため、押した順番で結果が
/// 変わっていた。ここでまとめて作り、ストアには upsert する。
///
/// **収集済み件数は順位に足さない。** 収集するのは選択したトピックだけなので、件数で加点すると
/// 選択済みのものが上位に張り付き、新しいトピックが永久に浮上しなくなる(表示はする)。
///
/// **カタログに無い語は LLM(<see cref="ITopicClassifier"/>)で分類してツリーへ入れる**
/// (同義語なら既存トピックへ寄せ、粒度の違いなら親付きの新トピックにする)。
/// 表記ゆれの判定は機械的にできるが、意味の同一と粒度の上下は語を知らないと判定できない。
/// LLM が未設定でも収集は動く(未知の語が平置きのまま残るだけ)。
/// </summary>
public class TopicReorganizationRunner(
    TopicCatalog catalog,
    IEnumerable<ITrendTopicSource> sources,
    ITopicStore topicStore,
    IArticleStore articleStore,
    IEventStore eventStore,
    IBookStore bookStore,
    ITopicClassificationStore classificationStore,
    TopicCandidateFinder candidateFinder,
    TagRenormalizationRunner renormalizationRunner,
    ILogger<TopicReorganizationRunner> logger,
    TimeProvider clock,
    ITopicClassifier? classifier = null) : JobRunner
{
    /// <summary>
    /// 1回の実行で LLM に渡す未知タグの上限(呼び出し回数の暴走を防ぐ枠)。
    /// ジョブはバックグラウンドで走り進捗も見えるので、時間よりも
    /// 「1回の実行で LLM を何回呼ぶか」(60 語 × 5 バッチ)で決めている。
    /// </summary>
    const int MaxTagsPerClassification = 300;

    readonly IReadOnlyList<ITrendTopicSource> _sources = sources.ToList();

    int _failedSources;

    public override string Name => "トピックを再編成";

    // カタログだけでも一覧は作れる(外部トレンドが無ければ話題度が 0 になるだけ)
    public override bool IsConfigured => catalog.Entries.Count > 0 || _sources.Count > 0;

    public Task<TopicReorganizationResult> RunOnceAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => CollectAsync(cancellationToken), TopicReorganizationResult.Nothing, cancellationToken);

    async Task<TopicReorganizationResult> CollectAsync(CancellationToken cancellationToken)
    {
        Progress = "外部トレンドを取得中…";
        var trends = await FetchTrendsAsync(cancellationToken);
        Progress = "集めたデータの件数を集計中…";
        var counts = await FetchCountsAsync(cancellationToken);

        // 未知の語を LLM で分類し、通ったぶんでカタログとデータを追従させる
        var classified = await ClassifyUnknownAsync(trends, counts, cancellationToken);

        // 保存済みデータのタグを常に作り直してから集計する。LLM 分類だけでなく、
        // 手で編集したカタログ(topic-catalog.json)の別名もここで過去データに効く ——
        // かつては別ボタン(タグを再正規化)だったが、収集のたびに走らせても数秒で
        // 冪等なので、ボタンを覚えて押してもらうより毎回やるほうが確実。
        // トレンドの語も同じカタログで寄せ直す(`ai駆動開発` が別名になったら合算する)
        Progress = "タグを再正規化中…";
        await renormalizationRunner.RunOnceAsync(cancellationToken);
        counts = await FetchCountsAsync(cancellationToken);
        trends = Remap(trends);

        Progress = "トピック一覧を書き込み中…";

        // LLM が「トピックでない」と確定させた語(Skip)は一覧に載せず、残っている行も消す
        // —— ニュース・ゲーム・開発 のような一般語が話題度の上位を占めると一覧が読めない。
        // 記事などのタグとしては残る(消すのはトピック一覧の行だけ)
        var skips = (await classificationStore.GetAllAsync(cancellationToken))
            .Where(c => c.Kind == TopicClassificationKind.Skip)
            .Select(c => c.Tag)
            .ToHashSet(StringComparer.Ordinal);
        await topicStore.RemoveAsync(skips.ToList(), cancellationToken);

        // カタログの語彙 + トレンドで見つかった語 + 集めたデータにタグとして付いている語
        var tags = catalog.Entries.Select(entry => entry.Key)
            .Concat(trends.Keys)
            .Concat(counts.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(tag => !skips.Contains(tag))
            .ToList();

        // 単体の話題度とは別に、配下を含めた合算(自身 + 子孫の合計)も持たせる。
        // 親は「プログラミング言語」のような構造の語で単体の話題度がほぼ付かず、
        // 単体だけで並べると一覧の取得件数から押し出されて子が根として孤立する。
        // 合算があれば、子が上位に入る木は親も必ず入る(単体は単体でランキングに使う)
        var aggregated = AggregateTrendScores(tags, trends);

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
                    aggregated.GetValueOrDefault(tag),
                    trend.Sources,
                    count.Articles,
                    count.Events,
                    count.Books);
            })
            .ToList();

        await topicStore.UpsertAsync(topics, clock.GetUtcNow(), cancellationToken);

        return new TopicReorganizationResult(topics.Count, trends.Count, _failedSources, classified);
    }

    /// <summary>
    /// カタログに無く、まだ分類していない語を LLM に渡し、検証を通った分類の件数を返す。
    /// **失敗しても収集は続ける**(分類は次の実行でやり直せるが、トピック一覧が
    /// 作られないと選択まで狂う)。
    /// </summary>
    async Task<int> ClassifyUnknownAsync(
        Dictionary<string, (double Score, int Sources)> trends,
        Dictionary<string, (int Articles, int Events, int Books)> counts,
        CancellationToken cancellationToken)
    {
        if (classifier is null)
        {
            return 0;
        }

        try
        {
            // 候補は「集めたデータから溜まった語」(TopicCandidateFinder。設定画面の
            // 「新規トピック候補」と同じもの)+「今回のトレンドに現れた語」。
            // 上限で切れる分は、目立つ語(件数 + 話題度)から先に分類する
            var excluded = await candidateFinder.GetExcludedAsync(cancellationToken);
            var unknown = (await candidateFinder.FindAsync(cancellationToken))
                .Select(candidate => candidate.Tag)
                .Concat(trends.Keys.Where(tag => !catalog.Contains(tag) && !excluded.Contains(tag)))
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(tag => Total(counts.GetValueOrDefault(tag))
                    + trends.GetValueOrDefault(tag).Score)
                .ThenBy(tag => tag, StringComparer.Ordinal)
                .Take(MaxTagsPerClassification)
                .ToList();

            if (unknown.Count == 0)
            {
                return 0;
            }

            // 数分かかる。どこまで進んだかを画面(JobButton の自動リロード)に出す
            var verdicts = await classifier.ClassifyAsync(
                unknown,
                catalog.Entries,
                step => Progress = $"未知の語 {unknown.Count} 件を LLM で分類中: {step}…",
                cancellationToken);
            var accepted = TopicClassificationValidator.Validate(
                unknown, verdicts, catalog, clock.GetUtcNow());

            await classificationStore.UpsertAsync(accepted, cancellationToken);
            catalog.Extend(accepted);

            var effective = accepted.Count(c => c.Kind
                is TopicClassificationKind.Alias or TopicClassificationKind.NewTopic);
            logger.LogInformation(
                "{Classifier} が未知の語 {Unknown} 件を分類: 反映 {Effective} 件(対象外 {Skipped} 件・保留 {Pending} 件)",
                classifier.Name, unknown.Count, effective,
                accepted.Count(c => c.Kind == TopicClassificationKind.Skip),
                accepted.Count(c => c.Kind == TopicClassificationKind.Unknown));

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

    static int Total((int Articles, int Events, int Books) count) =>
        count.Articles + count.Events + count.Books;

    /// <summary>
    /// タグごとの話題度を「自身 + 配下のトピックの合計」に畳み込む。
    /// LLM 分類の親子が万一循環していても、訪問中の枝は 0 として打ち切る。
    /// </summary>
    Dictionary<string, double> AggregateTrendScores(
        IReadOnlyList<string> tags, Dictionary<string, (double Score, int Sources)> trends)
    {
        var children = tags
            .Select(tag => (Tag: tag, Parent: catalog.ParentOf(tag)))
            .Where(pair => pair.Parent is { Length: > 0 } && pair.Parent != pair.Tag)
            .ToLookup(pair => pair.Parent!, pair => pair.Tag, StringComparer.Ordinal);

        var memo = new Dictionary<string, double>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        double Visit(string tag)
        {
            if (memo.TryGetValue(tag, out var known))
            {
                return known;
            }

            if (!visiting.Add(tag))
            {
                return 0;
            }

            var sum = trends.GetValueOrDefault(tag).Score;
            foreach (var child in children[tag])
            {
                sum += Visit(child);
            }

            visiting.Remove(tag);
            memo[tag] = sum;
            return sum;
        }

        foreach (var tag in tags)
        {
            Visit(tag);
        }

        return memo;
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
public record TopicReorganizationResult(int Count, int Trending, int FailedSources, int Classified = 0)
{
    public static readonly TopicReorganizationResult Nothing = new(0, 0, 0);
}

using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Core.Trends;

namespace TechAntenna.Web.Services;

/// <summary>
/// トピックを再編成する。**タグの観測 → 仕分け → 語彙の組み立て**を 1 回で通す。
/// 「収集」ではなく「再編成」なのは、材料の性質が分かれているから ——
/// **新しい語は記事などの収集で自然に溜まる**(語彙の問題。ここでは集めない)。
/// このジョブは溜まったタグを LLM で仕分けて語彙に組み込み、
/// **話題度だけをその場で取り直す**(鮮度の問題)。
///
/// 手順(画面の説明とそろえること):
///
/// 1. 外部トレンドを取得(<see cref="ITrendTopicSource"/>。Qiita のいいね・はてブのブックマーク数)
/// 2. タグを作り直す(1 回目) —— 正規化の規則やカタログを変えたときに<b>そこで初めて現れる語</b>を
///    保存済みデータへ反映する
/// 3. 残骸のタグ・トピックを掃除する(いまの正規化では作られないキー)
/// 4. **前回までに観測したタグ**を LLM で仕分け(<see cref="ITopicClassifier"/>)。
///    今回の観測より前に置くのが肝 —— その回に取得したトレンドの語まで聞くと、
///    押すまで何語 LLM に流れるか分からなくなる
/// 5. 説明の無いトピックに一言説明を付ける(<see cref="ITopicDescriber"/>)
/// 6. タグを作り直す(2 回目) —— 今回増えた別名を過去データへ反映する
/// 7. タグを観測(件数 + 話題度を書き込む。**状態は触らない**)。
///    ここで<b>次の回の対象が確定する</b>(画面に出す一覧と同じ)
/// 8. 語彙を組み立てて書き込む(件数と話題度の合算)
///
/// **1 本のジョブにしてあるのは、分けると互いの結果を消し合うから。**
/// 以前はカタログ生成が全行を削除し、話題度の更新が全行を 0 にしてから書き戻していたため、
/// 押した順番で結果が変わっていた。
/// </summary>
public class TopicReorganizationRunner(
    TopicCatalog catalog,
    IEnumerable<ITrendTopicSource> sources,
    ITagStore tagStore,
    ITopicStore topicStore,
    IArticleStore articleStore,
    IEventStore eventStore,
    IBookStore bookStore,
    TagRenormalizationRunner renormalizationRunner,
    TopicCatalogRefresher catalogRefresher,
    ILogger<TopicReorganizationRunner> logger,
    TimeProvider clock,
    ITopicClassifier? classifier = null,
    ITopicDescriber? describer = null) : JobRunner
{
    /// <summary>
    /// 1回の実行で LLM に渡すタグの上限(呼び出し回数の暴走を防ぐ枠)。
    /// 「1回の実行で LLM を何回呼ぶか」(60 語 × 5 バッチ)で決めている。
    /// </summary>
    public const int MaxTagsPerClassification = 300;

    /// <summary>1回の実行で一言説明を埋める語の上限。分類と同じ考え方。</summary>
    public const int MaxTermsPerDescription = 300;

    /// <summary>
    /// LLM に聞く下限(集めたデータに付いた回数)。1〜2 件の語は誤記や一過性のタグが多く、
    /// 枠を使ってまで整理する価値が無い。**外部トレンドで見つかった語には掛からない**
    /// —— 手元の件数は 0 なのが普通で、そこで落とすと新語が入ってこない。
    /// </summary>
    public const int MinTagCount = 3;

    /// <summary>判断できなかったタグを再挑戦させるまでの日数。</summary>
    public const int UnresolvedRetryDays = 7;

    readonly IReadOnlyList<ITrendTopicSource> _sources = sources.ToList();

    int _failedSources;

    public override string Name => "トピックを再編成";

    // 語彙だけでも一覧は作れる(外部トレンドが無ければ話題度が 0 になるだけ)
    public override bool IsConfigured => true;

    /// <summary>
    /// **直前の実行で実際に LLM へ聞いたタグ。** 画面で「何が対象になったのか」を見せるために持つ
    /// (アプリを再起動すると消えるので、画面は DB からの復元も併せて使う)。
    /// </summary>
    public IReadOnlyList<string> LastClassificationTargets { get; private set; } = [];

    public Task<TopicReorganizationResult> RunOnceAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () => ReorganizeAsync(cancellationToken), TopicReorganizationResult.Nothing, cancellationToken);

    async Task<TopicReorganizationResult> ReorganizeAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        Progress = "外部トレンドを取得中…";
        var trends = await FetchTrendsAsync(cancellationToken);

        // 分類の前にもタグを作り直す —— 正規化の規則を変えたときにそこで初めて現れる語を、
        // その回の対象に含めるため(DB だけの処理で数秒・冪等なので前後 2 回でよい)
        Progress = "タグを作り直し中…";
        await renormalizationRunner.RunOnceAsync(cancellationToken);

        // **残骸のタグを掃除する。** 正規化の規則を変えると、以前のキーのタグ
        // (`#生成ai`・`生成ai,`)が残る。中身は正しいタグに合流済みなので消して構わない
        await RemoveStaleAsync(cancellationToken);

        // **仕分けは「前回までに観測したタグ」を対象にする。** 今回の観測を先に入れると、
        // その場で取得したトレンドの語までその回で聞くことになり、
        // 押すまで何語 LLM に流れるか分からなくなる(画面に出している一覧と食い違う)
        var classified = await ClassifyPendingAsync(now, cancellationToken);
        var described = await DescribeMissingAsync(now, cancellationToken);

        // 2 回目のタグの作り直し。今回の仕分けで増えた別名を過去データへ反映する
        Progress = "タグを再正規化中…";
        await renormalizationRunner.RunOnceAsync(cancellationToken);

        // 観測はここで 1 回だけ。**次の回の対象がこの時点で確定する**(画面に出す一覧と同じ)
        Progress = "タグを観測中…";
        await ObserveTagsAsync(trends, now, cancellationToken);

        Progress = "語彙を組み立て中…";
        var topics = await BuildTopicsAsync(now, cancellationToken);
        await catalogRefresher.RefreshAsync(cancellationToken);

        return new TopicReorganizationResult(
            topics, trends.Count, _failedSources, classified, described);
    }

    /// <summary>
    /// 集めたデータの件数と外部トレンドの話題度を、タグの行へ書き込む。
    /// **状態には触らない** —— 収集のたびに仕分けが巻き戻らないようにするため。
    /// </summary>
    async Task ObserveTagsAsync(
        Dictionary<string, (double Score, int Sources)> trends,
        DateTimeOffset now,
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

        var observations = counts.Keys.Concat(trends.Keys)
            .Distinct(StringComparer.Ordinal)
            .Select(tag =>
            {
                var count = counts.GetValueOrDefault(tag);
                var trend = trends.GetValueOrDefault(tag);

                return new TagObservation(
                    tag, count.Articles, count.Events, count.Books, trend.Score, trend.Sources);
            })
            .ToList();

        // 渡さなかったタグは件数と話題度を 0 にする(別名がまとまって消えたタグに古い値が残らないように)
        await tagStore.ObserveAsync(observations, now, resetMissing: true, cancellationToken);
    }

    /// <summary>
    /// いまの正規化では作られないキーの行(タグ・トピック)を消す。
    /// **`Normalize` の往復で判定する** —— カンマは `Normalize` の側で語を分けているので、
    /// `ToKey` の比較では `生成ai,` を見逃す。選択済みのトピックは消さない。
    /// </summary>
    async Task RemoveStaleAsync(CancellationToken cancellationToken)
    {
        var staleTags = (await tagStore.GetAllAsync(cancellationToken))
            .Where(tag => !IsCurrentKey(tag.Key))
            .Select(tag => tag.Key)
            .ToList();
        var removedTags = await tagStore.RemoveAsync(staleTags, cancellationToken);

        var staleTopics = (await topicStore.GetAllAsync(cancellationToken))
            .Where(topic => !IsCurrentKey(topic.Key))
            .Select(topic => topic.Key)
            .ToList();
        var removedTopics = await topicStore.RemoveAsync(staleTopics, cancellationToken);

        if (removedTags > 0 || removedTopics > 0)
        {
            logger.LogInformation(
                "正規化で作られなくなった残骸を削除: タグ {Tags} 件・トピック {Topics} 件",
                removedTags, removedTopics);
        }
    }

    static bool IsCurrentKey(string key) =>
        TagNormalizer.Normalize([key]) is [var only] && only == key;

    /// <summary>
    /// 未仕分けのタグを LLM に渡し、仕分けが通った件数を返す。
    /// **失敗しても再編成は続ける**(仕分けは次の実行でやり直せるが、語彙が
    /// 組み立てられないと選択まで狂う)。
    /// </summary>
    async Task<int> ClassifyPendingAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (classifier is null)
        {
            return 0;
        }

        try
        {
            var pending = (await tagStore.GetPendingAsync(now, MinTagCount, cancellationToken))
                .Take(MaxTagsPerClassification)
                .Select(tag => tag.Key)
                .ToList();

            LastClassificationTargets = pending;
            if (pending.Count == 0)
            {
                return 0;
            }

            // 数分かかる。どこまで進んだかを画面(JobButton の自動リロード)に出す
            var verdicts = await classifier.ClassifyAsync(
                pending,
                catalog.Entries,
                step => Progress = $"未仕分けのタグ {pending.Count} 件を LLM で分類中: {step}…",
                cancellationToken);

            var accepted = TopicClassificationValidator.Validate(
                pending, verdicts, catalog, now, UnresolvedRetryDays);

            // 新しいトピックを先に入れてから仕分けを書く(寄せ先の行が無い状態を作らない)
            if (accepted.NewTopics.Count > 0)
            {
                var merged = (await topicStore.GetAllAsync(cancellationToken))
                    .Concat(accepted.NewTopics)
                    .GroupBy(topic => topic.Key, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToList();
                await topicStore.UpsertAsync(merged, now, cancellationToken);
            }

            await tagStore.DecideAsync(accepted.Decisions, now, cancellationToken);
            await catalogRefresher.RefreshAsync(cancellationToken);

            var effective = accepted.Decisions.Count(decision =>
                decision.Status is TagStatus.Promoted or TagStatus.Alias);
            logger.LogInformation(
                "{Classifier} がタグ {Pending} 件を仕分け: 語彙へ {Effective} 件"
                + "(トピック外 {Skipped} 件・保留 {Unresolved} 件・新トピック {New} 件)",
                classifier.Name, pending.Count, effective,
                accepted.Decisions.Count(d => d.Status == TagStatus.NotTopic),
                accepted.Decisions.Count(d => d.Status == TagStatus.Unresolved),
                accepted.NewTopics.Count);

            return effective;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "タグの仕分けに失敗(再編成は続ける)");
            return 0;
        }
    }

    /// <summary>
    /// 説明がまだ無いトピックに一言説明を付ける。**1 語につき 1 回だけ聞く**
    /// (結果は列に残るので、次の再編成では聞かない)。上限で切れる分は、
    /// 収集対象に選んだトピック → 話題度の高い順で先に埋める。
    /// </summary>
    async Task<int> DescribeMissingAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (describer is null)
        {
            return 0;
        }

        try
        {
            var missing = (await topicStore.GetAllAsync(cancellationToken))
                .Where(topic => topic.Description is not { Length: > 0 })
                .OrderByDescending(topic => topic.IsSelected)
                .ThenByDescending(topic => topic.TrendScore)
                .ThenBy(topic => topic.Key, StringComparer.Ordinal)
                .Take(MaxTermsPerDescription)
                .ToList();

            if (missing.Count == 0)
            {
                return 0;
            }

            // 説明させるのは正式表記(キーは小文字で区切りも落ちていて語として読みにくい)
            var terms = missing.Select(topic => topic.Display).ToList();
            var verdicts = await describer.DescribeAsync(
                terms,
                step => Progress = $"説明の無い用語 {terms.Count} 件を LLM で説明中: {step}…",
                cancellationToken);

            var filled = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var verdict in verdicts)
            {
                // 番号が範囲外・重複・空文字は捨てる(LLM の応答をそのまま信じない)
                if (verdict.Index >= 1 && verdict.Index <= missing.Count
                    && !string.IsNullOrWhiteSpace(verdict.Text))
                {
                    filled.TryAdd(missing[verdict.Index - 1].Key, verdict.Text);
                }
            }

            if (filled.Count == 0)
            {
                return 0;
            }

            var topics = await topicStore.GetAllAsync(cancellationToken);
            foreach (var topic in topics.Where(topic => filled.ContainsKey(topic.Key)))
            {
                topic.Description = filled[topic.Key];
            }

            await topicStore.UpsertAsync(topics, now, cancellationToken);
            await catalogRefresher.RefreshAsync(cancellationToken);

            logger.LogInformation(
                "{Describer} が用語 {Asked} 件のうち {Filled} 件に説明を付けた",
                describer.Name, terms.Count, filled.Count);

            return filled.Count;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "用語の説明に失敗(再編成は続ける)");
            return 0;
        }
    }

    /// <summary>
    /// タグの観測結果を語彙へ集約して書き込み、書いたトピック数を返す。
    ///
    /// - **件数と話題度は「自分自身 + 別名」のタグから合算する**。別名の件数が寄せ先に
    ///   合算されるのが構造で保証されるのは、この形にしたから
    /// - **配下込みの話題度も持つ**。「プログラミング言語」のような構造の語は単体の話題度が
    ///   ほぼ付かず、単体だけで並べるとツリーが読みにくい
    /// </summary>
    async Task<int> BuildTopicsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var tags = await tagStore.GetAllAsync(cancellationToken);
        var topics = await topicStore.GetAllAsync(cancellationToken);

        var byTopic = tags
            .Where(tag => tag.TopicKey is { Length: > 0 })
            .GroupBy(tag => tag.TopicKey!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (var topic in topics)
        {
            var owned = byTopic.GetValueOrDefault(topic.Key, []);
            topic.ArticleCount = owned.Sum(tag => tag.ArticleCount);
            topic.EventCount = owned.Sum(tag => tag.EventCount);
            topic.BookCount = owned.Sum(tag => tag.BookCount);
            topic.TrendScore = owned.Sum(tag => tag.TrendScore);
        }

        foreach (var (key, score) in AggregateTrendScores(topics))
        {
            var topic = topics.First(topic => topic.Key == key);
            topic.SubtreeTrendScore = score;
        }

        await topicStore.UpsertAsync(topics, now, cancellationToken);

        return topics.Count;
    }

    /// <summary>
    /// 話題度を「自身 + 配下のトピックの合計」に畳み込む。
    /// LLM の分類の親子が万一循環していても、訪問中の枝は 0 として打ち切る。
    /// </summary>
    static Dictionary<string, double> AggregateTrendScores(IReadOnlyList<Topic> topics)
    {
        var children = topics
            .Where(topic => topic.Parent is { Length: > 0 } parent && parent != topic.Key)
            .ToLookup(topic => topic.Parent!, topic => topic.Key, StringComparer.Ordinal);
        var own = topics.ToDictionary(topic => topic.Key, topic => topic.TrendScore, StringComparer.Ordinal);

        var memo = new Dictionary<string, double>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        double Visit(string key)
        {
            if (memo.TryGetValue(key, out var known))
            {
                return known;
            }

            if (!visiting.Add(key))
            {
                return 0;
            }

            var sum = own.GetValueOrDefault(key);
            foreach (var child in children[key])
            {
                sum += Visit(child);
            }

            visiting.Remove(key);
            memo[key] = sum;

            return sum;
        }

        foreach (var topic in topics)
        {
            Visit(topic.Key);
        }

        return memo;
    }

    /// <summary>
    /// 収集元ごとの話題度を、**そのソース内でのシェア**(合計に対する割合 × 100)に直してから合算する。
    /// 生の値のまま足すと、桁の大きい収集元が常に勝つ —— 全期間の質問数(10^6)と
    /// 直近のいいね数(10^1)を同じ列に入れると、後者は事実上無視される。
    /// </summary>
    async Task<Dictionary<string, (double Score, int Sources)>> FetchTrendsAsync(
        CancellationToken cancellationToken)
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

            // 別名を正式表記のキーへ寄せてから集計する(`人工知能` を `ai` に寄せる)
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
}

/// <summary>トピック再編成の結果。</summary>
/// <param name="Count">語彙に載ったトピックの数。</param>
/// <param name="Trending">話題度が付いた(外部トレンドに現れた)タグの数。</param>
/// <param name="FailedSources">取得に失敗した収集元の数。</param>
/// <param name="Classified">今回 LLM の仕分けで語彙へ入った(昇格・別名)タグの数。</param>
/// <param name="Described">今回 LLM が一言説明を付けた用語の数。</param>
public record TopicReorganizationResult(
    int Count, int Trending, int FailedSources, int Classified = 0, int Described = 0)
{
    public static readonly TopicReorganizationResult Nothing = new(0, 0, 0);
}

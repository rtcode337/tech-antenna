using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Core.Trends;

namespace TechAntenna.Web.Services;

/// <summary>
/// トピックの整備。**入口を2つ持つ**(どちらも語彙の組み立てまで面倒を見る)。
///
/// | 入口 | やること | LLM | 未知語が増えるか |
/// |---|---|---|---|
/// | <see cref="RefreshTrendsAsync"/> 話題度を取り直す | 外部トレンドを引いて鮮度を更新 | 使わない | **増える** |
/// | <see cref="ReclassifyTagsAsync"/> タグを仕分けなおす | 溜まったタグを LLM で語彙へ入れる | 使う | **増えない** |
///
/// **分けてあるのは、1 本だと終わらないから。** 以前は 1 つのジョブで「トレンドを引く →
/// 溜まったタグを仕分ける」を通していたので、押すたびに<b>その回のトレンドが新しい未知語を
/// 連れてきて</b>、仕分け待ちが尽きなかった。仕分け側がトレンドを引かないので、
/// **押し続ければ仕分け待ちは空になる**(新しい語は収集と話題度の取り直しで増える)。
///
/// **2 つは同じ <see cref="JobRunner"/> の上に置く**(メソッドを 2 つにしただけ)。
/// 別のクラスにすると直列化の関門も別々になり、同時に走って互いの結果を上書きする ——
/// どちらも最後にタグの観測と語彙の組み立てをするため。
///
/// 「収集」ではなく「整備」なのは、材料の性質が分かれているから ——
/// **新しい語は記事などの収集で自然に溜まる**(語彙の問題。ここでは集めない)。
///
/// 手順(画面の説明とそろえること):
///
/// **話題度を取り直す**
/// 1. 外部トレンドを取得(<see cref="ITrendTopicSource"/>。Qiita のいいね・はてブのブックマーク数)
/// 2. タグを観測(件数 + 話題度を書き込む。**状態は触らない**)。
///    ここで<b>次の仕分けの対象が確定する</b>(画面に出す一覧と同じ)
/// 3. 語彙を組み立てて書き込む(件数と話題度の合算)
///
/// **タグを仕分けなおす**
/// 1. タグを作り直す(1 回目) —— 正規化の規則やカタログを変えたときに<b>そこで初めて現れる語</b>を
///    保存済みデータへ反映する
/// 2. 残骸のタグ・トピックを掃除する(いまの正規化では作られないキー)
/// 3. 仕分け待ちのタグを LLM で仕分け(<see cref="ITopicClassifier"/>)
/// 4. 同義のトピックを寄せる(<see cref="ITopicMergeAdvisor"/>)。
///    分類はキーの重複しか見ないので、`AI` と `人工知能` が別々に作られうる
/// 5. 説明の無いトピックに一言説明を付ける(<see cref="ITopicDescriber"/>)
/// 6. タグを作り直す(2 回目) —— 今回増えた別名を過去データへ反映する
/// 7. タグを観測。**外部へは出ず、いまある話題度をそのまま持ち回す** ——
///    空の話題度で観測すると、取ってあった話題度を 0 で上書きしてしまう
/// 8. 語彙を組み立てて書き込む
/// </summary>
public class TopicMaintenanceRunner(
    TopicCatalog catalog,
    IEnumerable<ITrendTopicSource> sources,
    SourceToggles toggles,
    ITagStore tagStore,
    ITopicStore topicStore,
    TagObserver tagObserver,
    TagRenormalizationRunner renormalizationRunner,
    TopicCatalogRefresher catalogRefresher,
    TopicMerger merger,
    ILogger<TopicMaintenanceRunner> logger,
    TimeProvider clock,
    LlmGateway llm) : JobRunner
{
    /// <summary>
    /// 1回の実行で LLM に渡すタグの上限(呼び出し回数の暴走を防ぐ枠)。
    /// 「1回の実行で LLM を何回呼ぶか」(60 語 × 5 バッチ)で決めている。
    /// </summary>
    public const int MaxTagsPerClassification = 300;

    /// <summary>1回の実行で一言説明を埋める語の上限。分類と同じ考え方。</summary>
    public const int MaxTermsPerDescription = 300;

    /// <summary>判断できなかったタグを再挑戦させるまでの日数。</summary>
    public const int UnresolvedRetryDays = 7;

    readonly IReadOnlyList<ITrendTopicSource> _sources = sources.ToList();

    int _failedSources;

    public override string Name => "トピックの整備";

    /// <summary>
    /// 仕分け(タグを仕分けなおす)のボタンに出す名前。**LLM を使う入口なので方式とモデルを出す**
    /// —— 要約・翻訳・サマリーと同じ扱いで、いまどの枠を消費するのかがボタンから分かるように。
    /// 話題度の取り直しは LLM を使わないので、そちらは名前を足さない。
    /// </summary>
    public string ClassificationName => $"タグを仕分けなおす({llm.Classifier?.Name ?? "未設定"})";

    // 語彙だけでも一覧は作れる(外部トレンドが無ければ話題度が 0 になるだけ)
    public override bool IsConfigured => true;

    /// <summary>
    /// **直前の実行で実際に LLM へ聞いたタグ。** 画面で「何が対象になったのか」を見せるために持つ
    /// (アプリを再起動すると消えるので、画面は DB からの復元も併せて使う)。
    /// </summary>
    public IReadOnlyList<string> LastClassificationTargets { get; private set; } = [];

    /// <summary>
    /// 話題度を取り直す(**LLM は使わない**)。外部トレンドを引いて鮮度を更新し、語彙へ反映する。
    ///
    /// **ここで仕分け待ちの語が増える。** トレンドに現れた未知の語がタグとして入るため ——
    /// 増えた語を語彙へ入れるのは <see cref="ReclassifyTagsAsync"/> の仕事。
    /// </summary>
    public Task<TrendRefreshResult> RefreshTrendsAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () => RefreshAsync(cancellationToken), TrendRefreshResult.Nothing, cancellationToken);

    /// <summary>
    /// 溜まったタグを LLM で仕分けて語彙へ入れる(**外部トレンドは引かない**)。
    ///
    /// **押しても新しい未知語は増えない**ので、仕分け待ちは押すぶんだけ減る ——
    /// 以前はトレンドの取得と同じジョブだったので、押すたびに新しい語が入って終わらなかった。
    /// </summary>
    public Task<TagClassificationResult> ReclassifyTagsAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () => ReclassifyAsync(cancellationToken), TagClassificationResult.Nothing, cancellationToken);

    async Task<TrendRefreshResult> RefreshAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        // **最初に語彙のスナップショットを DB からそろえる。** 画面からの手直しや別プロセスの
        // 変更が入っていることがあり、古いままだと別名の解決が食い違う
        await catalogRefresher.RefreshAsync(cancellationToken);

        Progress = "外部トレンドを取得中…";
        var trends = await FetchTrendsAsync(cancellationToken);

        // 3 種すべてを数え直すので、渡さなかったタグの件数は 0 に戻す(古い件数を残さない)。
        // **次の仕分けの対象がこの時点で確定する**(画面に出す一覧と同じ)
        Progress = "タグを観測中…";
        await tagObserver.ObserveAsync(trends, resetMissing: true, cancellationToken);

        Progress = "語彙を組み立て中…";
        var topics = await BuildTopicsAsync(now, cancellationToken);
        await catalogRefresher.RefreshAsync(cancellationToken);

        return new TrendRefreshResult(topics, trends.Count, _failedSources);
    }

    async Task<TagClassificationResult> ReclassifyAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        await catalogRefresher.RefreshAsync(cancellationToken);

        // 仕分けの前にタグを作り直す —— 正規化の規則を変えたときにそこで初めて現れる語を、
        // その回の対象に含めるため(DB だけの処理で数秒・冪等なので前後 2 回でよい)
        Progress = "タグを作り直し中…";
        await renormalizationRunner.RunOnceAsync(cancellationToken);

        // **残骸のタグを掃除する。** 正規化の規則を変えると、以前のキーのタグ
        // (`#生成ai`・`生成ai,`)が残る。中身は正しいタグに合流済みなので消して構わない
        await RemoveStaleAsync(cancellationToken);

        var (asked, classified) = await ClassifyPendingAsync(now, cancellationToken);
        var merged = await MergeDuplicatesAsync(cancellationToken);
        var described = await DescribeMissingAsync(now, cancellationToken);

        // 2 回目のタグの作り直し。今回の仕分けで増えた別名を過去データへ反映する
        Progress = "タグを再正規化中…";
        await renormalizationRunner.RunOnceAsync(cancellationToken);

        // **いまある話題度をそのまま持ち回して観測する。** 空のまま観測すると、
        // 取ってあった話題度を 0 で上書きしてしまう(観測は件数と話題度を同時に書く)
        Progress = "タグを観測中…";
        await tagObserver.ObserveAsync(
            await CurrentTrendsAsync(cancellationToken), resetMissing: true, cancellationToken);

        Progress = "語彙を組み立て中…";
        var topics = await BuildTopicsAsync(now, cancellationToken);
        await catalogRefresher.RefreshAsync(cancellationToken);

        return new TagClassificationResult(topics, asked, classified, merged, described);
    }

    /// <summary>
    /// DB にいま入っている話題度。**外部へは出ない** —— 仕分け側の観測で書き戻すために読む。
    /// </summary>
    async Task<Dictionary<string, (double Score, int Sources)>> CurrentTrendsAsync(
        CancellationToken cancellationToken) =>
        (await tagStore.GetAllAsync(cancellationToken))
            .Where(tag => tag.TrendScore > 0)
            .ToDictionary(
                tag => tag.Key, tag => (tag.TrendScore, tag.SourceCount), StringComparer.Ordinal);

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
    /// **失敗しても後の手順は続ける**(仕分けは次の実行でやり直せるが、語彙が
    /// 組み立てられないと選択まで狂う)。
    /// </summary>
    async Task<(int Asked, int Effective)> ClassifyPendingAsync(
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var classifier = llm.Classifier;
        if (classifier is null)
        {
            // 前回の値が残らないように空にする(画面が「前回聞いた語」に使う)
            LastClassificationTargets = [];

            return (0, 0);
        }

        try
        {
            var pending = (await tagStore.GetPendingAsync(now, cancellationToken))
                .Take(MaxTagsPerClassification)
                .Select(tag => tag.Key)
                .ToList();

            LastClassificationTargets = pending;
            if (pending.Count == 0)
            {
                return (0, 0);
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
                + "(除外 {Skipped} 件・保留 {Unresolved} 件・新トピック {New} 件)",
                classifier.Name, pending.Count, effective,
                accepted.Decisions.Count(d => d.Status == TagStatus.NotTopic),
                accepted.Decisions.Count(d => d.Status == TagStatus.Unresolved),
                accepted.NewTopics.Count);

            return (pending.Count, effective);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "タグの仕分けに失敗(後の手順は続ける)");

            return (LastClassificationTargets.Count, 0);
        }
    }

    /// <summary>
    /// 語彙の中の同義トピックを LLM に見つけさせて寄せる。寄せた件数を返す。
    ///
    /// **分類だけでは重複を防げない。** 検証はキーの重複しか見ないので、あるバッチが
    /// `AI` を、別のバッチが `人工知能` を新トピックとして作りうる。ここで後から寄せる
    /// 手当てがあるので、語彙を空から始められる(初期値の JSON を捨てられる)。
    ///
    /// **LLM の応答はそのまま信じない** —— 寄せ先が実在するか、自分自身でないか、
    /// 寄せ先が寄せ元を指していないか(相互参照)を確かめてから適用する。
    /// </summary>
    async Task<int> MergeDuplicatesAsync(CancellationToken cancellationToken)
    {
        var mergeAdvisor = llm.MergeAdvisor;
        if (mergeAdvisor is null)
        {
            return 0;
        }

        try
        {
            var topics = catalog.Entries;
            if (topics.Count < 2)
            {
                return 0;
            }

            var verdicts = await mergeAdvisor.SuggestMergesAsync(
                topics,
                step => Progress = $"同義のトピックを探索中: {step}…",
                cancellationToken);

            // 番号 → 寄せ元、表記 → 寄せ先。**相互参照は両方捨てる**
            // (A→B と B→A が来たとき、順に適用すると語彙が 1 つに潰れる)
            var pairs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var verdict in verdicts)
            {
                if (verdict.Index < 1 || verdict.Index > topics.Count)
                {
                    continue;
                }

                var from = topics[verdict.Index - 1].Key;
                var into = catalog.Resolve(verdict.Into);
                if (from != into && catalog.IsTopic(into))
                {
                    pairs[from] = into;
                }
            }

            var merged = 0;
            foreach (var (from, into) in pairs)
            {
                if (pairs.TryGetValue(into, out var back) && back == from)
                {
                    logger.LogInformation("相互に寄せ合っているので見送る: {From} ⇄ {Into}", from, into);
                    continue;
                }

                // 連鎖(A→B→C)は最後まで辿ってから寄せる。途中の行は先に消えている
                var target = into;
                var guard = 0;
                while (pairs.TryGetValue(target, out var next) && next != target && guard++ < 10)
                {
                    target = next;
                }

                if (target != from && await merger.MergeAsync(from, target, DecidedBy.Llm, cancellationToken))
                {
                    merged++;
                }
            }

            if (merged > 0)
            {
                logger.LogInformation(
                    "{Advisor} の判定で同義のトピック {Merged} 件を寄せた", mergeAdvisor.Name, merged);
            }

            return merged;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "同義トピックの統合に失敗(後の手順は続ける)");
            return 0;
        }
    }

    /// <summary>
    /// 説明がまだ無いトピックに一言説明を付ける。**1 語につき 1 回だけ聞く**
    /// (結果は列に残るので、次の仕分けでは聞かない)。上限で切れる分は、
    /// 収集対象に選んだトピック → 話題度の高い順で先に埋める。
    /// </summary>
    async Task<int> DescribeMissingAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var describer = llm.Describer;
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
            logger.LogError(ex, "用語の説明に失敗(後の手順は続ける)");
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

        // **止めたトレンドの収集元は叩きに行かない**(実行のたびに読む)
        foreach (var source in toggles.Enabled(_sources, SourceToggles.Trend, source => source.Name))
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

/// <summary>話題度を取り直した結果。</summary>
/// <param name="Count">語彙に載ったトピックの数。</param>
/// <param name="Trending">話題度が付いた(外部トレンドに現れた)タグの数。</param>
/// <param name="FailedSources">取得に失敗した収集元の数。</param>
public record TrendRefreshResult(int Count, int Trending, int FailedSources)
{
    public static readonly TrendRefreshResult Nothing = new(0, 0, 0);
}

/// <summary>タグを仕分けなおした結果。</summary>
/// <param name="Count">語彙に載ったトピックの数。</param>
/// <param name="Asked">今回 LLM に聞いたタグの数。</param>
/// <param name="Classified">そのうち語彙へ入った(昇格・別名)タグの数。</param>
/// <param name="Merged">今回 LLM の判定で寄せた同義トピックの数。</param>
/// <param name="Described">今回 LLM が一言説明を付けた用語の数。</param>
public record TagClassificationResult(
    int Count, int Asked, int Classified, int Merged, int Described)
{
    public static readonly TagClassificationResult Nothing = new(0, 0, 0, 0, 0);
}

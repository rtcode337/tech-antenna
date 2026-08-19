using Microsoft.Extensions.Options;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;

namespace TechAntenna.Web.Services;

/// <summary>サマリー1本分の結果。</summary>
/// <param name="Scope">守備範囲(全体 / 興味トピック)。</param>
/// <param name="Items">生成した項目数。</param>
/// <param name="Notified">通知した先の数。</param>
/// <param name="NotifyFailed">通知に失敗した先の数(生成自体は成功のまま)。</param>
/// <param name="Generators">書かせた AI の数(メイン + サブ)。1 なら文言に出さない。</param>
public record DigestPart(
    DigestScope Scope, int Items, int Notified, int NotifyFailed, int Generators = 1);

/// <summary>メインの AI がその範囲を書けなかったこと。</summary>
/// <param name="Scope">書けなかった範囲(全体 / 興味トピック)。</param>
/// <param name="Generator">書けなかった相手の名前。どの AI が落ちたかを文言に出すため。</param>
/// <param name="Reason">理由(例外のメッセージ。HTTP 502 など)。</param>
/// <param name="Substituted">
/// サブの AI が書いたもので代替できたか。代替できても失敗は失敗として扱うが、
/// 「何も残っていない」のか「別の相手のものが残っている」のかで打つ手が違うので分ける。
/// </param>
public record DigestFailure(DigestScope Scope, string Generator, string Reason, bool Substituted);

/// <summary>ダイジェストを1回実行した結果(全体・興味トピックの最大2本)。</summary>
/// <param name="Parts">生成できたぶんだけ入る。材料が無い範囲は入らない。</param>
/// <param name="Failures">
/// メインの AI が書けなかった範囲。空でなければジョブは失敗(<see cref="DigestPrimaryFailedException"/>)
/// —— ただし投げるのは保存と通知を済ませた後なので、書けたぶんは残る。
/// </param>
public record DigestRunResult(
    IReadOnlyList<DigestPart> Parts, IReadOnlyList<DigestFailure> Failures)
{
    public static readonly DigestRunResult Nothing = new([], []);

    public bool Composed => Parts.Count > 0;

    public int Items => Parts.Sum(part => part.Items);

    public int Notified => Parts.Sum(part => part.Notified);

    public int NotifyFailed => Parts.Sum(part => part.NotifyFailed);

    /// <summary>ボタンの隣に出す結果の文言。範囲ごとに出す ——
    /// 合計だけだと、興味トピック側が作られなかったことに気づけない。</summary>
    public string Describe() =>
        Failures.Count > 0 ? DescribeFailure()
        : Composed ? DescribeComposed()
        : "材料がありません。先にトレンドの収集(と、あれば興味トピック側の収集)を実行してください。";

    string DescribeComposed() =>
        $"今日のサマリーを生成しました({PartsText()})。{NotifyText()}";

    /// <summary>作れた範囲の並び。成功の文言と失敗の文言で同じものを使う(表記をずらさない)。</summary>
    string PartsText() =>
        string.Join("・", Parts.Select(part => $"{part.Scope.Label()} {part.Items} 項目"
            + (part.Generators > 1 ? $"(AI {part.Generators} 本)" : "")));

    string NotifyText() =>
        (Notified > 0 ? $" ntfy へ {Notified} 通 通知しました。" : "")
        + (NotifyFailed > 0 ? " 通知に失敗しました(詳細はログ)。" : "");

    /// <summary>
    /// メインが書けなかったときの文言。そのまま例外のメッセージになる(画面には
    /// 「失敗: 」を付けて出る)ので、何が落ちて何が残ったかを1文で言い切る ——
    /// 「失敗」とだけ出ると、サブの記録が残っていることにも、片方の範囲は作れたことにも
    /// 気づけない。
    /// </summary>
    string DescribeFailure() =>
        "メインの AI が書けませんでした("
        + string.Join("・", Failures.Select(f => $"{f.Scope.Label()}: {f.Generator} — {f.Reason}"))
        + ")。"
        + (Failures.Any(f => f.Substituted)
            ? " その範囲はサブの AI が書いたもので代替して保存しています。" : "")
        + (Composed ? $" 保存できたぶん: {PartsText()}。{NotifyText()}" : "");
}

/// <summary>
/// メインの AI がダイジェストを書けなかったときに投げる。
///
/// 保存と通知を済ませてから投げる。サブが書けていればその記録は残したうえで、
/// ジョブとしては失敗にしたい —— メインが欠けた日を成功として扱うと、
/// 「今日はメインが書けなかった」ことが画面にもログにも残らない。
/// </summary>
public class DigestPrimaryFailedException(string message) : Exception(message);

/// <summary>
/// 「今日のサマリー」を生成する。1回の実行で2本作る —— 技術界隈全体
/// (話題度上位。トピックの選択に依らない)と、興味トピック(選んだトピック配下に
/// 当たる記事・これからのイベント)。トピックが1つも選ばれていなければ後者は作らない。
///
/// 材料の選別はここでやる —— 全量を LLM に渡すとトークンを浪費するうえ、
/// 選別の基準は LLM ではなくデータ側の知識(話題度・選択)だから。
///
/// <b>失敗の扱いは3段に分けてある。</b>
/// <list type="number">
/// <item><b>サブの AI が落ちた</b> —— 何も起きない(比較のための1本が欠けるだけ)。</item>
/// <item><b>メインの AI が落ちた</b> —— <b>その範囲だけ</b>で、もう一方の範囲は作りに行く。
/// 書けたサブがあれば保存し、先頭をメインに繰り上げて通知まで済ませる。
/// そのうえで<b>ジョブとしては失敗</b>にする(<see cref="DigestPrimaryFailedException"/>)——
/// メインが欠けた日を成功として扱うと、画面にもログにも残らない。</item>
/// <item><b>通知先が落ちた</b> —— 生成は成功のまま(<see cref="NotifyAsync"/>)。</item>
/// </list>
/// </summary>
public class DigestRunner(
    LlmGateway llm,
    IEnumerable<IDigestNotifier> notifiers,
    IArticleStore articleStore,
    IEventStore eventStore,
    ITopicStore topicStore,
    IDigestStore digestStore,
    TopicCatalog catalog,
    IOptions<DigestOptions> options,
    TimeProvider clock,
    ILogger<DigestRunner> logger) : JobRunner
{
    readonly IReadOnlyList<IDigestNotifier> _notifiers = notifiers.ToList();

    public override string Name => $"今日のサマリーの生成({llm.DigestComposer?.Name ?? "未設定"})";

    public override bool IsConfigured => llm.IsConfigured;

    public override string? NotConfiguredReason => LlmGateway.NotConfiguredReason;

    public Task<DigestRunResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var composer = llm.DigestComposer;
        return composer is null
            ? Task.FromResult(DigestRunResult.Nothing)
            : RunExclusiveAsync(() => ComposeAllAsync(composer, cancellationToken),
                DigestRunResult.Nothing, cancellationToken);
    }

    async Task<DigestRunResult> ComposeAllAsync(
        IDigestComposer composer, CancellationToken cancellationToken)
    {
        var composed = new List<Digest>();
        var generatorCounts = new Dictionary<DigestScope, int>();
        var failures = new List<DigestFailure>();

        // 全体 → 興味トピックの順に作る。片方の材料が無くても、もう片方は作る ——
        // トレンドの収集だけ済んでいる日と、興味トピック側だけ動いた日のどちらもあるため。
        // 生成が先で通知は後(下の NotifyAsync)—— 順序を決める理由が両者で違う
        foreach (var materials in await CollectMaterialsAsync(cancellationToken))
        {
            if (materials.IsEmpty)
            {
                continue;
            }

            // 範囲どうしは独立。ここで例外を投げ返すと、先に作る「全体」が失敗した日は
            // 「興味トピック」を試すことすらできない(以前はそうなっていた)。
            // メインの失敗は failures に貯めて、全部試した後でジョブごと失敗させる
            var (digests, failure) = await ComposeWithAllAsync(composer, materials, cancellationToken);
            if (failure is not null)
            {
                failures.Add(failure);
            }

            if (digests.Count == 0)
            {
                continue;
            }

            generatorCounts[materials.Scope] = digests.Count;
            // 通知とホームの既定に使うのはメイン。保存はサブの分も含めて全部
            composed.Add(digests[0]);
        }

        var result = composed.Count == 0
            ? new DigestRunResult([], failures)
            : await NotifyAllAsync(composed, generatorCounts, failures, cancellationToken);

        // 保存も通知も済ませてから失敗させる。メインが欠けた回を成功として扱うと、
        // 「今日はメインが書けなかった」ことが画面にもログにも残らない
        if (failures.Count > 0)
        {
            throw new DigestPrimaryFailedException(result.Describe());
        }

        return result;
    }

    /// <summary>
    /// 作れたぶんを通知して結果にまとめる。
    ///
    /// 通知は生成と逆順に送る。ntfy のアプリは新着が上に並ぶので、最後に送ったものが
    /// 一番上に出る —— 技術界隈全体を先頭に置きたいので、興味トピック → 全体 の順に送る。
    /// 生成の順は変えない(全体を先に作る)。
    /// </summary>
    async Task<DigestRunResult> NotifyAllAsync(
        IReadOnlyList<Digest> composed,
        IReadOnlyDictionary<DigestScope, int> generatorCounts,
        IReadOnlyList<DigestFailure> failures,
        CancellationToken cancellationToken)
    {
        var notifications = new Dictionary<DigestScope, (int Notified, int Failed)>();
        foreach (var digest in Enumerable.Reverse(composed))
        {
            notifications[digest.Scope] = await NotifyAsync(digest, cancellationToken);
        }

        // 画面の文言は生成順(全体 → 興味トピック)のまま —— 読む順は通知の都合とは別
        return new DigestRunResult(
            composed
                .Select(digest => new DigestPart(
                    digest.Scope,
                    digest.Items.Count,
                    notifications[digest.Scope].Notified,
                    notifications[digest.Scope].Failed,
                    generatorCounts.GetValueOrDefault(digest.Scope, 1)))
                .ToList(),
            failures);
    }

    /// <summary>
    /// 同じ材料を選ばれた AI 全部に同時に書かせて保存する(戻り値の先頭がメイン)。
    ///
    /// 同時に投げるのは、比べる相手を同じ材料・同じ回にそろえるため。順に走らせると
    /// 後ろの相手ほど時間が空き、途中で収集が走れば材料まで変わる。
    ///
    /// 誰が失敗しても、書けた相手のぶんは必ず保存する。サブが落ちてもメインは残るし、
    /// メインが落ちてもサブは残る —— 以前は <c>Task.WhenAll</c> がメインの例外で
    /// 待ち合わせごと投げていたため、同じ回に書けていたサブまで捨てていた
    /// (下の「繰り上げ」に辿り着けず、読み比べ用の相手がいちばん要る日に消えていた)。
    /// メインが落ちたことは戻り値の <see cref="DigestFailure"/> で呼び出し側へ伝える。
    /// </summary>
    async Task<(IReadOnlyList<Digest> Digests, DigestFailure? Failure)> ComposeWithAllAsync(
        IDigestComposer composer, DigestMaterials materials, CancellationToken cancellationToken)
    {
        var scope = materials.Scope;
        // 相手を選べない経路(同梱の CLI / Anthropic API)では 1 本だけ
        var generators = llm.DigestGenerators.Count > 0
            ? llm.DigestGenerators
            : [new DigestGenerator(LlmGateway.DefaultGeneratorKey, composer.Name, true, composer)];

        Progress = generators.Count > 1
            ? $"LLM でダイジェストを生成中…({scope.Label()}・AI {generators.Count} 本)"
            : $"LLM でダイジェストを生成中…({scope.Label()})";

        // 回の識別子は先に決める。保存の順ではなく「同じ回で作った束」で寄せたいので、
        // 生成の成否に関わらず同じ値を全員に配る
        var runId = Guid.NewGuid();
        var attempts = await Task.WhenAll(generators.Select(generator =>
            ComposeOneAsync(generator, materials, runId, cancellationToken)));

        var digests = attempts.Select(attempt => attempt.Digest).OfType<Digest>().ToList();

        // メインが失敗していたら、残ったサブの先頭をメインに繰り上げる ——
        // 通知もホームも「1本目」を使うので、空にすると何も出なくなる。
        // 繰り上げは保存より先に。保存の後に立てても DB には残らない
        // (通知の署名は書いた相手の名前のままなので、読む側は誰が書いたか分かる)
        if (digests.Count > 0 && !digests[0].IsPrimary)
        {
            digests[0].IsPrimary = true;
        }

        foreach (var digest in digests)
        {
            await digestStore.SaveAsync(digest, cancellationToken);
        }

        var primary = attempts.FirstOrDefault(attempt => attempt.Generator.IsPrimary);
        var failure = primary?.Error is { } error
            ? new DigestFailure(scope, primary.Generator.Name, error, digests.Count > 0)
            : null;

        return (digests, failure);
    }

    /// <summary>1 本ぶんの生成の顛末。失敗も値で返す(投げ返さない)。</summary>
    /// <param name="Generator">頼んだ相手。メインだったかと、名前を文言に出すために持つ。</param>
    /// <param name="Digest">書けたもの。失敗したら null。</param>
    /// <param name="Error">失敗の理由(例外のメッセージ)。書けたら null。</param>
    record ComposeAttempt(DigestGenerator Generator, Digest? Digest, string? Error);

    /// <summary>
    /// 1 本ぶん。誰が失敗してもここでは投げ返さない ——
    /// <c>Task.WhenAll</c> は 1 本の例外で待ち合わせごと投げるので、
    /// 投げると同時に走らせた他の相手の結果まで受け取れなくなる。
    /// メインだったかどうかは呼び出し側が見て、保存と通知の後にジョブを失敗させる。
    /// </summary>
    async Task<ComposeAttempt> ComposeOneAsync(
        DigestGenerator generator,
        DigestMaterials materials,
        Guid runId,
        CancellationToken cancellationToken)
    {
        try
        {
            var digest = await generator.Composer.ComposeAsync(materials, cancellationToken);
            digest.RunId = runId;
            digest.GeneratorKey = generator.Key;
            digest.IsPrimary = generator.IsPrimary;

            logger.LogInformation(
                "{Composer}: ダイジェストを生成({Scope}・{Items} 項目)",
                generator.Name, materials.Scope, digest.Items.Count);

            return new ComposeAttempt(generator, digest, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // メインの失敗はログも重く出す。サブは比較のための1本が欠けるだけだが、
            // メインが欠けた日はその範囲の読み物が別の相手のものに変わっている
            logger.Log(
                generator.IsPrimary ? LogLevel.Error : LogLevel.Warning, ex,
                "{Composer} でのダイジェスト生成に失敗({Scope})。この相手ぶんは飛ばす",
                generator.Name, materials.Scope);

            return new ComposeAttempt(generator, null, ex.Message);
        }
    }

    /// <summary>
    /// 1本ぶんを通知する。通知は保存の後・失敗しても生成は成功のまま ——
    /// 通知先(ntfy)が落ちていることと、ダイジェストが作れたことは別の話。
    /// 失敗はログと結果の文言に出す。
    /// 範囲ごとに1通ずつ送る(まとめて1通にしない —— 読み分けられなくなる)。
    /// </summary>
    async Task<(int Notified, int Failed)> NotifyAsync(
        Digest digest, CancellationToken cancellationToken)
    {
        var scope = digest.Scope;
        var notified = 0;
        var failed = 0;

        foreach (var notifier in _notifiers)
        {
            try
            {
                Progress = $"{notifier.Name} へ通知中…({scope.Label()})";
                // 未設定・無効の通知先は false を返す(送っていないので数えない)
                if (await notifier.NotifyAsync(digest, cancellationToken))
                {
                    notified++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogError(ex, "{Notifier} への通知に失敗({Scope})", notifier.Name, scope);
            }
        }

        return (notified, failed);
    }

    /// <summary>
    /// 2本ぶんの材料を集める。興味トピックが1つも選ばれていなければ、その1本は作らない
    /// (返す配列に入れない)—— 材料が空なら結局書けないうえ、「興味トピックのサマリーが
    /// 空です」という枠だけがホームに残ることになるため。
    /// </summary>
    async Task<IReadOnlyList<DigestMaterials>> CollectMaterialsAsync(
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();
        var threshold = now.AddHours(-settings.WindowHours);

        // 選んだトピックは表記(LLM に読ませる)とキーの配下込み集合(突き合わせ)の両方を使う
        var selected = await topicStore.GetSelectedAsync(cancellationToken);
        var interestKeys = catalog
            .ExpandWithDescendants(selected.Select(topic => topic.Key))
            .ToHashSet(StringComparer.Ordinal);

        // 直近の話題: トレンド(/recent)と同じ規則 —— 種別ごとに窓で切り、
        // 話題度(はてブ数と upvote の大きいほう)の高い順に数件ずつ
        var perKind = Math.Max(1, settings.TrendingCount / 3);
        var trending = new List<Article>();
        foreach (var kind in new[] { ArticleKind.News, ArticleKind.Article, ArticleKind.TrendingPaper })
        {
            trending.AddRange((await articleStore.GetRecentAsync(perKind * 5, kind, cancellationToken))
                .Where(a => (a.PublishedAt ?? a.CollectedAt) >= threshold)
                .OrderByDescending(a => Math.Max(a.BookmarkCount ?? 0, a.UpvoteCount ?? 0))
                .ThenByDescending(a => a.PublishedAt ?? a.CollectedAt)
                .Take(perKind));
        }

        var overall = new DigestMaterials(DigestScope.Overall, trending, [], []);
        if (interestKeys.Count == 0)
        {
            return [overall];
        }

        // 興味トピックに当たる直近の記事。話題度上位と重なっていても外さない ——
        // 別々のサマリーに渡すので、全体で触れた記事が興味トピック側にも要ることがある
        // (むしろ「自分の関心でも大きい話」なので落とすほうが不自然)
        var interest = (await articleStore.GetRecentAsync(100, null, cancellationToken))
            .Where(a => (a.PublishedAt ?? a.CollectedAt) >= threshold)
            .Where(a => a.Tags.Any(interestKeys.Contains))
            .Take(settings.InterestCount)
            .ToList();

        // これからのイベント(興味トピックのもの)。/events と同じ絞り
        var horizon = now.AddDays(settings.EventWindowDays);
        var events = (await eventStore.GetUpcomingAsync(now, 50, cancellationToken))
            .Where(e => e.StartsAt <= horizon)
            .Where(e => e.Tags.Any(interestKeys.Contains))
            .Take(settings.EventCount)
            .ToList();

        return
        [
            overall,
            new DigestMaterials(
                DigestScope.Interests,
                interest,
                events,
                selected.Select(topic => topic.Display).ToList()),
        ];
    }
}

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
public record DigestPart(DigestScope Scope, int Items, int Notified, int NotifyFailed);

/// <summary>ダイジェストを1回実行した結果(全体・興味トピックの最大2本)。</summary>
/// <param name="Parts">生成できたぶんだけ入る。材料が無い範囲は入らない。</param>
public record DigestRunResult(IReadOnlyList<DigestPart> Parts)
{
    public static readonly DigestRunResult Nothing = new([]);

    public bool Composed => Parts.Count > 0;

    public int Items => Parts.Sum(part => part.Items);

    public int Notified => Parts.Sum(part => part.Notified);

    public int NotifyFailed => Parts.Sum(part => part.NotifyFailed);

    /// <summary>ボタンの隣に出す結果の文言。**範囲ごとに出す** ——
    /// 合計だけだと、興味トピック側が作られなかったことに気づけない。</summary>
    public string Describe() => Composed
        ? "今日のサマリーを生成しました("
            + string.Join("・", Parts.Select(part => $"{part.Scope.Label()} {part.Items} 項目"))
            + ")。"
            + (Notified > 0 ? $" ntfy へ {Notified} 通 通知しました。" : "")
            + (NotifyFailed > 0 ? " 通知に失敗しました(詳細はログ)。" : "")
        : "材料がありません。先にトレンドの収集(と、あれば興味トピック側の収集)を実行してください。";
}

/// <summary>
/// 「今日のサマリー」を生成する。**1回の実行で2本作る** —— 技術界隈全体
/// (話題度上位。トピックの選択に依らない)と、興味トピック(選んだトピック配下に
/// 当たる記事・これからのイベント)。**トピックが1つも選ばれていなければ後者は作らない**。
///
/// **材料の選別はここでやる** —— 全量を LLM に渡すとトークンを浪費するうえ、
/// 選別の基準は LLM ではなくデータ側の知識(話題度・選択)だから。
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

        // 全体 → 興味トピックの順に作る。**片方の材料が無くても、もう片方は作る** ——
        // トレンドの収集だけ済んでいる日と、興味トピック側だけ動いた日のどちらもあるため。
        // **生成が先で通知は後**(下の NotifyAsync)—— 順序を決める理由が両者で違う
        foreach (var materials in await CollectMaterialsAsync(cancellationToken))
        {
            if (materials.IsEmpty)
            {
                continue;
            }

            composed.Add(await ComposeOneAsync(composer, materials, cancellationToken));
        }

        if (composed.Count == 0)
        {
            return DigestRunResult.Nothing;
        }

        // **通知は生成と逆順に送る。** ntfy のアプリは新着が上に並ぶので、**最後に送ったものが
        // 一番上に出る** —— 技術界隈全体を先頭に置きたいので、興味トピック → 全体 の順に送る。
        // 生成の順は変えない(全体を先に作るので、途中で失敗しても価値の大きいほうが残る)
        var notifications = new Dictionary<DigestScope, (int Notified, int Failed)>();
        foreach (var digest in Enumerable.Reverse(composed))
        {
            notifications[digest.Scope] = await NotifyAsync(digest, cancellationToken);
        }

        // 画面の文言は生成順(全体 → 興味トピック)のまま —— 読む順は通知の都合とは別
        return new DigestRunResult(composed
            .Select(digest => new DigestPart(
                digest.Scope,
                digest.Items.Count,
                notifications[digest.Scope].Notified,
                notifications[digest.Scope].Failed))
            .ToList());
    }

    async Task<Digest> ComposeOneAsync(
        IDigestComposer composer, DigestMaterials materials, CancellationToken cancellationToken)
    {
        var scope = materials.Scope;

        Progress = $"LLM でダイジェストを生成中…({scope.Label()})";
        var digest = await composer.ComposeAsync(materials, cancellationToken);
        await digestStore.SaveAsync(digest, cancellationToken);

        logger.LogInformation(
            "{Composer}: ダイジェストを生成({Scope}・{Items} 項目)",
            composer.Name, scope, digest.Items.Count);

        return digest;
    }

    /// <summary>
    /// 1本ぶんを通知する。**通知は保存の後・失敗しても生成は成功のまま** ——
    /// 通知先(ntfy)が落ちていることと、ダイジェストが作れたことは別の話。
    /// 失敗はログと結果の文言に出す。
    /// **範囲ごとに1通ずつ送る**(まとめて1通にしない —— 読み分けられなくなる)。
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
    /// 2本ぶんの材料を集める。**興味トピックが1つも選ばれていなければ、その1本は作らない**
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

        // 興味トピックに当たる直近の記事。**話題度上位と重なっていても外さない** ——
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

using Microsoft.Extensions.Options;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;

namespace TechAntenna.Web.Services;

/// <summary>ダイジェストを1回生成した結果。</summary>
/// <param name="Composed">生成したか(材料が無ければ false)。</param>
/// <param name="Items">生成した項目数。</param>
/// <param name="Notified">通知した先の数。</param>
/// <param name="NotifyFailed">通知に失敗した先の数(生成自体は成功のまま)。</param>
public record DigestRunResult(bool Composed, int Items, int Notified = 0, int NotifyFailed = 0)
{
    public static readonly DigestRunResult Nothing = new(false, 0);

    /// <summary>ボタンの隣に出す結果の文言。</summary>
    public string Describe() => Composed
        ? $"今日のサマリーを生成しました({Items} 項目)。"
            + (Notified > 0 ? " ntfy へ通知しました。" : "")
            + (NotifyFailed > 0 ? " 通知に失敗しました(詳細はログ)。" : "")
        : "材料がありません。先にトレンドの収集(と、あれば興味トピック側の収集)を実行してください。";
}

/// <summary>
/// 「今日のサマリー」を1件生成する。**材料の選別はここでやる** —— 直近の話題
/// (話題度上位)・興味トピック(配下込み)に当たる記事・これからのイベントを
/// 数件ずつに絞って LLM に渡す。全量を渡すとトークンを浪費するうえ、
/// 選別の基準は LLM ではなくデータ側の知識(話題度・選択)だから。
/// </summary>
public class DigestRunner(
    IEnumerable<IDigestComposer> composers,
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
    readonly IDigestComposer? _composer = composers.FirstOrDefault();
    readonly IReadOnlyList<IDigestNotifier> _notifiers = notifiers.ToList();

    public override string Name => $"今日のサマリーの生成({_composer?.Name ?? "未設定"})";

    public override bool IsConfigured => _composer is not null;

    public Task<DigestRunResult> RunOnceAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => ComposeAsync(_composer!, cancellationToken),
            DigestRunResult.Nothing, cancellationToken);

    async Task<DigestRunResult> ComposeAsync(
        IDigestComposer composer, CancellationToken cancellationToken)
    {
        var materials = await CollectMaterialsAsync(cancellationToken);
        if (materials.IsEmpty)
        {
            return DigestRunResult.Nothing;
        }

        Progress = "LLM でダイジェストを生成中…";
        var digest = await composer.ComposeAsync(materials, cancellationToken);
        await digestStore.SaveAsync(digest, cancellationToken);

        logger.LogInformation(
            "{Composer}: ダイジェストを生成({Items} 項目)", composer.Name, digest.Items.Count);

        // 通知は保存の後・失敗しても生成は成功のまま —— 通知先(ntfy)が落ちていることと、
        // ダイジェストが作れたことは別の話。失敗はログと結果の文言に出す
        var notified = 0;
        var notifyFailed = 0;
        foreach (var notifier in _notifiers)
        {
            try
            {
                Progress = $"{notifier.Name} へ通知中…";
                await notifier.NotifyAsync(digest, cancellationToken);
                notified++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                notifyFailed++;
                logger.LogError(ex, "{Notifier} への通知に失敗", notifier.Name);
            }
        }

        return new DigestRunResult(true, digest.Items.Count, notified, notifyFailed);
    }

    async Task<DigestMaterials> CollectMaterialsAsync(CancellationToken cancellationToken)
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

        // 興味トピックに当たる直近の記事。話題度上位と重複した分は外す
        // (同じ記事を2つの節で渡すと、LLM が2項目に割りがちになる)
        var trendingUrls = trending.Select(a => a.Url).ToHashSet();
        var interest = interestKeys.Count == 0
            ? []
            : (await articleStore.GetRecentAsync(100, null, cancellationToken))
                .Where(a => (a.PublishedAt ?? a.CollectedAt) >= threshold)
                .Where(a => !trendingUrls.Contains(a.Url))
                .Where(a => a.Tags.Any(interestKeys.Contains))
                .Take(settings.InterestCount)
                .ToList();

        // これからのイベント(興味トピックのもの)。/events と同じ絞り
        var horizon = now.AddDays(settings.EventWindowDays);
        var events = interestKeys.Count == 0
            ? []
            : (await eventStore.GetUpcomingAsync(now, 50, cancellationToken))
                .Where(e => e.StartsAt <= horizon)
                .Where(e => e.Tags.Any(interestKeys.Contains))
                .Take(settings.EventCount)
                .ToList();

        return new DigestMaterials(
            trending,
            interest,
            events,
            selected.Select(topic => topic.Display).ToList());
    }
}

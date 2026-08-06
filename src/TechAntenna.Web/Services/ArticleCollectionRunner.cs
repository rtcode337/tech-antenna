using Microsoft.Extensions.Options;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Infrastructure.Feeds;

namespace TechAntenna.Web.Services;

/// <summary>
/// 登録された記事ソースを1巡し、ストアへ保存する。
///
/// **選択トピックで絞らず、流れてきた記事はすべて保存する。** 以前は選択したタグを含むものだけ
/// 残していたが、それだと「選んだトピックの外で何が起きているか」が画面に一切出てこない。
/// イベントや書籍と違って RSS は検索ではなく巡回なので、絞っても収集先への負荷は変わらない
/// —— 捨てる意味がほとんど無い。選択トピックは<b>表示側での強調</b>にだけ使う(`/articles`)。
/// </summary>
public class ArticleCollectionRunner(
    IEnumerable<IArticleSource> sources,
    IArticleStore store,
    IOptions<CollectionOptions> options,
    TimeProvider clock,
    ILogger<ArticleCollectionRunner> logger,
    HatenaBookmarkCounts? bookmarkCounts = null) : JobRunner
{
    readonly IReadOnlyList<IArticleSource> _sources = sources.ToList();

    public override string Name => "記事の収集";

    public override bool IsConfigured => _sources.Count > 0;

    public Task<CollectionRunResult> RunOnceAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => CollectAsync(cancellationToken),
            CollectionRunResult.Nothing, cancellationToken);

    async Task<CollectionRunResult> CollectAsync(CancellationToken cancellationToken)
    {
        // 収集先へ同時アクセスしないよう、並列化せず1本ずつ間隔を空けて読む
        var delay = TimeSpan.FromSeconds(options.Value.DelayBetweenSourcesSeconds);
        int fetched = 0, added = 0, failed = 0;

        for (var i = 0; i < _sources.Count; i++)
        {
            var source = _sources[i];
            try
            {
                var articles = await source.FetchAsync(cancellationToken);
                var newlyAdded = await store.AddRangeAsync(articles, cancellationToken);
                fetched += articles.Count;
                added += newlyAdded;
                logger.LogInformation(
                    "{Source}: {Fetched} 件取得、うち {Added} 件を新規追加",
                    source.Name, articles.Count, newlyAdded);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 1ソースの失敗で巡回全体を止めない
                failed++;
                logger.LogError(ex, "{Source} の収集に失敗", source.Name);
            }

            // 最後の収集元の後は待たない(手動実行で無駄に待たせないため)
            if (i < _sources.Count - 1 && delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        await RefreshBookmarkCountsAsync(cancellationToken);

        return new CollectionRunResult(fetched, added, failed);
    }

    /// <summary>直近の記事・ニュースを何件までブックマーク数の補完対象にするか(50 件 = 1 リクエスト)。</summary>
    const int BookmarkRefreshLimit = 200;

    /// <summary>ブックマーク数の補完対象にする公開からの日数。古い記事は数字がほぼ動かない。</summary>
    const int BookmarkRefreshDays = 7;

    /// <summary>
    /// 直近の記事・ニュースのブックマーク数を、はてなブックマークの件数 API で引き直す。
    /// はてブの RSS 由来は収集時点の値を持っているが、それ以外のソースは数値を持たないし、
    /// 件数は時間とともに増えるので、収集のたびにまとめて更新する。
    /// **論文は対象外** —— はてブにほぼ載らず、リクエストを増やすだけになる。
    /// 失敗しても収集の結果は返す(人気の指標が古いだけで、記事自体は揃っているため)。
    /// </summary>
    async Task RefreshBookmarkCountsAsync(CancellationToken cancellationToken)
    {
        if (bookmarkCounts is null)
        {
            return;
        }

        try
        {
            var threshold = clock.GetUtcNow().AddDays(-BookmarkRefreshDays);
            var targets = new List<Article>();
            foreach (var kind in (ArticleKind[])[ArticleKind.Article, ArticleKind.News])
            {
                targets.AddRange((await store.GetRecentAsync(BookmarkRefreshLimit, kind, cancellationToken))
                    .Where(a => (a.PublishedAt ?? a.CollectedAt) >= threshold));
            }

            if (targets.Count == 0)
            {
                return;
            }

            var counts = await bookmarkCounts.FetchAsync(
                targets.Select(a => a.Url).ToList(), cancellationToken);
            var updates = targets
                .Where(a => counts.ContainsKey(a.Url.ToString()))
                .Select(a => (a.Id, counts[a.Url.ToString()]))
                .ToList();

            var updated = await store.UpdateBookmarkCountsAsync(updates, cancellationToken);
            logger.LogInformation(
                "はてなブックマーク件数: {Targets} 件を照会し、{Updated} 件を更新", targets.Count, updated);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "はてなブックマーク件数の取得に失敗");
        }
    }
}

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
    SourceToggles toggles,
    IArticleStore store,
    TagObserver tagObserver,
    IOptions<CollectionOptions> options,
    ILogger<ArticleCollectionRunner> logger,
    BookmarkCountRefresher? bookmarkRefresher = null) : JobRunner
{
    readonly IReadOnlyList<IArticleSource> _sources = sources.ToList();

    // かつては「記事の収集」。ニュース・話題の論文・ブックマーク数の補完まで含むので、
    // 軸の名前(トレンド)で呼ぶ —— 「記事」だけだと集まる範囲が実態より狭く読める
    public override string Name => "トレンドの収集";

    public override bool IsConfigured => _sources.Count > 0;

    public Task<CollectionRunResult> RunOnceAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => CollectAsync(cancellationToken),
            CollectionRunResult.Nothing, cancellationToken);

    async Task<CollectionRunResult> CollectAsync(CancellationToken cancellationToken)
    {
        // **止めた収集元は叩きに行かない。** 実行のたびに読むので、画面の切り替えは
        // 再起動なしで効く(起動時に絞ると、切り替えても次の再起動まで変わらない)
        var enabled = toggles.Enabled(_sources, SourceToggles.Article, source => source.Name);
        if (enabled.Count == 0)
        {
            return CollectionRunResult.AllDisabled("トレンド");
        }

        // 収集先へ同時アクセスしないよう、並列化せず1本ずつ間隔を空けて読む
        var delay = TimeSpan.FromSeconds(options.Value.DelayBetweenSourcesSeconds);
        int fetched = 0, added = 0, failed = 0;

        for (var i = 0; i < enabled.Count; i++)
        {
            var source = enabled[i];
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
            if (i < enabled.Count - 1 && delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        await RefreshBookmarkCountsAsync(cancellationToken);

        // **見つけたタグをタグの一覧へ反映する。** これが無いと、集めたのにタグの画面が
        // 変わらない(仕分け待ちの語が増えず、次の整備まで何も起きないように見える)。
        // 状態は触らないので、仕分け済みの語が巻き戻ることはない
        Progress = "タグを反映中…";
        await tagObserver.ObserveAsync(cancellationToken: cancellationToken);

        return new CollectionRunResult(fetched, added, failed);
    }

    /// <summary>
    /// 直近の記事・ニュースのブックマーク数を、はてなブックマークの件数 API で引き直す
    /// (中身は <see cref="BookmarkCountRefresher"/>。トピック収集の話題度とも共用)。
    /// はてブの RSS 由来は収集時点の値を持っているが、それ以外のソースは数値を持たないし、
    /// 件数は時間とともに増えるので、収集のたびにまとめて更新する。
    /// 失敗しても収集の結果は返す(人気の指標が古いだけで、記事自体は揃っているため)。
    /// </summary>
    async Task RefreshBookmarkCountsAsync(CancellationToken cancellationToken)
    {
        // ブックマーク数の補完も1つの収集元として止められる(止めたら叩きに行かない)
        if (!toggles.IsEnabled(SourceToggles.Bookmark, BookmarkCountRefresher.SourceName))
        {
            return;
        }

        if (bookmarkRefresher is null)
        {
            return;
        }

        try
        {
            var updated = await bookmarkRefresher.RefreshAsync(cancellationToken);
            logger.LogInformation("はてなブックマーク件数: {Updated} 件を更新", updated);
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

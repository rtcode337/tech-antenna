using Microsoft.Extensions.Options;
using TechAntenna.Core.Abstractions;

namespace TechAntenna.Web.Services;

/// <summary>
/// 論文の収集元(arXiv・J-STAGE)を1巡し、ストアへ保存する。
///
/// **記事の収集と分けてある。** 記事の RSS は巡回だが、論文は<b>検索</b>なので
/// 収集対象に選んだトピックが検索語として要る —— 同じボタンにまとめていたときは
/// 「記事は集まったのに論文だけ 0 件」の理由が画面から分からなかった
/// (選択が空だと 1 件も取りに行かない。イベント・書籍と同じ性質)。
/// </summary>
public class PaperCollectionRunner(
    IEnumerable<IPaperSource> sources,
    IArticleStore store,
    ITopicStore topicStore,
    TagObserver tagObserver,
    IOptions<CollectionOptions> options,
    ILogger<PaperCollectionRunner> logger) : JobRunner
{
    readonly IReadOnlyList<IPaperSource> _sources = sources.ToList();

    public override string Name => "論文の収集";

    public override bool IsConfigured => _sources.Count > 0;

    public Task<CollectionRunResult> RunOnceAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () => CollectAsync(cancellationToken), CollectionRunResult.Nothing, cancellationToken);

    async Task<CollectionRunResult> CollectAsync(CancellationToken cancellationToken)
    {
        // 収集対象が空なら何も取りに行かない。**理由を文言にする** ——
        // 0 件の結果だけ返すと、設定の問題なのか本当に無いのか分からない
        if ((await topicStore.GetSelectedAsync(cancellationToken)).Count == 0)
        {
            throw new InvalidOperationException(
                "収集対象のトピックが選ばれていません（論文は選んだトピックを検索語にします）。");
        }

        var delay = TimeSpan.FromSeconds(options.Value.DelayBetweenSourcesSeconds);
        int fetched = 0, added = 0, failed = 0;

        for (var i = 0; i < _sources.Count; i++)
        {
            var source = _sources[i];
            Progress = $"{source.Name} から取得中…";
            try
            {
                var articles = await source.FetchAsync(cancellationToken);
                fetched += articles.Count;
                added += await store.AddRangeAsync(articles, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 1 つの収集元が落ちても他は回す
                failed++;
                logger.LogError(ex, "{Source} の収集に失敗", source.Name);
            }

            // 最後の収集元の後は待たない(手動実行で無駄に待たせないため)
            if (i < _sources.Count - 1 && delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        // 見つけたタグをタグの一覧へ反映する(状態は触らない)
        Progress = "タグを反映中…";
        await tagObserver.ObserveAsync(cancellationToken: cancellationToken);

        return new CollectionRunResult(fetched, added, failed);
    }
}

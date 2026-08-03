using Microsoft.Extensions.Options;
using TechAntenna.Core.Abstractions;

namespace TechAntenna.Web.Services;

/// <summary>登録された記事ソースを1巡し、ストアへ保存する。</summary>
public class ArticleCollectionRunner(
    IEnumerable<IArticleSource> sources,
    IArticleStore store,
    ITopicStore topicStore,
    IOptions<CollectionOptions> options,
    ILogger<ArticleCollectionRunner> logger) : JobRunner
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
        // RSS は検索できないので、集めたものを選択トピックで絞る。
        // 比べる相手は記事の正規化済みタグなので、**キーのほう**を使う
        var selectedTags = (await topicStore.GetSelectedAsync(cancellationToken))
            .Select(topic => topic.Tag).ToList();
        if (selectedTags.Count == 0)
        {
            return CollectionRunResult.Nothing;
        }
        int fetched = 0, added = 0, failed = 0;

        for (var i = 0; i < _sources.Count; i++)
        {
            var source = _sources[i];
            try
            {
                var articles = await source.FetchAsync(cancellationToken);
                articles = articles.Where(article => article.Tags.Intersect(selectedTags, StringComparer.Ordinal).Any()).ToList();
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

        return new CollectionRunResult(fetched, added, failed);
    }
}

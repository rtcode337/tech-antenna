using Microsoft.Extensions.Options;
using TechAntenna.Core.Abstractions;

namespace TechAntenna.Web.Workers;

/// <summary>登録された記事ソースを定期的に巡回し、ストアへ保存する。</summary>
public class ArticleCollectionWorker(
    IEnumerable<IArticleSource> sources,
    IArticleStore store,
    IOptions<CollectionOptions> options,
    ILogger<ArticleCollectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(options.Value.IntervalMinutes);
        using var timer = new PeriodicTimer(interval);

        do
        {
            await CollectOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    async Task CollectOnceAsync(CancellationToken cancellationToken)
    {
        // 収集先へ同時アクセスしないよう、並列化せず1本ずつ間隔を空けて読む
        var delay = TimeSpan.FromSeconds(options.Value.DelayBetweenSourcesSeconds);

        foreach (var source in sources)
        {
            try
            {
                var articles = await source.FetchAsync(cancellationToken);
                var added = await store.AddRangeAsync(articles, cancellationToken);
                logger.LogInformation(
                    "{Source}: {Fetched} 件取得、うち {Added} 件を新規追加",
                    source.Name, articles.Count, added);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 1ソースの失敗で巡回全体を止めない
                logger.LogError(ex, "{Source} の収集に失敗", source.Name);
            }

            await Task.Delay(delay, cancellationToken);
        }
    }
}

using Microsoft.Extensions.Options;
using TechAntenna.Core.Abstractions;

namespace TechAntenna.Web.Workers;

/// <summary>要約が未生成の記事を定期的に取り出し、LLM で要約して保存する。</summary>
public class SummaryWorker(
    ISummarizer summarizer,
    IArticleStore store,
    IOptions<AnthropicOptions> options,
    ILogger<SummaryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(options.Value.IntervalMinutes);
        using var timer = new PeriodicTimer(interval);

        do
        {
            await SummarizeBatchAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    async Task SummarizeBatchAsync(CancellationToken cancellationToken)
    {
        var articles = await store.GetUnsummarizedAsync(options.Value.BatchSize, cancellationToken);

        foreach (var article in articles)
        {
            try
            {
                var summary = await summarizer.SummarizeAsync(article, cancellationToken);
                if (summary is null)
                {
                    // 材料不足で要約できない記事に毎回挑まないよう、空の要約として確定する
                    await store.UpdateSummaryAsync(article.Id, "", cancellationToken);
                    continue;
                }

                await store.UpdateSummaryAsync(article.Id, summary, cancellationToken);
                logger.LogInformation("要約を生成: {Title}", article.Title);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 1件の失敗でバッチ全体を止めない(次回の実行で再試行される)
                logger.LogError(ex, "要約の生成に失敗: {Title}", article.Title);
            }

            // API への連続リクエストを避けるための間隔
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }
}

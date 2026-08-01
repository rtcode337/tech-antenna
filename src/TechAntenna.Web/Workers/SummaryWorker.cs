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
        if (articles.Count == 0)
        {
            return;
        }

        try
        {
            // 記事はまとめて渡す。Claude Code 版は呼び出し1回の固定費が大きく、
            // 1件ずつ投げると同じハーネスを何度も入力に積むことになるため
            var results = await summarizer.SummarizeAsync(articles, cancellationToken);

            foreach (var result in results)
            {
                // 材料不足で要約できない記事に毎回挑まないよう、空の要約として確定する
                await store.UpdateSummaryAsync(
                    result.ArticleId, result.Summary ?? "", cancellationToken);
            }

            logger.LogInformation(
                "{Summarizer}: {Total} 件中 {Summarized} 件を要約",
                summarizer.Name, articles.Count, results.Count(r => r.Summary is not null));

            // 結果に含まれなかった記事は未処理のまま。次の巡回で再試行される
            if (results.Count < articles.Count)
            {
                logger.LogWarning(
                    "{Skipped} 件は結果に含まれず、次回に持ち越し", articles.Count - results.Count);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // バッチの失敗で巡回そのものを止めない(次回の実行で再試行される)
            logger.LogError(ex, "{Summarizer} による要約に失敗", summarizer.Name);
        }
    }
}

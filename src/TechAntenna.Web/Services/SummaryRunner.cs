using Microsoft.Extensions.Options;
using TechAntenna.Core.Abstractions;

namespace TechAntenna.Web.Services;

/// <summary>要約を1バッチ分だけ実行した結果。</summary>
/// <param name="Requested">要約を試みた記事数。</param>
/// <param name="Summarized">実際に要約が生成できた件数。</param>
/// <param name="Skipped">結果に含まれず次回へ持ち越した件数。</param>
public record SummaryRunResult(int Requested, int Summarized, int Skipped)
{
    public static readonly SummaryRunResult Nothing = new(0, 0, 0);
}

/// <summary>未要約の記事を1バッチ分だけ要約する。</summary>
public class SummaryRunner(
    IEnumerable<ISummarizer> summarizers,
    IArticleStore store,
    IOptions<AnthropicOptions> options,
    ILogger<SummaryRunner> logger) : JobRunner
{
    readonly ISummarizer? _summarizer = summarizers.FirstOrDefault();

    public override string Name => $"記事の要約({_summarizer?.Name ?? "未設定"})";

    public override bool IsConfigured => _summarizer is not null;

    public Task<SummaryRunResult> RunOnceAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => SummarizeBatchAsync(_summarizer!, cancellationToken),
            SummaryRunResult.Nothing, cancellationToken);

    async Task<SummaryRunResult> SummarizeBatchAsync(
        ISummarizer summarizer, CancellationToken cancellationToken)
    {
        var articles = await store.GetUnsummarizedAsync(options.Value.BatchSize, cancellationToken);
        if (articles.Count == 0)
        {
            return SummaryRunResult.Nothing;
        }

        // 記事はまとめて渡す。Claude Code 版は呼び出し1回の固定費が大きく、
        // 1件ずつ投げると同じハーネスを何度も入力に積むことになるため
        var results = await summarizer.SummarizeAsync(articles, cancellationToken);

        foreach (var result in results)
        {
            // 材料不足で要約できない記事に毎回挑まないよう、空の要約として確定する
            await store.UpdateSummaryAsync(
                result.ArticleId, result.Summary ?? "", cancellationToken);
        }

        var summarized = results.Count(r => r.Summary is not null);
        logger.LogInformation(
            "{Summarizer}: {Total} 件中 {Summarized} 件を要約",
            summarizer.Name, articles.Count, summarized);

        // 結果に含まれなかった記事は未処理のまま。次の実行で再試行される
        var skipped = articles.Count - results.Count;
        if (skipped > 0)
        {
            logger.LogWarning("{Skipped} 件は結果に含まれず、次回に持ち越し", skipped);
        }

        return new SummaryRunResult(articles.Count, summarized, skipped);
    }
}

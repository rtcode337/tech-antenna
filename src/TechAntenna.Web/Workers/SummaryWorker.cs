using Microsoft.Extensions.Options;
using TechAntenna.Web.Services;

namespace TechAntenna.Web.Workers;

/// <summary>
/// 記事の要約を定期的に実行する。
///
/// **登録されるのは <c>Anthropic:AutoRun</c> が true のときだけ**。開発環境では
/// appsettings.Development.json で false にしてある —— 開発サーバーを消し忘れると、
/// 気づかないうちに収集先を叩き続けたり LLM の枠を使い続けたりするため。
/// 手動では画面のボタンから走らせる。
/// </summary>
public class SummaryWorker(
    SummaryRunner runner,
    IOptions<AnthropicOptions> options,
    ILogger<SummaryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(options.Value.IntervalMinutes);
        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                await runner.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 1巡の失敗で以降の巡回を止めない
                logger.LogError(ex, "{Job} に失敗", runner.Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

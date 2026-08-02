using Microsoft.Extensions.Options;
using TechAntenna.Web.Services;

namespace TechAntenna.Web.Workers;

/// <summary>
/// 書籍の収集を定期的に実行する。
///
/// **登録されるのは <c>Books:AutoRun</c> が true のときだけで、既定は false**
/// —— 消し忘れたサーバーが気づかないうちに収集先を叩き続けたり、LLM の枠を
/// 使い続けたりするため。既定では画面のボタンを押したときだけ走る。
/// </summary>
public class BookCollectionWorker(
    BookCollectionRunner runner,
    IOptions<BooksOptions> options,
    ILogger<BookCollectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(options.Value.IntervalHours);
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

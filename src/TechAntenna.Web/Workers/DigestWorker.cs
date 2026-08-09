using Microsoft.Extensions.Options;
using TechAntenna.Web.Services;

namespace TechAntenna.Web.Workers;

/// <summary>
/// 今日のサマリー(ダイジェスト)を定期的に生成する。
///
/// **登録されるのは <c>Digest:AutoRun</c> が true のときだけで、既定は false**
/// (理由は <see cref="SummaryWorker"/> と同じ)。既定では設定画面のボタンを
/// 押したときだけ生成する。間隔の既定は 12 時間 = 1日2回。
/// </summary>
public class DigestWorker(
    DigestRunner runner,
    IOptions<DigestOptions> options,
    ILogger<DigestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromHours(options.Value.IntervalHours);
        using var timer = new PeriodicTimer(interval);

        // do-while なので起動直後にも1回生成する(他の Worker と同じ流儀。
        // 最初のタイマーまで12時間待つと「機能が動いていない」ように見える)
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
                // 1回の失敗で以降の生成を止めない
                logger.LogError(ex, "{Job} に失敗", runner.Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

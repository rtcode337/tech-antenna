using System.Globalization;
using TechAntenna.Core;
using TechAntenna.Web.Services;

namespace TechAntenna.Web.Workers;

/// <summary>
/// 定期実行のワーカー。**アプリ全体で1本**で、設定した時刻になったら
/// <see cref="ScheduleRunner"/> を1回走らせる(中身 —— チェックの入ったジョブを
/// 決まった順で通す —— はそちら。画面の「定期実行を今すぐ実行」と同じ経路)。
///
/// かつてはジョブごとに BackgroundService を持ち、それぞれ「N 分ごと」で回していた。
/// 順序の保証が無いので、サマリーがその日の収集より先に走ることがあった。
///
/// - **ワーカーは常に登録する。** 設定は周回ごとに読むので、画面での切り替えは
///   再起動なしで次の周回から効く
/// - **判定は1分ごと**(タイマー1本の維持コストは無視できる)。時刻の分解能も分なので、
///   これより細かく見ても意味が無い
/// - **止まっていたあいだに時刻を跨いでいたら、起動後の最初の周回で1回だけ走る** ——
///   何回跨いでいても1回(まとめて取り返しても集まるものは同じで、外部を余計に叩くだけ)
/// - **前回の実行時刻は DB に持つ**(<see cref="ScheduleSettings.LastRunName"/>)——
///   メモリに置くと再起動のたびに「跨いだか」が分からなくなり、起動のたびに走ってしまう
/// </summary>
public class ScheduleWorker(
    ScheduleRunner runner,
    ApiCredentials credentials,
    TimeProvider clock,
    ILogger<ScheduleWorker> logger) : BackgroundService
{
    /// <summary>設定を見に行く間隔。時刻の分解能(分)より細かくしても意味が無い。</summary>
    static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);

        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 1周の失敗で以降の周回を止めない
                logger.LogError(ex, "定期実行の判定に失敗");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    async Task TickAsync(CancellationToken cancellationToken)
    {
        var times = ScheduleSettings.GetTimes(credentials);
        if (times.Count == 0)
        {
            // 時刻が未設定 = 定期実行しない。**印も残さない** —— 残すと、あとで時刻を
            // 設定したときに「前回」が古いままになり、設定した直後に走ってしまう
            return;
        }

        var now = clock.GetUtcNow();
        var lastRun = ReadLastRun();
        if (lastRun is null)
        {
            // 初めて時刻を設定したとき。**ここでは走らせず、今を起点にする** ——
            // 走らせると「今日のぶんはもう過ぎている」時刻の設定が、保存した瞬間に発火する
            await MarkRanAsync(now, cancellationToken);
            return;
        }

        if (!ScheduleSettings.IsDue(times, lastRun.Value, now))
        {
            return;
        }

        // **印を先に進める。** ジョブの列は数十分かかることがあり、そのあいだの周回で
        // もう一度「跨いだ」と判定されると二重に走り出す
        await MarkRanAsync(now, cancellationToken);

        // 結果の文言は Runner に残る(画面のボタンで押したときと同じ)
        await runner.RunAndRecordAsync(
            ScheduleRunner.OperationName, runner.RunOnceAsync, JobMessage.Describe, cancellationToken);
    }

    DateTimeOffset? ReadLastRun() =>
        DateTimeOffset.TryParse(
            credentials.Get(ScheduleSettings.LastRunName),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var value)
            ? value
            : null;

    Task MarkRanAsync(DateTimeOffset at, CancellationToken cancellationToken) =>
        credentials.SetAsync(
            ScheduleSettings.LastRunName,
            // 保存は往復できる形(UTC)。人が読むところだけ JapanTime を通す
            at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            cancellationToken);
}

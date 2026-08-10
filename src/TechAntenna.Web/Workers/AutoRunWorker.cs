using TechAntenna.Web.Services;

namespace TechAntenna.Web.Workers;

/// <summary>
/// 定期実行の共通ループ。**ワーカーは常に登録され、周回ごとに画面設定
/// (<see cref="AutoRunSettings"/>)を見て実行するか決める** —— かつては AutoRun の
/// 環境変数を見て登録自体を分岐していたが、それだと画面から切り替えても再起動するまで
/// 効かない。無効の周回はタイマーを待つだけ(タイマー1本の維持コストは無視できる)。
///
/// 有効なら起動直後にも1回走る(タイマーの初回発火を待たない)——
/// 定期実行を頼みにしている環境で、再起動のたびに1周期ぶん空白ができないようにするため。
/// </summary>
public abstract class AutoRunWorker(
    ApiCredentials credentials,
    ILogger logger) : BackgroundService
{
    /// <summary>オン/オフの設定キー(AutoRunSettings の定数)。</summary>
    protected abstract string SettingName { get; }

    /// <summary>ログに出すジョブ名。</summary>
    protected abstract string JobName { get; }

    /// <summary>周回の間隔。</summary>
    protected abstract TimeSpan Interval { get; }

    /// <summary>1回分の実行(Runner の RunOnceAsync を呼ぶ)。</summary>
    protected abstract Task RunOnceAsync(CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            if (!AutoRunSettings.IsEnabled(credentials, SettingName))
            {
                continue;
            }

            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 1巡の失敗で以降の巡回を止めない
                logger.LogError(ex, "{Job} に失敗", JobName);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

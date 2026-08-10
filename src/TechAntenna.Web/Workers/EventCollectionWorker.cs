using Microsoft.Extensions.Options;
using TechAntenna.Web.Services;

namespace TechAntenna.Web.Workers;

/// <summary>
/// イベントの収集を定期的に実行する。**オン/オフは画面(設定)の定期実行チェックで切り替える**
/// (既定は無効)。ループと判定は <see cref="AutoRunWorker"/>。
/// </summary>
public class EventCollectionWorker(
    EventCollectionRunner runner,
    ApiCredentials credentials,
    IOptions<CollectionOptions> options,
    ILogger<EventCollectionWorker> logger) : AutoRunWorker(credentials, logger)
{
    // 記事と同じ「収集」のくくりで、1つのチェックでまとめて切り替える
    protected override string SettingName => AutoRunSettings.CollectionName;

    protected override string JobName => runner.Name;

    protected override TimeSpan Interval => TimeSpan.FromMinutes(options.Value.IntervalMinutes);

    protected override Task RunOnceAsync(CancellationToken cancellationToken) =>
        runner.RunOnceAsync(cancellationToken);
}

using Microsoft.Extensions.Options;
using TechAntenna.Web.Services;

namespace TechAntenna.Web.Workers;

/// <summary>
/// トレンドの収集を定期的に実行する。**オン/オフは画面(設定)の定期実行チェックで切り替える**
/// (既定は無効)。ループと判定は <see cref="AutoRunWorker"/>。
/// </summary>
public class ArticleCollectionWorker(
    ArticleCollectionRunner runner,
    ApiCredentials credentials,
    IOptions<CollectionOptions> options,
    ILogger<ArticleCollectionWorker> logger) : AutoRunWorker(credentials, logger)
{
    protected override string SettingName => AutoRunSettings.CollectionName;

    protected override string JobName => runner.Name;

    protected override TimeSpan Interval => TimeSpan.FromMinutes(options.Value.IntervalMinutes);

    protected override Task RunOnceAsync(CancellationToken cancellationToken) =>
        runner.RunOnceAsync(cancellationToken);
}

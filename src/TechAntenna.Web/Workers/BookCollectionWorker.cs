using Microsoft.Extensions.Options;
using TechAntenna.Web.Services;

namespace TechAntenna.Web.Workers;

/// <summary>
/// 書籍の収集を定期的に実行する。**オン/オフは画面(設定)の定期実行チェックで切り替える**
/// (既定は無効)。ループと判定は <see cref="AutoRunWorker"/>。
/// </summary>
public class BookCollectionWorker(
    BookCollectionRunner runner,
    ApiCredentials credentials,
    IOptions<BooksOptions> options,
    ILogger<BookCollectionWorker> logger) : AutoRunWorker(credentials, logger)
{
    protected override string SettingName => AutoRunSettings.BooksName;

    protected override string JobName => runner.Name;

    protected override TimeSpan Interval => TimeSpan.FromHours(options.Value.IntervalHours);

    protected override Task RunOnceAsync(CancellationToken cancellationToken) =>
        runner.RunOnceAsync(cancellationToken);
}

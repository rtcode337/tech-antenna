using Microsoft.Extensions.Options;
using TechAntenna.Web.Services;

namespace TechAntenna.Web.Workers;

/// <summary>
/// 今日のサマリーの生成を定期的に実行する。**オン/オフは画面(設定)の定期実行チェックで切り替える**
/// (既定は無効)。ループと判定は <see cref="AutoRunWorker"/>。
/// </summary>
public class DigestWorker(
    DigestRunner runner,
    ApiCredentials credentials,
    IOptions<DigestOptions> options,
    ILogger<DigestWorker> logger) : AutoRunWorker(credentials, logger)
{
    protected override string SettingName => AutoRunSettings.DigestName;

    protected override string JobName => runner.Name;

    protected override TimeSpan Interval => TimeSpan.FromHours(options.Value.IntervalHours);

    // キー未設定なら Runner が何もしない(チェックだけ入れても LLM は呼ばれない)
    protected override Task RunOnceAsync(CancellationToken cancellationToken) =>
        runner.RunOnceAsync(cancellationToken);
}

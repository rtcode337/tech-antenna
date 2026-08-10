namespace TechAntenna.Web.Services;

/// <summary>定期実行を1回通した結果。</summary>
/// <param name="Total">チェックの入っていたジョブ数。</param>
/// <param name="Ran">実際に走って成功した数。</param>
/// <param name="Failed">走ったが失敗した数(残りは止めずに続ける)。</param>
/// <param name="Skipped">設定が足りずに飛ばした数(LLM のキーが無い等)。</param>
public record ScheduleRunResult(int Total, int Ran, int Failed, int Skipped)
{
    public static readonly ScheduleRunResult Nothing = new(0, 0, 0, 0);
}

/// <summary>
/// **定期実行の中身**。チェックの入ったジョブを <see cref="ScheduledJobs.InOrder"/> の順に
/// 1つずつ通しで走らせる。
///
/// **時刻で走るのも、画面の「定期実行を今すぐ実行」も同じここを通る** ——
/// ワーカー(<c>Workers/ScheduleWorker</c>)は時刻の判定だけを持ち、中身はこの Runner。
/// 他のジョブと同じ <see cref="JobRunner"/> なので、進捗と結果の文言が同じ作りで画面に出る。
/// </summary>
public class ScheduleRunner(
    ScheduledJobs jobs,
    ApiCredentials credentials,
    ILogger<ScheduleRunner> logger) : JobRunner
{
    public override string Name => "定期実行を今すぐ実行";

    /// <summary>
    /// 常に押せる。**対象が0件でもボタンは生かしておく** —— disabled にすると
    /// 「なぜ押せないのか」を別に説明することになるので、押した結果として文言で返す。
    /// </summary>
    public override bool IsConfigured => true;

    public Task<ScheduleRunResult> RunOnceAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => ExecuteAsync(cancellationToken), ScheduleRunResult.Nothing, cancellationToken);

    async Task<ScheduleRunResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var enabled = jobs.InOrder
            .Where(job => ScheduleSettings.IsEnabled(credentials, job.Key))
            .ToList();
        if (enabled.Count == 0)
        {
            logger.LogInformation("定期実行の対象がありません(チェックが1つも入っていない)");
            return ScheduleRunResult.Nothing;
        }

        logger.LogInformation("定期実行を開始({Count} ジョブ)", enabled.Count);

        var ran = 0;
        var failed = 0;
        var skipped = 0;

        for (var i = 0; i < enabled.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var job = enabled[i];

            // 設定が足りない(LLM のキーが無い等)ジョブは飛ばす。手動ボタンと同じ扱いで、
            // 設定が入った次の回から動き出す
            if (!job.Runner.IsConfigured)
            {
                skipped++;
                logger.LogInformation(
                    "{Job} は設定が足りないので飛ばします: {Reason}",
                    job.Name, job.Runner.NotConfiguredReason ?? "(理由なし)");
                continue;
            }

            // 通しで走らせると数十分かかる。いまどこかを画面に出す
            // (各ジョブ自身の進捗は、そのジョブの行に出る)
            Progress = $"{i + 1}/{enabled.Count} {job.Name} を実行中…";
            logger.LogInformation("{Job} を実行します", job.Name);

            // **1つずつ待つ。** 後ろのジョブは前のジョブが集めたものを材料にする
            if (await job.RunAsync(cancellationToken))
            {
                ran++;
                logger.LogInformation("{Job}: {Message}", job.Name, job.Runner.LastMessage);
            }
            else
            {
                // 失敗しても次のジョブへ進む(収集元が1つ落ちているだけでサマリーまで止めない)
                failed++;
                logger.LogError("{Job} に失敗: {Error}", job.Name, job.Runner.LastError);
            }
        }

        logger.LogInformation("定期実行が終わりました");
        return new ScheduleRunResult(enabled.Count, ran, failed, skipped);
    }
}

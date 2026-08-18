using Microsoft.Extensions.Options;
using TechAntenna.Core;
using TechAntenna.Core.Abstractions;

namespace TechAntenna.Web.Services;

/// <summary>
/// 最近出た本(新刊・ムック)を集める。**読ませるためではなく数えるため**の収集で、
/// 集めたものは「出版されているテーマ」(<c>/recent</c> の節)の材料になる。
///
/// **トレンドの軸**なので収集対象の選択に依存しない —— 分類(NDC)と刊行日で引く。
/// 書籍の収集(<see cref="BookCollectionRunner"/>)とは別のジョブ・別の表:
/// あちらは選んだトピックを検索語にして「読んでおくべき本」を集める。
/// </summary>
public class NewReleaseCollectionRunner(
    IEnumerable<INewReleaseSource> sources,
    SourceToggles toggles,
    INewReleaseStore store,
    IOptions<NewReleaseOptions> options,
    TimeProvider clock,
    ILogger<NewReleaseCollectionRunner> logger) : JobRunner
{
    readonly IReadOnlyList<INewReleaseSource> _sources = sources.ToList();

    public override string Name => "出版トレンドの収集";

    public override bool IsConfigured => _sources.Count > 0;

    public override string? NotConfiguredReason =>
        "新刊の収集元が無効になっています(appsettings の NewReleases:Enabled)。";

    public Task<CollectionRunResult> RunOnceAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(
            () => CollectAsync(cancellationToken), CollectionRunResult.Nothing, cancellationToken);

    async Task<CollectionRunResult> CollectAsync(CancellationToken cancellationToken)
    {
        // **止めた収集元は叩きに行かない。** 実行のたびに読むので、画面の切り替えは
        // 再起動なしで効く(起動時に絞ると、切り替えても次の再起動まで変わらない)
        var enabled = toggles.Enabled(_sources, SourceToggles.NewRelease, source => source.Name);
        if (enabled.Count == 0)
        {
            return CollectionRunResult.AllDisabled("出版トレンド");
        }

        int found = 0, added = 0, failed = 0;
        // **窓は毎回同じ**(直近 N か月)。同じ本を引き直すことになるが、URL で上書きするので
        // 増えず、タグは最新の語彙で付け直される
        // 「直近 N か月」の境界も日本時間で数える(UTC の日付だと日本の朝 9 時までは前日)
        var since = DateOnly.FromDateTime(
            JapanTime.To(clock.GetUtcNow()).AddMonths(-options.Value.WindowMonths).Date);

        foreach (var source in enabled)
        {
            try
            {
                Progress = $"{source.Name} から {since:yyyy-MM} 以降の新刊を読んでいます…";
                var releases = await source.FetchAsync(since, cancellationToken);

                var newlyAdded = await store.AddRangeAsync(releases, cancellationToken);
                found += releases.Count;
                added += newlyAdded;
                logger.LogInformation(
                    "{Source}: {Found} 冊(うち新規 {Added} 冊)",
                    source.Name, releases.Count, newlyAdded);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 1つの収集元の失敗で全体を止めない
                failed++;
                logger.LogError(ex, "{Source} からの新刊の収集に失敗", source.Name);
            }
        }

        return new CollectionRunResult(found, added, failed);
    }
}

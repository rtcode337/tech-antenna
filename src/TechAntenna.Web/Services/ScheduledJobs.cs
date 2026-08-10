namespace TechAntenna.Web.Services;

/// <summary>
/// 設定画面のどの見出しの下に置くか。**実行順とは別**(画面はサイドバーと同じ並び、
/// 実行はデータの依存順)なので、行には順番の番号も出す。
/// </summary>
public enum JobGroup
{
    Home,
    Trend,
    Interests,
    Classics,
    Topics,
}

/// <summary>定期実行に並ぶジョブ1つ。画面のチェックボックスとワーカーの両方がこれを見る。</summary>
/// <param name="Key">設定キーの一部(<see cref="ScheduleSettings.EnabledName"/>)。**変えると設定が外れる**。</param>
/// <param name="Name">画面とログに出す名前(ボタンの文字)。</param>
/// <param name="Group">設定画面のどの見出しの下に出すか。</param>
/// <param name="Runner">実行中・未設定の判定に使う Runner(手動ボタンと同じもの)。</param>
/// <param name="RunAsync">1回分の実行。結果の文言は Runner に残り、成功なら true。</param>
public record ScheduledJob(
    string Key,
    string Name,
    JobGroup Group,
    JobRunner Runner,
    Func<CancellationToken, Task<bool>> RunAsync);

/// <summary>
/// 定期実行に並ぶジョブと**その順番**。順番はここ1か所だけが決める(画面もワーカーも
/// この並びをそのまま使うので、チェックの並びと実際に走る順が食い違わない)。
///
/// **順番には理由がある** —— 後ろのジョブは前のジョブが集めたものを材料にする:
///
/// <list type="number">
///   <item>収集(トレンド・論文・イベント・書籍・定番)—— 材料そのものを増やす</item>
///   <item>話題度を取り直す —— 集まった記事のタグを観測して、次の仕分けの対象を決める</item>
///   <item>タグを仕分けなおす —— そこで溜まった語を LLM が語彙へ入れる</item>
///   <item>記事の要約・論文タイトルの翻訳 —— 集まった記事に要約と訳題を付ける</item>
///   <item>今日のサマリー —— <b>要約と訳題まで揃ったもの</b>を材料にまとめる</item>
/// </list>
///
/// 逆順で走らせると、サマリーがその日の収集を含まない材料で作られる。
/// </summary>
public class ScheduledJobs(
    ArticleCollectionRunner trendCollection,
    PaperCollectionRunner paperCollection,
    EventCollectionRunner eventCollection,
    BookCollectionRunner bookCollection,
    ClassicsCollectionRunner classicsCollection,
    TopicMaintenanceRunner maintenance,
    SummaryRunner summary,
    TitleTranslationRunner translation,
    DigestRunner digest)
{
    /// <summary>
    /// 走る順に並んだジョブ。**キーは設定に保存されている**ので、増減はできても
    /// 既存のキーは変えないこと(変えるとその行のチェックが外れる)。
    ///
    /// **毎回組み直す**(フィールドに固定しない)—— LLM を使うジョブの名前には方式名が
    /// 入っていて(「記事の要約(Claude Code / …)」)、キーを画面から設定した直後に変わる。
    /// </summary>
    public IReadOnlyList<ScheduledJob> InOrder =>
    [
        new("trend-collection", trendCollection.Name, JobGroup.Trend, trendCollection,
            ct => trendCollection.RunAndRecordAsync(
                trendCollection.RunOnceAsync, JobMessage.Describe, ct)),

        new("paper-collection", paperCollection.Name, JobGroup.Interests, paperCollection,
            ct => paperCollection.RunAndRecordAsync(
                paperCollection.RunOnceAsync, JobMessage.Describe, ct)),

        new("event-collection", eventCollection.Name, JobGroup.Interests, eventCollection,
            ct => eventCollection.RunAndRecordAsync(
                eventCollection.RunOnceAsync, JobMessage.Describe, ct)),

        new("book-collection", bookCollection.Name, JobGroup.Interests, bookCollection,
            ct => bookCollection.RunAndRecordAsync(
                bookCollection.RunOnceAsync, JobMessage.Describe, ct)),

        new("classics-collection", classicsCollection.Name, JobGroup.Classics, classicsCollection,
            ct => classicsCollection.RunAndRecordAsync(
                classicsCollection.RunOnceAsync, JobMessage.Describe, ct)),

        // 話題度と仕分けは Runner が同じ1つ(「トピックの整備」)なので、名前はここで書き分ける
        new("trend-scores", "話題度を取り直す", JobGroup.Topics, maintenance,
            ct => maintenance.RunAndRecordAsync(
                maintenance.RefreshTrendsAsync, JobMessage.Describe, ct)),

        new("tag-classification", "タグを仕分けなおす", JobGroup.Topics, maintenance,
            ct => maintenance.RunAndRecordAsync(
                maintenance.ReclassifyTagsAsync, JobMessage.Describe, ct)),

        new("summary", summary.Name, JobGroup.Trend, summary,
            ct => summary.RunAndRecordAsync(summary.RunOnceAsync, JobMessage.Describe, ct)),

        new("translation", translation.Name, JobGroup.Trend, translation,
            ct => translation.RunAndRecordAsync(
                translation.RunOnceAsync,
                result => result.Requested == 0
                    ? "訳題の付いていない論文はありません。"
                    : $"{result.Requested} 件中 {result.Translated} 件に訳題を付けました。",
                ct)),

        new("digest", digest.Name, JobGroup.Home, digest,
            ct => digest.RunAndRecordAsync(
                digest.RunOnceAsync, result => result.Describe(), ct)),
    ];

    /// <summary>キーからジョブを引く(画面がボタンの value で押された行を見分けるのに使う)。</summary>
    public ScheduledJob? ByKey(string key) =>
        InOrder.FirstOrDefault(job => job.Key == key);

    /// <summary>実行順(1 始まり)。**画面の並びは実行順と違う**ので、行に番号を出すために要る。</summary>
    public int OrderOf(ScheduledJob job) =>
        InOrder.ToList().FindIndex(candidate => candidate.Key == job.Key) + 1;
}

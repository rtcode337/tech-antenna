using TechAntenna.Core.Topics;

namespace TechAntenna.Core.Abstractions;

/// <summary>タグ1語の観測結果(収集データに何回付いたか・外部トレンドでの話題度)。</summary>
/// <param name="Key">正規化済みタグ。</param>
/// <param name="ArticleCount">記事に付いている件数。</param>
/// <param name="EventCount">イベントに付いている件数。</param>
/// <param name="BookCount">書籍に付いている件数。</param>
/// <param name="TrendScore">外部トレンドで付いた話題度。</param>
/// <param name="SourceCount">話題度の集計元になったサービス数。</param>
public record TagObservation(
    string Key,
    int ArticleCount = 0,
    int EventCount = 0,
    int BookCount = 0,
    double TrendScore = 0,
    int SourceCount = 0);

/// <summary>タグ1語の仕分け結果。</summary>
/// <param name="Key">正規化済みタグ。</param>
/// <param name="Status">仕分けた状態。</param>
/// <param name="TopicKey">Promoted なら自分、Alias なら寄せ先。それ以外は null。</param>
/// <param name="DecidedBy">誰が決めたか。</param>
/// <param name="RetryAfter">Unresolved をもう一度聞いてよくなる時刻。</param>
public record TagDecision(
    string Key,
    TagStatus Status,
    string? TopicKey = null,
    DecidedBy DecidedBy = DecidedBy.Llm,
    DateTimeOffset? RetryAfter = null);

/// <summary>
/// 見かけたタグとその仕分け状態の保存先。
///
/// **観測(<see cref="ObserveAsync"/>)と仕分け(<see cref="DecideAsync"/>)を分けてある。**
/// 収集は件数と話題度を書き替えるだけで状態には触らず、状態を変えるのは再編成と
/// 画面からの手直しだけ —— 混ぜると、収集のたびに仕分けが巻き戻る。
/// </summary>
public interface ITagStore
{
    /// <summary>
    /// 観測結果を書き込む(無ければ <see cref="TagStatus.Pending"/> で作る)。
    /// **状態・寄せ先・判定日時は触らない。**
    /// <paramref name="resetMissing"/> が true なら、渡されなかったタグの件数と話題度を 0 にする
    /// (別名がまとまってタグが消えたときに古い件数が残らないようにするため)。
    /// </summary>
    Task ObserveAsync(
        IReadOnlyList<TagObservation> observations,
        DateTimeOffset seenAt,
        bool resetMissing = false,
        CancellationToken cancellationToken = default);

    /// <summary>仕分け結果を書き込む(無いタグは作らない)。</summary>
    Task DecideAsync(
        IReadOnlyList<TagDecision> decisions,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken = default);

    /// <summary>全件返す(画面の用語集と、語彙の組み立てに使う)。</summary>
    Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 次に LLM へ聞くタグを「目立つ順」(件数 + 話題度)に返す。**上限は掛けない** ——
    /// 1 回に何語聞くかは呼ぶ側の枠で決め、画面では枠に収まらない分も見せたいため。
    ///
    /// 対象は <see cref="TagStatus.Pending"/> と、再挑戦の時刻を過ぎた
    /// <see cref="TagStatus.Unresolved"/>。件数も話題度も無いタグは含めない
    /// (誰も使っていない語を聞いても枠を使うだけ)。
    /// </summary>
    Task<IReadOnlyList<Tag>> GetPendingAsync(
        DateTimeOffset now,
        int minCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// いまの正規化では作られないキーのタグを消す。正規化の規則を変えたときの残骸
    /// (`#生成ai`・`生成ai,` など)を掃除するために使う。実際に消した件数を返す。
    /// </summary>
    Task<int> RemoveAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default);
}

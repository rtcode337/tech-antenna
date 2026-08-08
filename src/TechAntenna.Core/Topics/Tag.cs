namespace TechAntenna.Core.Topics;

/// <summary>
/// タグの仕分け状態。**「見かけた語」から「語彙」への一方向の流れ**を表す。
/// </summary>
public enum TagStatus
{
    /// <summary>まだ仕分けていない。件数か話題度が付いていれば、次の仕分けで LLM に聞く。</summary>
    Pending = 0,

    /// <summary>トピックとして精査済み。<see cref="Tag.TopicKey"/> は自分自身。</summary>
    Promoted = 1,

    /// <summary>既存トピックの別表記として吸収した。件数は <see cref="Tag.TopicKey"/> へ合算する。</summary>
    Alias = 2,

    /// <summary>
    /// トピックとして扱わないと判定した(メディア名・一般語など)。語彙には入らず、
    /// LLM にも聞き直さない。**画面の見出しは「除外」**(「トピック外」だと無視する語だと読めない)。
    /// </summary>
    NotTopic = 3,

    /// <summary>
    /// LLM が判断できなかった。**期限付き** —— <see cref="Tag.RetryAfter"/> を過ぎたら
    /// もう一度聞く(新語は時間が経てば分類できるようになる)。
    /// </summary>
    Unresolved = 4,
}

/// <summary>その状態を誰が決めたか。画面で出どころを示すために持つ。</summary>
public enum DecidedBy
{
    /// <summary>まだ誰も決めていない。</summary>
    None = 0,

    /// <summary>機械的な正規化の規則(表記ゆれ・区切り・カンマ分割など)。</summary>
    Rule = 1,

    /// <summary>初期投入した語彙(`topic-seed.json`)。</summary>
    Seed = 2,

    /// <summary>LLM の分類。</summary>
    Llm = 3,

    /// <summary>画面から人が直した。**LLM より優先する** —— 誤判定を直せる経路として残す。</summary>
    Human = 4,
}

/// <summary>
/// 収集データで見かけたタグ1語と、その仕分け状態。
///
/// **記事・イベント・書籍のタグは、まずここに全部入る。** 語彙(<see cref="Topic"/>)は
/// ここから精査で昇格したものだけで、両者を分けているのは<b>別物だから</b> ——
/// 以前は同じテーブルに同居していて、状態を列にできず「行の有無 × カタログに載っているか ×
/// 分類記録の種別」から導出していた。
/// </summary>
public class Tag
{
    /// <summary>正規化済みのタグ(<see cref="TagNormalizer.ToKey"/> の結果)。主キー。</summary>
    public string Key { get; set; } = "";

    public TagStatus Status { get; set; }

    /// <summary>
    /// 対応するトピックのキー。<see cref="TagStatus.Promoted"/> なら自分自身、
    /// <see cref="TagStatus.Alias"/> なら寄せ先。それ以外は null。
    /// </summary>
    public string? TopicKey { get; set; }

    public DecidedBy DecidedBy { get; set; }

    /// <summary>状態を決めた時刻。未仕分けなら null。</summary>
    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>
    /// <see cref="TagStatus.Unresolved"/> をもう一度聞いてよくなる時刻。
    /// **期限を列に持つ**ことで、「7 日」の計算が読む側から消える。
    /// </summary>
    public DateTimeOffset? RetryAfter { get; set; }

    public int ArticleCount { get; set; }

    public int EventCount { get; set; }

    public int BookCount { get; set; }

    /// <summary>外部トレンドで付いた話題度(ソース内シェアの合算)。</summary>
    public double TrendScore { get; set; }

    /// <summary>話題度の集計元になったサービス数。</summary>
    public int SourceCount { get; set; }

    public DateTimeOffset FirstSeenAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>集めたデータに何回付いているか。</summary>
    public int TotalCount => ArticleCount + EventCount + BookCount;
}

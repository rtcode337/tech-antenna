namespace TechAntenna.Core.Topics;

/// <summary>収集時点の件数とともに保存したトピック。</summary>
public class StoredTopic
{
    /// <summary>突き合わせキー(正規化済み)。主キー。</summary>
    public string Tag { get; set; } = "";

    /// <summary>画面に出す正式表記。カタログに無いトピックは <see cref="Tag"/> と同じ。</summary>
    public string Display { get; set; } = "";

    /// <summary>1つ上の粒度のキー(`llm` の親は `生成ai`)。無ければ null。</summary>
    public string? Parent { get; set; }

    public int ArticleCount { get; set; }

    public int EventCount { get; set; }

    public int BookCount { get; set; }

    /// <summary>
    /// 外部トレンドでの話題度。**ソース内シェアを合算した値**(1ソースあたり最大 100)で、
    /// 生の件数ではない —— 収集元ごとに桁が違う(全期間の質問数と直近のいいね数など)ため、
    /// そのまま足すと桁の大きいほうが常に勝ってしまう。
    /// </summary>
    public double TrendScore { get; set; }

    /// <summary>話題度の集計元になったサービス数。</summary>
    public int SourceCount { get; set; }

    /// <summary>このアプリの収集キーワードとして選択されているか。</summary>
    public bool IsSelected { get; set; }

    public DateTimeOffset CollectedAt { get; set; }

    public int Coverage =>
        (ArticleCount > 0 ? 1 : 0) + (EventCount > 0 ? 1 : 0) + (BookCount > 0 ? 1 : 0);

    public int Total => ArticleCount + EventCount + BookCount;
}

/// <summary>トピック1件ぶんの更新内容(カタログの語彙 + 外部トレンドの話題度 + 自分の在庫)。</summary>
public record TopicUpdate(
    string Tag,
    string Display,
    string? Parent,
    double TrendScore,
    int SourceCount,
    int ArticleCount,
    int EventCount,
    int BookCount);

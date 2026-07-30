namespace TechAntenna.Web;

/// <summary>収集ジョブの設定。appsettings の Collection セクションから読む。</summary>
public class CollectionOptions
{
    public const string SectionName = "Collection";

    /// <summary>巡回間隔(分)。収集先への負荷を考えて短くしすぎない。</summary>
    public int IntervalMinutes { get; set; } = 30;

    /// <summary>1つの収集先を読んでから次に移るまでの待ち時間(秒)。</summary>
    public int DelayBetweenSourcesSeconds { get; set; } = 2;

    public List<FeedOptions> Feeds { get; set; } = [];
}

/// <summary>収集対象のフィード1本分の設定。</summary>
public class FeedOptions
{
    public string Name { get; set; } = "";

    public string Url { get; set; } = "";
}

/// <summary>connpass API の設定。appsettings の Connpass セクションから読む。</summary>
public class ConnpassOptions
{
    public const string SectionName = "Connpass";

    /// <summary>connpass API v2 の API キー。空ならイベント収集を行わない。
    /// 実値はコミットせず、環境変数(Connpass__ApiKey)や user-secrets で渡す。</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>いずれかに一致するイベントを収集するキーワード。</summary>
    public List<string> Keywords { get; set; } = [];
}

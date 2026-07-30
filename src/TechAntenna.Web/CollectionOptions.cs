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

/// <summary>書籍収集の設定。appsettings の Books セクションから読む。</summary>
public class BooksOptions
{
    public const string SectionName = "Books";

    /// <summary>巡回間隔(時)。書籍は記事ほど頻繁に増えないため既定を長めにする。</summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>1キーワードを検索してから次に移るまでの待ち時間(秒)。</summary>
    public int DelayBetweenKeywordsSeconds { get; set; } = 2;

    /// <summary>検索するキーワード。ここが空なら書籍収集は動かない。</summary>
    public List<string> Keywords { get; set; } = [];

    /// <summary>Google Books API キー。任意(未設定でも検索できるが1日あたりの上限が低くなる)。
    /// 実値はコミットせず環境変数(Books__GoogleBooksApiKey)や user-secrets で渡す。</summary>
    public string GoogleBooksApiKey { get; set; } = "";

    /// <summary>openBD で日本の書誌情報を補うか。</summary>
    public bool UseOpenBd { get; set; } = true;
}

/// <summary>Anthropic API による要約の設定。appsettings の Anthropic セクションから読む。</summary>
public class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    /// <summary>Anthropic API キー。空なら要約を行わない。
    /// 実値はコミットせず、環境変数(Anthropic__ApiKey)や user-secrets で渡す。</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>使用するモデル ID。コストを抑えるなら claude-haiku-4-5 に変更する。</summary>
    public string Model { get; set; } = "claude-opus-5";

    /// <summary>要約ジョブの実行間隔(分)。</summary>
    public int IntervalMinutes { get; set; } = 10;

    /// <summary>1回の実行で要約する記事数の上限。</summary>
    public int BatchSize { get; set; } = 5;
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

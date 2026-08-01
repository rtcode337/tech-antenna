namespace TechAntenna.Web;

/// <summary>収集ジョブの設定。appsettings の Collection セクションから読む。</summary>
public class CollectionOptions
{
    public const string SectionName = "Collection";

    /// <summary>
    /// 記事・イベントの収集を定期実行するか。**開発環境では false**
    /// (appsettings.Development.json)—— 開発サーバーを消し忘れると気づかないうちに
    /// 収集先を叩き続けるため。false でも画面のボタンから手動で走らせられる。
    /// </summary>
    public bool AutoRun { get; set; } = true;

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

/// <summary>Doorkeeper API の設定。appsettings の Doorkeeper セクションから読む。</summary>
public class DoorkeeperOptions
{
    public const string SectionName = "Doorkeeper";

    /// <summary>Doorkeeper の Public API アクセストークン。空ならイベント収集を行わない。
    /// 実値はコミットせず、環境変数(Doorkeeper__AccessToken)や user-secrets で渡す。</summary>
    public string AccessToken { get; set; } = "";

    /// <summary>検索するキーワード。1つずつ問い合わせ、見つかったイベントのタグになる。</summary>
    public List<string> Keywords { get; set; } = [];
}

/// <summary>TECH PLAY のイベント RSS の設定。appsettings の TechPlay セクションから読む。</summary>
public class TechPlayOptions
{
    public const string SectionName = "TechPlay";

    /// <summary>イベント RSS の URL。空なら TECH PLAY からの収集を行わない。</summary>
    public string FeedUrl { get; set; } = "";
}

/// <summary>書籍収集の設定。appsettings の Books セクションから読む。</summary>
public class BooksOptions
{
    public const string SectionName = "Books";

    /// <summary>書籍の収集を定期実行するか。既定と開発環境の扱いは
    /// <see cref="CollectionOptions.AutoRun"/> と同じ。</summary>
    public bool AutoRun { get; set; } = true;

    /// <summary>巡回間隔(時)。書籍は記事ほど頻繁に増えないため既定を長めにする。</summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>1キーワードを検索してから次に移るまでの待ち時間(秒)。</summary>
    public int DelayBetweenKeywordsSeconds { get; set; } = 2;

    /// <summary>検索するキーワード。ここが空なら書籍収集は動かない。</summary>
    public List<string> Keywords { get; set; } = [];

    /// <summary>Google Books API キー。実質必須(未設定だと検索が常に 429 になる)。
    /// 実値はコミットせず環境変数(Books__GoogleBooksApiKey)や user-secrets で渡す。</summary>
    public string GoogleBooksApiKey { get; set; } = "";

    /// <summary>openBD で日本の書誌情報を補うか。</summary>
    public bool UseOpenBd { get; set; } = true;
}

/// <summary>要約の設定。appsettings の Anthropic セクションから読む(実行間隔・件数は方式共通)。</summary>
public class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    /// <summary>Anthropic API キー。空でも Claude Code 方式が使えれば要約は動く。
    /// 実値はコミットせず、環境変数(Anthropic__ApiKey)や user-secrets で渡す。</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>使用するモデル ID。コストを抑えるなら claude-haiku-4-5 に変更する。</summary>
    public string Model { get; set; } = "claude-opus-5";

    /// <summary>要約を定期実行するか。既定と開発環境の扱いは
    /// <see cref="CollectionOptions.AutoRun"/> と同じ。</summary>
    public bool AutoRun { get; set; } = true;

    /// <summary>要約ジョブの実行間隔(分)。</summary>
    public int IntervalMinutes { get; set; } = 10;

    /// <summary>1回の実行で要約する記事数の上限。Claude Code 方式ではここが大きいほど
    /// 呼び出しの固定費が薄まり、1件あたりの消費が下がる。</summary>
    public int BatchSize { get; set; } = 20;
}

/// <summary>
/// Claude Code のヘッドレス実行で要約する場合の設定。appsettings の ClaudeCode セクションから読む。
/// 認証トークン(<c>CLAUDE_CODE_OAUTH_TOKEN</c>)はここでは持たない —— CLI が環境変数を
/// 直接読むので、アプリは有無を見て方式を選ぶだけ。
/// </summary>
public class ClaudeCodeOptions
{
    public const string SectionName = "ClaudeCode";

    /// <summary>claude CLI のパス。PATH 上にあるなら名前だけでよい。</summary>
    public string ExecutablePath { get; set; } = "claude";

    /// <summary>使うモデル。空なら CLI の既定に任せる。</summary>
    public string Model { get; set; } = "";

    /// <summary>1回の呼び出しの上限(秒)。超えたらプロセスごと落として次の巡回で再試行する。</summary>
    public int TimeoutSeconds { get; set; } = 300;
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

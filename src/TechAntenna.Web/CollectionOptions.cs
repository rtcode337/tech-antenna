using TechAntenna.Core.Models;

namespace TechAntenna.Web;

/// <summary>収集ジョブの設定。appsettings の Collection セクションから読む。</summary>
public class CollectionOptions
{
    public const string SectionName = "Collection";

    /// <summary>
    /// 記事・イベントの収集を定期実行するか。**既定は false で、動くのは画面のボタンを
    /// 押したときだけ** —— 消し忘れたサーバーが気づかないうちに収集先を叩き続け、
    /// 外部 API や LLM の無料枠を使い切ってしまうため。定期実行に切り替えるときに
    /// true にする(環境変数なら Collection__AutoRun=true)。
    /// </summary>
    public bool AutoRun { get; set; } = false;

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

    /// <summary>種別(Article / News)。一覧を分けて出すのに使う。既定は Article。</summary>
    public ArticleKind Kind { get; set; } = ArticleKind.Article;
}

/// <summary>J-STAGE(日本語の論文)の設定。appsettings の Jstage セクションから読む。</summary>
public class JstageOptions
{
    public const string SectionName = "Jstage";

    /// <summary>日本語の論文を集めるか。検索語は選択中のトピック。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>1キーワードあたりの取得件数。</summary>
    public int MaxResults { get; set; } = 20;

    /// <summary>何年ぶんさかのぼるか(1 なら今年のみ)。古い論文で一覧が埋まらないように絞る。</summary>
    public int WithinYears { get; set; } = 2;

    /// <summary>キーワードを1つ引くごとに空ける間隔(秒)。</summary>
    public double DelaySeconds { get; set; } = 3;
}

/// <summary>arXiv(論文)の設定。appsettings の Arxiv セクションから読む。</summary>
public class ArxivOptions
{
    public const string SectionName = "Arxiv";

    /// <summary>論文を集めるか。検索語は選択中のトピックなので、選択が空なら問い合わせない。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>1キーワードあたりの取得件数。</summary>
    public int MaxResults { get; set; } = 20;

    /// <summary>キーワードを1つ引くごとに空ける間隔(秒)。
    /// **arXiv の API 利用条件が 3 秒以上を求めている**ので、これより短くしない。</summary>
    public double DelaySeconds { get; set; } = 3;
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

    /// <summary>書籍の収集を定期実行するか。既定(false = 手動のみ)の理由は
    /// <see cref="CollectionOptions.AutoRun"/> と同じ。</summary>
    public bool AutoRun { get; set; } = false;

    /// <summary>巡回間隔(時)。書籍は記事ほど頻繁に増えないため既定を長めにする。</summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>1キーワードを検索してから次に移るまでの待ち時間(秒)。
    /// **検索語は設定ではなく選択中のトピック**(<c>ITopicStore.GetSelectedAsync</c>)から取る。</summary>
    public int DelayBetweenKeywordsSeconds { get; set; } = 2;

    /// <summary>Google Books API キー。実質必須(未設定だと検索が常に 429 になる)。
    /// 実値はコミットせず環境変数(Books__GoogleBooksApiKey)や user-secrets で渡す。</summary>
    public string GoogleBooksApiKey { get; set; } = "";

    /// <summary>openBD で日本の書誌情報を補うか。</summary>
    public bool UseOpenBd { get; set; } = true;

    /// <summary>
    /// 保存するのに必要なレビュー件数の下限。**レビューが取れた本だけが対象**で、
    /// 取れていない本(null)は通す —— 楽天のアプリ ID を設定していない状態で
    /// 足切りが効くと、1冊も保存されなくなるため。既定 0 は足切り無し。
    /// </summary>
    public int MinReviewCount { get; set; } = 0;
}

/// <summary>
/// Qiita の設定。appsettings の Qiita セクションから読む。
/// 「読むべき技術書」を挙げた記事から、薦められている本を拾うのに使う。
/// </summary>
public class QiitaOptions
{
    public const string SectionName = "Qiita";

    /// <summary>推薦本を拾うか。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 検索クエリ(Qiita の検索構文)。**ストック数の下限で絞る**のが肝 ——
    /// 誰も読んでいない記事の推薦まで数えると、指標が薄まる。
    /// </summary>
    public string Query { get; set; } = "tag:技術書 stocks:>100";

    /// <summary>1回に読む記事の本数。</summary>
    public int MaxArticles { get; set; } = 20;

    /// <summary>アクセストークン(任意)。未設定でも動くが、上限が 60 → 1000 リクエスト/時になる。
    /// 実値はコミットせず環境変数(Qiita__AccessToken)や user-secrets で渡す。</summary>
    public string AccessToken { get; set; } = "";
}

/// <summary>
/// 楽天ウェブサービスの設定。appsettings の Rakuten セクションから読む。
/// レビュー件数(「どのくらい読まれているか」の代理指標)の取得に使う。
/// </summary>
public class RakutenOptions
{
    public const string SectionName = "Rakuten";

    /// <summary>楽天ウェブサービスのアプリ ID。空ならレビューを取りに行かない。
    /// 実値はコミットせず環境変数(Rakuten__ApplicationId)や user-secrets で渡す。</summary>
    public string ApplicationId { get; set; } = "";

    /// <summary>アプリ ID と一緒に発行されるアクセスキー。要る場合だけ設定する。</summary>
    public string AccessKey { get; set; } = "";

    /// <summary>1 ISBN 引くごとに空ける間隔(秒)。ISBN の一括指定ができないため件数分のリクエストになる。</summary>
    public double DelaySeconds { get; set; } = 1;
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

    /// <summary>要約を定期実行するか。既定(false = 手動のみ)の理由は
    /// <see cref="CollectionOptions.AutoRun"/> と同じ。</summary>
    public bool AutoRun { get; set; } = false;

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

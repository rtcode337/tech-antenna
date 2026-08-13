using TechAntenna.Core.Models;

namespace TechAntenna.Web;

/// <summary>収集ジョブの設定。appsettings の Collection セクションから読む。</summary>
public class CollectionOptions
{
    public const string SectionName = "Collection";

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

/// <summary>
/// Hugging Face Daily Papers の設定。**トピックの選択に依存しない**収集元なので、
/// キーワードの設定も間隔の設定も持たない(1 回の実行で 1 リクエストだけ)。
/// </summary>
public class HuggingFacePapersOptions
{
    public const string SectionName = "HuggingFacePapers";

    /// <summary>話題の論文を集めるか。</summary>
    public bool Enabled { get; set; } = true;
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

    /// <summary>1キーワードを検索してから次に移るまでの待ち時間(秒)。
    /// **検索語は設定ではなく選択中のトピック**(<c>ITopicStore.GetSelectedAsync</c>)から取る。</summary>
    public int DelayBetweenKeywordsSeconds { get; set; } = 2;

    /// <summary>openBD で日本の書誌情報を補うか。</summary>
    public bool UseOpenBd { get; set; } = true;

    /// <summary>
    /// 書影が欠けている本を Google Books へ ISBN で引きに行くときの間隔(秒)。
    /// **1 冊 1 リクエスト**(ISBN の一括指定ができない)なので、間隔を空けて 1 冊ずつ引く。
    /// openBD は技術書の書影をほとんど持たないので、定番の書籍にはこの補完が要る。
    /// </summary>
    public int CoverLookupDelaySeconds { get; set; } = 1;

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
    /// 検索クエリ(Qiita の検索構文)。複数指定でき、同じ記事が複数のクエリに当たっても
    /// 1票に数える。**ストック数の下限で絞る**のが肝 —— 誰も読んでいない記事の推薦まで
    /// 数えると、指標が薄まる。タグ検索だけだと「読むべき本」系の記事の多く(タグ無し)を
    /// 取りこぼすので、本文検索のクエリも混ぜる —— ノイズは Amazon リンクの ISBN 検算が
    /// 落とすので、当たりの広さは害にならない。
    /// </summary>
    public List<string> Queries { get; set; } =
    [
        "tag:技術書 stocks:>100",
        "tag:書籍 stocks:>20",
        "おすすめ 技術書 stocks:>100",
    ];

    /// <summary>
    /// クエリ1つあたりで読む記事数の上限。検索は新着順に返るため、
    /// 少なすぎると古い定番記事が読めない(1ページ 50 件でページングする)。
    /// </summary>
    public int MaxArticles { get; set; } = 200;

    /// <summary>リクエストの間隔(秒)。無料でコミュニティに開かれている API のため空ける。</summary>
    public double DelaySeconds { get; set; } = 1;

}

/// <summary>
/// 楽天ウェブサービスの設定。appsettings の Rakuten セクションから読む。
/// レビュー件数(「どのくらい読まれているか」の代理指標)の取得に使う。
/// </summary>
public class RakutenOptions
{
    public const string SectionName = "Rakuten";


    /// <summary>1 ISBN 引くごとに空ける間隔(秒)。ISBN の一括指定ができないため件数分のリクエストになる。</summary>
    public double DelaySeconds { get; set; } = 1;
}

/// <summary>要約の設定。appsettings の Anthropic セクションから読む(実行間隔・件数は方式共通)。</summary>
public class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    /// <summary>使用するモデル ID。コストを抑えるなら claude-haiku-4-5 に変更する。</summary>
    public string Model { get; set; } = "claude-opus-5";

    /// <summary>1回の実行で要約する記事数の上限。Claude Code 方式ではここが大きいほど
    /// 呼び出しの固定費が薄まり、1件あたりの消費が下がる。</summary>
    public int BatchSize { get; set; } = 20;
}

/// <summary>
/// Claude Code のヘッドレス実行で要約する場合の設定。appsettings の ClaudeCode セクションから読む。
/// 認証トークンはここでは持たない —— 画面(外部連携)から設定した値を、LlmGateway が
/// 子プロセスの環境変数 <c>CLAUDE_CODE_OAUTH_TOKEN</c> として CLI に渡す。
/// </summary>
public class ClaudeCodeOptions
{
    public const string SectionName = "ClaudeCode";

    /// <summary>claude CLI のパス。PATH 上にあるなら名前だけでよい。</summary>
    public string ExecutablePath { get; set; } = "claude";

    /// <summary>使うモデル。**空にすると CLI の既定に任せる**が、その既定は重いモデル
    /// (実測 claude-fable-5)でサブスクの週間枠を消費しすぎるため、
    /// appsettings.json では claude-sonnet-5 を明示している。</summary>
    public string Model { get; set; } = "";

    /// <summary>1回の呼び出しの上限(秒)。超えたらプロセスごと落として次の巡回で再試行する。</summary>
    public int TimeoutSeconds { get; set; } = 300;
}

/// <summary>
/// 今日のサマリー(ダイジェスト)の設定。appsettings の Digest セクションから読む。
/// LLM の方式とキーは要約と共通(<see cref="AnthropicOptions"/> / <see cref="ClaudeCodeOptions"/>)。
/// </summary>
public class DigestOptions
{
    public const string SectionName = "Digest";

    /// <summary>「直近の話題」として材料に入れる窓(時間)。定期実行の間隔より広めに取り、
    /// 窓の境目で取りこぼさないようにする(既定 48 = 1日1回でも取りこぼさない)。</summary>
    public int WindowHours { get; set; } = 48;

    /// <summary>話題度上位として渡す件数(ニュース・記事・話題の論文で等分)。</summary>
    public int TrendingCount { get; set; } = 12;

    /// <summary>興味トピックに当たる記事として渡す件数。</summary>
    public int InterestCount { get; set; } = 10;

    /// <summary>これからのイベントとして渡す件数。</summary>
    public int EventCount { get; set; } = 8;

    /// <summary>イベントを「近い」とみなす日数。</summary>
    public int EventWindowDays { get; set; } = 14;
}

/// <summary>
/// ntfy(今日のサマリーの通知先)の設定。appsettings の Ntfy セクションから読む。
/// 接続先(ベース URL・トピック名・トークン)は画面から設定する(NtfySettings 参照)ので、
/// ここに残るのは ClickUrl だけ。
/// </summary>
public class NtfyOptions
{
    public const string SectionName = "Ntfy";

    /// <summary>通知をタップしたときに開く URL(任意。ホームの公開 URL を入れる)。
    /// 接続先(BaseUrl / Topic / トークン)は画面から設定するのでここには無い ——
    /// これだけは「この環境の公開 URL」というデプロイ側の事実なので環境変数に残す。</summary>
    public string ClickUrl { get; set; } = "";
}

/// <summary>connpass API の設定。appsettings の Connpass セクションから読む。</summary>
public class ConnpassOptions
{
    public const string SectionName = "Connpass";

    /// <summary>いずれかに一致するイベントを収集するキーワード。</summary>
    public List<string> Keywords { get; set; } = [];
}

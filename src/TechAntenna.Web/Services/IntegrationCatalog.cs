using Microsoft.Extensions.Options;

namespace TechAntenna.Web.Services;

/// <summary>
/// APIキー・トークンの要否。**「その連携が動くのに要るか」**で言う ——
/// アプリ全体が止まるかどうかではない(何が起きなくなるかは Effect が説明する)。
/// </summary>
public enum CredentialNeed
{
    /// <summary>キーもトークンも要らない(公開 API / RSS)。</summary>
    NotNeeded,

    /// <summary>無くてもこの連携は動くが、あると機能が増える・上限が上がる。</summary>
    Optional,

    /// <summary>無いとこの連携が動かない。</summary>
    Required,

    /// <summary>
    /// 同じ機能を担う連携が複数あり、**どれか1つ**が設定されていればよい(LLM の2方式)。
    /// どれも未設定なら機能ごと動かない —— 単なる「任意」とは意味が違う。
    /// </summary>
    EitherRequired,
}

/// <summary>
/// その連携がどちらの軸で使われるか。**両方で使うものは両方に出す** ——
/// 「トレンドが動かない」と「興味トピックが動かない」で見る場所が変わるので、
/// どちらの画面からでも必要なキーが分かるようにする。
/// </summary>
[Flags]
public enum IntegrationAxis
{
    /// <summary>トレンド(外で何が話題か。トピックの選択に依存しない)。</summary>
    Trending = 1,

    /// <summary>興味トピック(選んだトピックを検索語にして集める)。</summary>
    Interests = 2,

    /// <summary>定番(時間が経っても薦められ続けるもの。固定クエリで定評を掘る)。</summary>
    Classics = 4,

    /// <summary>トレンドと興味トピックの両方で使う(話題度の材料・LLM)。</summary>
    Both = Trending | Interests,
}

/// <summary>外部連携1件分の状態。</summary>
/// <param name="Axis">どちらの軸で使うか(両方なら両方の節に出る)。</param>
/// <param name="Purpose">用途(この単位で画面にまとめる)。</param>
/// <param name="Name">連携先。</param>
/// <param name="Need">キーの要否。</param>
/// <param name="Configured">キーが設定されているか。要らない連携では常に true。</param>
/// <param name="Effect">未設定・無効のときに何が起きるか。</param>
/// <param name="Enabled">そもそもこの連携を使う設定になっているか。</param>
/// <param name="SecretNames">
/// 画面から設定できるキーの設定パス(ApiCredentials に渡す名前。例 "Connpass:ApiKey")。
/// ほとんどの連携は 1 つだが、ntfy は接続先一式(BaseUrl / Topic / トークン)で複数持つ。
/// キーの要らない連携は空。
/// </param>
/// <param name="AlternativeConfigured">
/// <see cref="CredentialNeed.EitherRequired"/> の連携で、同じ機能を担う**もう一方**が
/// 設定されているか(この行が未設定でも機能は動いている、を画面で言うため)。
/// </param>
/// <param name="ToggleName">
/// 画面から<b>オン/オフを切り替えられる</b>連携の設定パス(<c>SweepSettings</c> のキー)。
/// null なら切り替えは出さない —— ほとんどの連携は「キーがあれば使う」で、
/// 止めたいかどうかを決める必要が無い(面掃きだけは相手を叩く量が大きいので明示的に入れる)。
/// </param>
public record Integration(
    IntegrationAxis Axis,
    string Purpose,
    string Name,
    CredentialNeed Need,
    bool Configured,
    string Effect,
    bool Enabled = true,
    IReadOnlyList<string>? SecretNames = null,
    bool AlternativeConfigured = false,
    string? ToggleName = null);

/// <summary>
/// 外部連携の一覧と、キーが設定されているかどうかを組み立てる。
///
/// **設定値そのものは画面に出さない**(有無だけを見る)。キーやトークンは秘匿情報なので、
/// 長さや先頭数文字も含めて出さない。
///
/// 一覧は<b>ここに手で並べる</b>。設定から自動生成はできない —— 「未設定だと何が起きるか」は
/// 設定値には書いていないうえ、キーの要否は収集元ごとの事情(申請の要不要・無料枠の有無)で
/// 決まるため。**外部 API を足したらここにも1行足すこと。**
/// </summary>
public class IntegrationCatalog(
    IConfiguration configuration,
    ApiCredentials credentials,
    IOptions<CollectionOptions> collection)
{
    /// <summary>画面から設定できるキーの1件分(外部連携画面の設定フォームの元)。</summary>
    /// <param name="SecretName">ApiCredentials に渡す設定パス。</param>
    /// <param name="Label">フォームに出す名前。</param>
    /// <param name="Sensitive">秘匿値か。false なら入力欄を伏せ字にせず、
    /// <b>保存済みの値も入力欄に出してコピーできる</b>(ntfy の URL・トピック名)。
    /// true のキーは有無だけを画面に出し、値は長さも先頭数文字も出さない。</param>
    /// <param name="Hint">入力欄の placeholder。既定値があるキーはここで示す。</param>
    /// <param name="Generate">値を自分で作れるキーはここに作り方を渡す(画面に「生成」が出て、
    /// 押すと入力欄に入る。保存はされないので、気に入らなければ押し直せる)。
    /// 外部から発行されるキー(API キー・トークン)は null。</param>
    public record EditableSecret(
        string SecretName, string Label, bool Sensitive = true, string? Hint = null,
        Func<string>? Generate = null);

    /// <summary>
    /// 画面から設定できるキーの一覧。**外部 API のキーを足したらここにも1行足すこと**
    /// (一覧の表と同じく、手で並べる —— どの設定パスがキーなのかは設定値からは分からない)。
    /// </summary>
    public static readonly IReadOnlyList<EditableSecret> EditableSecrets =
    [
        new("Connpass:ApiKey", "connpass API キー"),
        new("Doorkeeper:AccessToken", "Doorkeeper アクセストークン"),
        new("Books:GoogleBooksApiKey", "Google Books API キー"),
        new("Rakuten:ApplicationId", "楽天ウェブサービス アプリ ID"),
        new("Rakuten:AccessKey", "楽天ウェブサービス アクセスキー(任意)"),
        new("Qiita:AccessToken", "Qiita アクセストークン"),
        new(LlmGateway.ClaudeCodeTokenName, "Claude Code OAuth トークン"),
        new(LlmGateway.AnthropicApiKeyName, "Anthropic API キー"),
        // ntfy の接続先一式。URL とトピック名は秘匿値ではないので伏せ字にせず、
        // 保存済みの値も出す(トピック名は ntfy.sh では実質パスワードだが、設定ミスに
        // 気づけない・購読する端末へ写せないほうが害が大きい)
        new(NtfySettings.BaseUrlName, "ntfy ベース URL", Sensitive: false,
            Hint: $"未設定なら {NtfySettings.DefaultBaseUrl}"),
        // トピック名だけはこちらで決めてよい値なので「生成」を出す(ntfy に登録は要らず、
        // 好きな名前へ送れば購読側に届く。ただし推測されると誰でも読めるので乱数で作る)
        new(NtfySettings.TopicName, "ntfy トピック名", Sensitive: false,
            Generate: NtfySettings.GenerateTopic),
        new(NtfySettings.AccessTokenName, "ntfy アクセストークン(認証ありのときだけ)"),
    ];
    public IReadOnlyList<Integration> GetAll()
    {
        var huggingFace = Section<HuggingFacePapersOptions>(HuggingFacePapersOptions.SectionName);
        var arxiv = Section<ArxivOptions>(ArxivOptions.SectionName);
        var jstage = Section<JstageOptions>(JstageOptions.SectionName);
        var qiita = Section<QiitaOptions>(QiitaOptions.SectionName);
        var books = Section<BooksOptions>(BooksOptions.SectionName);
        var newReleases = Section<NewReleaseOptions>(NewReleaseOptions.SectionName);
        var techPlay = Section<TechPlayOptions>(TechPlayOptions.SectionName);
        var connpass = Section<ConnpassOptions>(ConnpassOptions.SectionName);
        var doorkeeper = Section<DoorkeeperOptions>(DoorkeeperOptions.SectionName);

        var integrations = new List<Integration>();

        // --- 記事・ニュース ---
        foreach (var feed in collection.Value.Feeds)
        {
            integrations.Add(new Integration(
                IntegrationAxis.Trending,
                feed.Kind == Core.Models.ArticleKind.News ? "ニュース" : "記事",
                feed.Name,
                CredentialNeed.NotNeeded,
                true,
                "RSS / Atom を巡回するだけなので申請もキーも要らない"));
        }

        integrations.Add(new Integration(
            IntegrationAxis.Trending, "記事", "はてなブックマーク件数 API", CredentialNeed.NotNeeded, true,
            "記事・ニュースの人気(ブックマーク数)を全ソース横断で補う。50 URL まで一括で引ける"));

        // --- 論文 ---
        integrations.Add(new Integration(
            IntegrationAxis.Trending, "話題の論文", "Hugging Face Daily Papers", CredentialNeed.NotNeeded, true,
            "話題の論文。トピックの選択に依存しない(中身は arXiv 投稿なので英語のみ)",
            huggingFace.Enabled));
        integrations.Add(new Integration(
            IntegrationAxis.Interests, "論文", "arXiv", CredentialNeed.NotNeeded, true,
            "英語の論文。3 秒以上の間隔を空けて問い合わせる", arxiv.Enabled));
        integrations.Add(new Integration(
            IntegrationAxis.Interests, "論文", "J-STAGE", CredentialNeed.NotNeeded, true,
            "日本語の論文。直近 " + jstage.WithinYears + " 年ぶんに絞って引く", jstage.Enabled));

        // 出版トレンド(最近出た本からテーマを数える)。**キーも検索語も要らない**ので
        // トレンドの軸に置ける —— 分類(NDC)と刊行日で引く
        integrations.Add(new Integration(
            IntegrationAxis.Trending, "出版トレンド", "NDL サーチ", CredentialNeed.NotNeeded, true,
            "最近出た本・ムックのタイトルからテーマを数える。申請もキーも要らない",
            newReleases.Enabled));

        // --- 書籍 ---
        // 定番の軸にも出す —— 検索には使わないが、**書影が欠けている本を ISBN で引く**のがここ
        integrations.Add(WithSecret(new Integration(
            IntegrationAxis.Interests | IntegrationAxis.Classics,
            "書籍", "Google Books", CredentialNeed.Required,
            false,
            "未設定だと検索が毎回 429 になる(キー無しは共有の匿名プロジェクト扱いで上限 0 件)。"
            + "定番の書籍の書影もここから引く"),
            "Books:GoogleBooksApiKey"));
        // 補完(openBD・楽天)は興味トピックの検索でも定番の推薦本でも使う
        integrations.Add(new Integration(
            IntegrationAxis.Interests | IntegrationAxis.Classics,
            "書籍", "openBD", CredentialNeed.NotNeeded, true,
            "ISBN から書誌情報を補う。日本の書誌が無料で引ける(技術書の書影はほとんど持たない)",
            books.UseOpenBd));
        // 「必須」= この連携(レビュー取得)が動くのに必須。書籍そのものは Google Books が
        // 集めるので、アプリとしては無くても回る(それは Effect の側で言う)
        integrations.Add(WithSecret(new Integration(
            IntegrationAxis.Interests | IntegrationAxis.Classics,
            "書籍", "楽天ブックス", CredentialNeed.Required,
            false,
            "未設定でも書籍は集まるが、レビュー(読まれている度合い)が取れず並べ替えができない。"
            + "設定すると書影も同じ応答から埋まる(Google Books への問い合わせが減る)"),
            "Rakuten:ApplicationId"));
        // 推薦本は定番の軸。**トピックの選択とは無関係**(固定クエリで「読むべき本」記事を掘る)
        integrations.Add(WithSecret(new Integration(
            IntegrationAxis.Classics, "書籍", "Qiita(推薦本)", CredentialNeed.Optional,
            false,
            "未設定でも動く。トークンを入れると API の上限が 60 → 1000 リクエスト/時になる",
            qiita.Enabled),
            "Qiita:AccessToken"));

        // --- イベント ---
        integrations.Add(WithSecret(new Integration(
            IntegrationAxis.Interests | IntegrationAxis.Classics, "イベント", "connpass", CredentialNeed.Required,
            false,
            "**利用申請が要る**。未設定だと connpass からは収集しない。"
            + "キーワード検索に加えて、設定 → 購読 に載せたシリーズも引く"),
            "Connpass:ApiKey"));
        integrations.Add(WithSecret(new Integration(
            IntegrationAxis.Interests | IntegrationAxis.Classics, "イベント", "Doorkeeper", CredentialNeed.Required,
            false,
            "未設定だと Doorkeeper からは収集しない。API は alpha 扱いで破壊的変更がありうる。"
            + "キーワード検索に加えて、設定 → 購読 に載せたコミュニティも引く"),
            "Doorkeeper:AccessToken"));
        // 面掃きは検索・購読と同じキーを使う別経路。**既定では動かない**ので、
        // 「使えるのに動いていない」ことが画面から読めるように 1 行を分けて出す。
        // **定番のイベント(/classics/events)を埋めるのは主にこの2つ** ——
        // 検索も購読も「こちらが知っているもの」しか拾えないため
        integrations.Add(WithSecret(new Integration(
            IntegrationAxis.Interests | IntegrationAxis.Classics,
            "イベント", "connpass(面掃き)", CredentialNeed.Required,
            false,
            $"月ごとに全件なめて、参加者 {connpass.Sweep.MinParticipants} 人以上だけを残す"
            + $"({connpass.Sweep.Months} か月ぶん)。"
            + "検索語も名簿も使わないので、名前を知らない大型イベントを拾える。"
            + "1か月ぶんで数十リクエストかかるため、この行の切り替えで明示的に入れる",
            SweepSettings.IsEnabled(credentials, SweepSettings.ConnpassName)),
            "Connpass:ApiKey") with { ToggleName = SweepSettings.ConnpassName });
        integrations.Add(WithSecret(new Integration(
            IntegrationAxis.Interests | IntegrationAxis.Classics,
            "イベント", "Doorkeeper(面掃き)", CredentialNeed.Required,
            false,
            $"期間で全件なめて、参加者 {doorkeeper.Sweep.MinParticipants} 人以上だけを残す"
            + $"({doorkeeper.Sweep.Months} か月ぶん。connpass の面掃きと同じ役割で、相手だけが違う)。"
            + "数十リクエストかかるため、この行の切り替えで明示的に入れる",
            SweepSettings.IsEnabled(credentials, SweepSettings.DoorkeeperName)),
            "Doorkeeper:AccessToken") with { ToggleName = SweepSettings.DoorkeeperName });
        integrations.Add(new Integration(
            IntegrationAxis.Interests | IntegrationAxis.Classics,
            "イベント", "TECH PLAY", CredentialNeed.NotNeeded, true,
            "RSS なのでキーは要らない代わりに検索ができない(最新 50 件が流れてくるだけ)。"
            + "主催者は取れる(dc:creator)が参加者数は無い",
            !string.IsNullOrWhiteSpace(techPlay.FeedUrl)));

        // --- トピック ---
        integrations.Add(new Integration(
            IntegrationAxis.Both, "トピック(話題度)", "Qiita(トレンド)", CredentialNeed.NotNeeded, true,
            "直近記事のタグをいいね数で重み付けして話題度を出す"));
        integrations.Add(new Integration(
            IntegrationAxis.Both, "トピック(話題度)", "はてなブックマーク(人気エントリー)",
            CredentialNeed.NotNeeded, true,
            "人気エントリーの RSS をブックマーク数で重み付けして話題度を出す"));

        // --- 要約・翻訳 ---
        // 2方式は同じ機能の担い手なので「どちらか必須」。両方未設定なら LLM 機能ごと止まり、
        // 片方があればもう片方の行は「未設定(もう一方で動作)」になる
        integrations.Add(WithSecret(new Integration(
            IntegrationAxis.Both, "要約・翻訳・語彙の仕分け・今日のサマリー", "Claude Code(サブスクの枠)", CredentialNeed.EitherRequired,
            false,
            "`claude setup-token` で発行する。**設定されていると Anthropic API より優先**され、"
            + "従量課金ではなくサブスクリプションの枠を使う。**CLI は別コンテナ(bridge)が動かす**ので、"
            + "そちらが起動していないと要約・翻訳のジョブが失敗する",
            AlternativeConfigured: credentials.Has(LlmGateway.AnthropicApiKeyName)),
            LlmGateway.ClaudeCodeTokenName));
        integrations.Add(WithSecret(new Integration(
            IntegrationAxis.Both, "要約・翻訳・語彙の仕分け・今日のサマリー", "Anthropic API(従量課金)", CredentialNeed.EitherRequired,
            false,
            "Claude Code のトークンが無いときの代わり。**両方とも未設定なら要約・翻訳・今日のサマリーのボタンが"
            + "無効になる**",
            AlternativeConfigured: credentials.Has(LlmGateway.ClaudeCodeTokenName)),
            LlmGateway.AnthropicApiKeyName));

        // --- 通知 ---
        // 通知そのものは接続先が無ければ動かないので「必須」(サマリーの生成は別の連携の話)。
        // 設定済みの判定は**トピックだけ**で足りる —— ベース URL には既定(ntfy.sh)がある。
        // 通知のオン/オフ(設定画面)は接続先の有無とは別の話なので、ここの状態には混ぜない
        integrations.Add(new Integration(
            IntegrationAxis.Both, "今日のサマリーの通知", "ntfy", CredentialNeed.Required,
            NtfySettings.IsConfigured(credentials),
            "トピック名を設定したときだけ通知する(ベース URL は未設定なら "
            + NtfySettings.DefaultBaseUrl + ")。通知のオン/オフは設定画面で切り替える。"
            + "未設定ならサマリーは画面に出るだけ",
            SecretNames:
            [
                NtfySettings.BaseUrlName,
                NtfySettings.TopicName,
                NtfySettings.AccessTokenName,
            ]));

        return integrations;
    }

    /// <summary>キーの有無を ApiCredentials から埋める(画面で設定できる連携に使う)。</summary>
    Integration WithSecret(Integration integration, string secretName) =>
        integration with
        {
            SecretNames = [secretName],
            Configured = credentials.Has(secretName),
        };

    T Section<T>(string name) where T : new() =>
        configuration.GetSection(name).Get<T>() ?? new T();
}

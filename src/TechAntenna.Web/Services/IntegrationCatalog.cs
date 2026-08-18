using Microsoft.Extensions.Options;
using TechAntenna.Infrastructure.Feeds;

namespace TechAntenna.Web.Services;

/// <summary>
/// APIキー・トークンの要否。**「その連携が動くのに要るか」**で言う ——
/// アプリ全体が止まるかどうかではない(この連携が何をするかは Description が説明する)。
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
/// <param name="Description">
/// **この連携が何のためにあるか**(画面の「説明」列)。
/// **「未設定だと何が起きるか」ではない** —— キーの要否は隣の「キー」列と「状態」列が言うので、
/// ここで繰り返すと、キーの要らない連携で「未設定のとき」の欄に用途が書いてある状態になり、
/// 読み手の直感と食い違う。
/// **キーが任意の連携(<see cref="CredentialNeed.Optional"/>)だけは、
/// 入れると何が変わるかもここに書く** —— 入れる動機がほかに書かれていないため。
/// </param>
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
    string Description,
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
    /// <summary>
    /// LLM の用途名。**この用途の行だけは「どの AI に書かせるか」の節に出す**
    /// (`AiBackendSection` の下。軸ごとの表からは外す)—— 相手を選ぶ話とキーを入れる話が
    /// 並んでいないと、「選べない理由がキー未設定」だと分からない。
    /// 両方の画面から同じ名前で絞れるよう定数にしてある。
    /// </summary>
    public const string LlmPurpose = "要約・翻訳・語彙の仕分け・今日のサマリー";

    /// <summary>
    /// 通知の用途名。**この用途の行だけは「通知」の節に出す**(軸ごとの表からは外す)——
    /// 通知は<b>集めた後の出口</b>で、「どこから集めるか」の話ではない。
    /// 軸(トレンド / 興味トピック / 定番)のどれにも属さないのに `Both` で両方の節に
    /// 出ていたので、収集の表を読む邪魔になっていた。
    /// </summary>
    public const string NotifyPurpose = "今日のサマリーの通知";

    /// <summary>
    /// 用途を画面に並べる順。**サイドバーの並びに合わせる** ——
    /// ニュース → 記事 → 書籍 → 論文(トレンド)、イベント → 書籍 → 論文(興味トピック・定番)。
    /// 設定の画面だけ順番が違うと、同じものを探すのに毎回読み直すことになる。
    /// **ここに無い用途は後ろに回る**(足したときに黙って先頭へ来ないように)。
    /// </summary>
    static readonly string[] PurposeOrder =
    [
        "ニュース", "記事", "イベント", "書籍", "論文", "話題の論文", "出版トレンド", "トピック(話題度)",
    ];

    /// <summary>用途の並び順(小さいほど先)。一覧の <c>GroupBy</c> の後に使う。</summary>
    public static int OrderOf(string purpose)
    {
        var index = Array.IndexOf(PurposeOrder, purpose);

        return index < 0 ? PurposeOrder.Length : index;
    }

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
    /// <param name="Required">
    /// **その連携が動くのに要る値か。** 連携が複数の値を持つとき(ntfy の
    /// ベース URL / トピック名 / トークン)、どれが無いと動かないのかは値ごとに違う ——
    /// 行として「キー: 必須」と出すと、ベース URL やトークンまで要るように読める。
    /// </param>
    public record EditableSecret(
        string SecretName, string Label, bool Sensitive = true, string? Hint = null,
        Func<string>? Generate = null, bool Required = false);

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
        // **必須なのはトピック名だけ。** ベース URL には既定(ntfy.sh)があり、
        // トークンは認証のある ntfy を使うときだけ要る
        new(NtfySettings.TopicName, "ntfy トピック名", Sensitive: false,
            Generate: NtfySettings.GenerateTopic, Required: true),
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
            integrations.Add(Toggleable(new Integration(
                IntegrationAxis.Trending,
                feed.Kind == Core.Models.ArticleKind.News ? "ニュース" : "記事",
                feed.Name,
                CredentialNeed.NotNeeded,
                true,
                "記事・ニュースの収集元。RSS / Atom を巡回して新着を取り込む"), SourceToggles.Article));
        }

        integrations.Add(Toggleable(new Integration(
            IntegrationAxis.Trending, "記事", BookmarkCountRefresher.SourceName, CredentialNeed.NotNeeded, true,
            "集めた記事・ニュースの人気(ブックマーク数)を収集元をまたいで補う。50 URL まで一括で引ける"),
            SourceToggles.Bookmark));

        // --- 論文 ---
        integrations.Add(Toggleable(new Integration(
            IntegrationAxis.Trending, "話題の論文", "Hugging Face Daily Papers", CredentialNeed.NotNeeded, true,
            "いま話題の論文を集める。トピックの選択に依存しない(中身は arXiv 投稿なので英語のみ)",
            huggingFace.Enabled), SourceToggles.Article));
        integrations.Add(Toggleable(new Integration(
            IntegrationAxis.Interests, "論文", "arXiv", CredentialNeed.NotNeeded, true,
            "選んだトピックを検索語にして英語の論文を集める。3 秒以上の間隔を空けて問い合わせる",
            arxiv.Enabled), SourceToggles.Paper));
        integrations.Add(Toggleable(new Integration(
            IntegrationAxis.Interests, "論文", "J-STAGE", CredentialNeed.NotNeeded, true,
            "選んだトピックを検索語にして日本語の論文を集める(直近 " + jstage.WithinYears + " 年ぶん)",
            jstage.Enabled), SourceToggles.Paper));

        // 出版トレンド(最近出た本からテーマを数える)。**キーも検索語も要らない**ので
        // トレンドの軸に置ける —— 分類(NDC)と刊行日で引く
        integrations.Add(Toggleable(new Integration(
            IntegrationAxis.Trending, "出版トレンド", "NDL サーチ", CredentialNeed.NotNeeded, true,
            "最近出た本・ムックのタイトルから「本になっているテーマ」を数える。検索語も要らない(分類と刊行日で引く)",
            newReleases.Enabled), SourceToggles.NewRelease));

        // --- 書籍 ---
        // 定番の軸にも出す —— 検索には使わないが、**書影が欠けている本を ISBN で引く**のがここ
        integrations.Add(Toggleable(WithSecret(new Integration(
            IntegrationAxis.Interests | IntegrationAxis.Classics,
            "書籍", "Google Books", CredentialNeed.Required,
            false,
            "選んだトピックを検索語にして書籍を探す。定番の書籍の書影もここから引く。"
            + "キー無しのリクエストは共有の匿名枠(上限 0 件)に入るので、毎回 429 になる"),
            "Books:GoogleBooksApiKey"), SourceToggles.Book));
        // 補完(openBD・楽天)は興味トピックの検索でも定番の推薦本でも使う
        integrations.Add(Toggleable(new Integration(
            IntegrationAxis.Interests | IntegrationAxis.Classics,
            "書籍", "openBD", CredentialNeed.NotNeeded, true,
            "ISBN から書誌情報(タイトル・著者・出版社)を補う。日本の書誌が無料で引ける(技術書の書影はほとんど持たない)",
            books.UseOpenBd), SourceToggles.Enricher));
        // 「必須」= この連携(レビュー取得)が動くのに必須。書籍そのものは Google Books が
        // 集めるので、アプリとしては無くても回る
        integrations.Add(Toggleable(WithSecret(new Integration(
            IntegrationAxis.Interests | IntegrationAxis.Classics,
            "書籍", "楽天ブックス", CredentialNeed.Required,
            false,
            "書籍のレビュー(件数・評価)を ISBN で引く。「読まれている度合い」の並べ替えはこれが元。"
            + "書影も同じ応答から埋まる(Google Books への問い合わせが減る)"),
            "Rakuten:ApplicationId"), SourceToggles.Enricher));
        // 推薦本は定番の軸。**トピックの選択とは無関係**(固定クエリで「読むべき本」記事を掘る)
        integrations.Add(Toggleable(WithSecret(new Integration(
            IntegrationAxis.Classics, "書籍", "Qiita(推薦本)", CredentialNeed.Optional,
            false,
            "「読むべき技術書」を挙げた記事から、薦められている本を掘る(定番の書籍の元)。"
            + "**トークンを入れると**上限が 60 → 1000 リクエスト/時になり、一度に読める記事が増える",
            qiita.Enabled),
            "Qiita:AccessToken"), SourceToggles.Recommendation));

        // --- イベント ---
        integrations.Add(Toggleable(WithSecret(new Integration(
            IntegrationAxis.Interests | IntegrationAxis.Classics, "イベント", "connpass", CredentialNeed.Required,
            false,
            "選んだトピックを検索語にしてイベントを集める。設定 → イベントの購読に載せたシリーズも引く。"
            + "**利用申請が要る**"),
            "Connpass:ApiKey"), SourceToggles.Event));
        integrations.Add(Toggleable(WithSecret(new Integration(
            IntegrationAxis.Interests | IntegrationAxis.Classics, "イベント", "Doorkeeper", CredentialNeed.Required,
            false,
            "選んだトピックを検索語にしてイベントを集める。設定 → イベントの購読に載せたコミュニティも引く。"
            + "API は alpha 扱いで破壊的変更がありうる"),
            "Doorkeeper:AccessToken"), SourceToggles.Event));
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
            + $"**1 リクエスト 100 件・1 か月あたり最大 10 ページ**なので、"
            + $"**初回だけ**最大 {connpass.Sweep.Months * 10} リクエスト(**5 秒間隔・逐次**)。"
            + "**2 回目からは前回以降に公開されたぶんだけ**(`publish_ymd`)なので、たいてい 1 リクエスト。"
            + "参加者数で絞り込める API が無いため、**週に一度は数え直す**",
            SweepSettings.IsEnabled(credentials, SweepSettings.ConnpassName)),
            "Connpass:ApiKey") with { ToggleName = SweepSettings.ConnpassName });
        integrations.Add(WithSecret(new Integration(
            IntegrationAxis.Interests | IntegrationAxis.Classics,
            "イベント", "Doorkeeper(面掃き)", CredentialNeed.Required,
            false,
            $"期間で全件なめて、参加者 {doorkeeper.Sweep.MinParticipants} 人以上だけを残す"
            + $"({doorkeeper.Sweep.Months} か月ぶん。connpass の面掃きと同じ役割で、相手だけが違う)。"
            + "**期間をまとめて 1 ページずつ**で、最大 10 リクエスト(2 秒間隔・逐次)。"
            + "空のページが返った時点で終わる。**掃くのは 1 日 1 回**",
            SweepSettings.IsEnabled(credentials, SweepSettings.DoorkeeperName)),
            "Doorkeeper:AccessToken") with { ToggleName = SweepSettings.DoorkeeperName });
        integrations.Add(Toggleable(new Integration(
            IntegrationAxis.Interests | IntegrationAxis.Classics,
            "イベント", "TECH PLAY", CredentialNeed.NotNeeded, true,
            "企業主催のウェビナーが厚い収集元。RSS なので検索ができず、最新 50 件が流れてくるだけ。"
            + "主催者は取れる(dc:creator)が参加者数は無い",
            !string.IsNullOrWhiteSpace(techPlay.FeedUrl)), SourceToggles.Event));

        // --- トピック ---
        integrations.Add(Toggleable(new Integration(
            IntegrationAxis.Both, "トピック(話題度)", "Qiita", CredentialNeed.NotNeeded, true,
            "直近記事のタグをいいね数で重み付けして、トピックの話題度を出す"), SourceToggles.Trend));
        integrations.Add(Toggleable(new Integration(
            IntegrationAxis.Both, "トピック(話題度)", "はてなブックマーク",
            CredentialNeed.NotNeeded, true,
            "人気エントリーの RSS をブックマーク数で重み付けして、トピックの話題度を出す"), SourceToggles.Trend));

        // --- 要約・翻訳 ---
        // 2方式は同じ機能の担い手なので「どちらか必須」。両方未設定なら LLM 機能ごと止まり、
        // 片方があればもう片方の行は「未設定(もう一方で動作)」になる
        integrations.Add(WithSecret(new Integration(
            IntegrationAxis.Both, LlmPurpose, "Claude Code(サブスクの枠)", CredentialNeed.EitherRequired,
            false,
            "要約・翻訳・タグの仕分け・今日のサマリーを書かせる。`claude setup-token` で発行するトークンで、"
            + "従量課金ではなく**サブスクリプションの枠**を使う",
            AlternativeConfigured: credentials.Has(LlmGateway.AnthropicApiKeyName)),
            LlmGateway.ClaudeCodeTokenName));
        integrations.Add(WithSecret(new Integration(
            IntegrationAxis.Both, LlmPurpose, "Anthropic API(従量課金)", CredentialNeed.EitherRequired,
            false,
            "同じ用途を従量課金の API で動かす。Claude Code のトークンが無いときの代わり",
            AlternativeConfigured: credentials.Has(LlmGateway.ClaudeCodeTokenName)),
            LlmGateway.AnthropicApiKeyName));

        // --- 通知 ---
        // 通知そのものは接続先が無ければ動かないので「必須」(サマリーの生成は別の連携の話)。
        // 設定済みの判定は**トピックだけ**で足りる —— ベース URL には既定(ntfy.sh)がある。
        // 通知のオン/オフ(設定画面)は接続先の有無とは別の話なので、ここの状態には混ぜない
        integrations.Add(new Integration(
            IntegrationAxis.Both, NotifyPurpose, "ntfy", CredentialNeed.Required,
            NtfySettings.IsConfigured(credentials),
            "今日のサマリーを ntfy へ送る。トピック名を設定したときだけ通知する(ベース URL は未設定なら "
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

    /// <summary>
    /// 収集元のオン/オフを行に付ける。**役割 + 名前が鍵**(<see cref="SourceToggles"/>)——
    /// ランナー側も同じ鍵で引くので、画面で止めたものは叩きに行かなくなる。
    /// 面掃きだけは別の設定(<see cref="SweepSettings"/>。既定が逆で、明示的に入れたときだけ動く)。
    /// </summary>
    Integration Toggleable(Integration integration, string role) =>
        integration with
        {
            ToggleName = SourceToggles.KeyOf(role, integration.Name),
            Enabled = integration.Enabled && !credentials.Has(SourceToggles.KeyOf(role, integration.Name)),
        };

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

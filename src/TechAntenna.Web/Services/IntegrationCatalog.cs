using Microsoft.Extensions.Options;

namespace TechAntenna.Web.Services;

/// <summary>APIキー・トークンの要否。</summary>
public enum CredentialNeed
{
    /// <summary>キーもトークンも要らない(公開 API / RSS)。</summary>
    NotNeeded,

    /// <summary>無くても動くが、あると機能が増える・上限が上がる。</summary>
    Optional,

    /// <summary>無いとその連携が動かない。</summary>
    Required,
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
/// <param name="SettingKey">設定する環境変数名。キーが要らない連携では null。</param>
/// <param name="Configured">キーが設定されているか。要らない連携では常に true。</param>
/// <param name="Effect">未設定・無効のときに何が起きるか。</param>
/// <param name="Enabled">そもそもこの連携を使う設定になっているか。</param>
public record Integration(
    IntegrationAxis Axis,
    string Purpose,
    string Name,
    CredentialNeed Need,
    string? SettingKey,
    bool Configured,
    string Effect,
    bool Enabled = true);

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
    IOptions<CollectionOptions> collection,
    IOptions<BooksOptions> books,
    IOptions<RakutenOptions> rakuten,
    IOptions<AnthropicOptions> anthropic)
{
    public IReadOnlyList<Integration> GetAll()
    {
        var huggingFace = Section<HuggingFacePapersOptions>(HuggingFacePapersOptions.SectionName);
        var arxiv = Section<ArxivOptions>(ArxivOptions.SectionName);
        var jstage = Section<JstageOptions>(JstageOptions.SectionName);
        var qiita = Section<QiitaOptions>(QiitaOptions.SectionName);
        var connpass = Section<ConnpassOptions>(ConnpassOptions.SectionName);
        var doorkeeper = Section<DoorkeeperOptions>(DoorkeeperOptions.SectionName);
        var techPlay = Section<TechPlayOptions>(TechPlayOptions.SectionName);
        var claudeCodeToken = configuration["CLAUDE_CODE_OAUTH_TOKEN"];

        var integrations = new List<Integration>();

        // --- 記事・ニュース ---
        foreach (var feed in collection.Value.Feeds)
        {
            integrations.Add(new Integration(
                IntegrationAxis.Trending,
                feed.Kind == Core.Models.ArticleKind.News ? "ニュース" : "記事",
                feed.Name,
                CredentialNeed.NotNeeded,
                null,
                true,
                "RSS / Atom を巡回するだけなので申請もキーも要らない"));
        }

        integrations.Add(new Integration(
            IntegrationAxis.Trending, "記事", "はてなブックマーク件数 API", CredentialNeed.NotNeeded, null, true,
            "記事・ニュースの人気(ブックマーク数)を全ソース横断で補う。50 URL まで一括で引ける"));

        // --- 論文 ---
        integrations.Add(new Integration(
            IntegrationAxis.Trending, "話題の論文", "Hugging Face Daily Papers", CredentialNeed.NotNeeded, null, true,
            "話題の論文。トピックの選択に依存しない(中身は arXiv 投稿なので英語のみ)",
            huggingFace.Enabled));
        integrations.Add(new Integration(
            IntegrationAxis.Interests, "論文", "arXiv", CredentialNeed.NotNeeded, null, true,
            "英語の論文。3 秒以上の間隔を空けて問い合わせる", arxiv.Enabled));
        integrations.Add(new Integration(
            IntegrationAxis.Interests, "論文", "J-STAGE", CredentialNeed.NotNeeded, null, true,
            "日本語の論文。直近 " + jstage.WithinYears + " 年ぶんに絞って引く", jstage.Enabled));

        // --- 書籍 ---
        integrations.Add(new Integration(
            IntegrationAxis.Interests, "書籍", "Google Books", CredentialNeed.Required, "Books__GoogleBooksApiKey",
            !string.IsNullOrWhiteSpace(books.Value.GoogleBooksApiKey),
            "未設定だと検索が毎回 429 になる(キー無しは共有の匿名プロジェクト扱いで上限 0 件)"));
        // 補完(openBD・楽天)は興味トピックの検索でも定番の推薦本でも使う
        integrations.Add(new Integration(
            IntegrationAxis.Interests | IntegrationAxis.Classics,
            "書籍", "openBD", CredentialNeed.NotNeeded, null, true,
            "ISBN から書誌情報を補う。日本の書誌が無料で引ける", books.Value.UseOpenBd));
        // 書籍そのものは集まるので「必須」ではない(レビューが取れないだけ)
        integrations.Add(new Integration(
            IntegrationAxis.Interests | IntegrationAxis.Classics,
            "書籍", "楽天ブックス", CredentialNeed.Optional, "Rakuten__ApplicationId",
            !string.IsNullOrWhiteSpace(rakuten.Value.ApplicationId),
            "未設定でも書籍は集まるが、レビュー(読まれている度合い)が取れず並べ替えができない"));
        // 推薦本は定番の軸。**トピックの選択とは無関係**(固定クエリで「読むべき本」記事を掘る)
        integrations.Add(new Integration(
            IntegrationAxis.Classics, "書籍", "Qiita(推薦本)", CredentialNeed.Optional, "Qiita__AccessToken",
            !string.IsNullOrWhiteSpace(qiita.AccessToken),
            "未設定でも動く。トークンを入れると API の上限が 60 → 1000 リクエスト/時になる",
            qiita.Enabled));

        // --- イベント ---
        integrations.Add(new Integration(
            IntegrationAxis.Interests, "イベント", "connpass", CredentialNeed.Required, "Connpass__ApiKey",
            !string.IsNullOrWhiteSpace(connpass.ApiKey),
            "**利用申請が要る**。未設定だと connpass からは収集しない"));
        integrations.Add(new Integration(
            IntegrationAxis.Interests, "イベント", "Doorkeeper", CredentialNeed.Required, "Doorkeeper__AccessToken",
            !string.IsNullOrWhiteSpace(doorkeeper.AccessToken),
            "未設定だと Doorkeeper からは収集しない。API は alpha 扱いで破壊的変更がありうる"));
        integrations.Add(new Integration(
            IntegrationAxis.Interests, "イベント", "TECH PLAY", CredentialNeed.NotNeeded, null, true,
            "RSS なのでキーは要らない代わりに検索ができない(最新 50 件が流れてくるだけ)",
            !string.IsNullOrWhiteSpace(techPlay.FeedUrl)));

        // --- トピック ---
        integrations.Add(new Integration(
            IntegrationAxis.Both, "トピック(話題度)", "Qiita(トレンド)", CredentialNeed.NotNeeded, null, true,
            "直近記事のタグをいいね数で重み付けして話題度を出す"));
        integrations.Add(new Integration(
            IntegrationAxis.Both, "トピック(話題度)", "はてなブックマーク(人気エントリー)",
            CredentialNeed.NotNeeded, null, true,
            "人気エントリーの RSS をブックマーク数で重み付けして話題度を出す"));

        // --- 要約・翻訳 ---
        var hasClaudeCode = !string.IsNullOrWhiteSpace(claudeCodeToken);
        integrations.Add(new Integration(
            IntegrationAxis.Both, "要約・翻訳・語彙の仕分け", "Claude Code(サブスクの枠)", CredentialNeed.Optional, "CLAUDE_CODE_OAUTH_TOKEN",
            hasClaudeCode,
            "`claude setup-token` で発行する。**設定されていると Anthropic API より優先**され、"
            + "従量課金ではなくサブスクリプションの枠を使う"));
        integrations.Add(new Integration(
            IntegrationAxis.Both, "要約・翻訳・語彙の仕分け", "Anthropic API(従量課金)", CredentialNeed.Optional, "Anthropic__ApiKey",
            !string.IsNullOrWhiteSpace(anthropic.Value.ApiKey),
            "Claude Code のトークンが無いときの代わり。**両方とも未設定なら要約と翻訳のボタンが出ない**"));

        return integrations;
    }

    T Section<T>(string name) where T : new() =>
        configuration.GetSection(name).Get<T>() ?? new T();
}

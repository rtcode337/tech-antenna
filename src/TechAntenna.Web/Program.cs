using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure;
using TechAntenna.Infrastructure.Books;
using TechAntenna.Infrastructure.Bridge;
using TechAntenna.Infrastructure.Chiezo;
using TechAntenna.Infrastructure.Events;
using TechAntenna.Infrastructure.Feeds;
using TechAntenna.Infrastructure.Notifications;
using TechAntenna.Infrastructure.Persistence;
using TechAntenna.Infrastructure.Storage;
using TechAntenna.Infrastructure.Summarization;
using TechAntenna.Infrastructure.Topics;
using TechAntenna.Infrastructure.Trends;
using TechAntenna.Web;
using TechAntenna.Web.Components;
using TechAntenna.Web.Services;
using TechAntenna.Web.Workers;

// 収集先からの応答サイズの上限。収集先が侵害されて巨大な応答を返してきたとき、
// swap の無いホストでは読み込みで OOM になりアプリごと落ちるため、バッファを打ち切る
// (超過は HttpRequestException になり、そのソースの収集が失敗するだけで済む)。
// 実データはフィードで数百 KB、書籍 API で数十 KB なので 10MB は十分に余裕がある
const long MaxResponseBytes = 10 * 1024 * 1024;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// --- 記事収集 ---
builder.Services.Configure<CollectionOptions>(
    builder.Configuration.GetSection(CollectionOptions.SectionName));

builder.Services.AddHttpClient(FeedArticleSource.HttpClientName, ConfigureFeedClient);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TopicService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<InterestTopicCookie>();

builder.Services.AddHttpClient(QiitaTrendTopicSource.HttpClientName, client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "TechAntenna/0.1 (+https://github.com/rtcode337/tech-antenna)");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.MaxResponseContentBufferSize = MaxResponseBytes;
});
builder.Services.AddSingleton<ITrendTopicSource, QiitaTrendTopicSource>();
// はてブの人気エントリー RSS からも話題度を作る(その場で1リクエスト。収集済み記事に依存しない)
builder.Services.AddSingleton<ITrendTopicSource, HatenaHotentryTrendSource>();

// トピックの語彙(読み取り用のスナップショット)。**権威は DB** で、起動時に組み立てる。
// `topic-seed.json` は **DB が空のときに流し込む初期値** —— 語彙がまったく無いと
// LLM が寄せ先も親も選べず、同義の親が二重にできるため。読めなくても起動は止めない
var seedEntries = JsonTopicCatalogLoader.Load(
    Path.Combine(builder.Environment.ContentRootPath, "topic-seed.json")).Entries;
var topicCatalog = TopicCatalog.Empty;
builder.Services.AddSingleton(topicCatalog);

// antiforgery と Blazor が使う Data Protection の鍵。既定ではコンテナ内の一時領域に
// 置かれるため、作り直すたびに鍵が変わって発行済みトークンが無効になる。保存先が
// 指定されていれば(Docker 運用では DataProtection__KeysDirectory)そこへ永続化する
var keysDirectory = builder.Configuration["DataProtection:KeysDirectory"];
if (!string.IsNullOrWhiteSpace(keysDirectory))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));
}

// TLS を前段のリバースプロキシで終端する運用では、コンテナ自身は HTTP だけを待ち受ける。
// その場合リダイレクト先のポートが決まらず UseHttpsRedirection は警告を出して素通しに
// なるだけなので、HTTPS の待ち受けが分かるときだけ HTTPS 前提の設定を有効にする
var httpsConfigured =
    !string.IsNullOrWhiteSpace(builder.Configuration["HTTPS_PORT"])
    || !string.IsNullOrWhiteSpace(builder.Configuration["ASPNETCORE_HTTPS_PORTS"])
    || (builder.Configuration["ASPNETCORE_URLS"]?.Contains("https", StringComparison.OrdinalIgnoreCase) ?? false);

// 接続文字列があれば PostgreSQL、無ければメモリ上のストアで動かす(DB なしのお試し起動用)
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddSingleton<IArticleStore, InMemoryArticleStore>();
    builder.Services.AddSingleton<IEventStore, InMemoryEventStore>();
    builder.Services.AddSingleton<IBookStore, InMemoryBookStore>();
    builder.Services.AddSingleton<INewReleaseStore, InMemoryNewReleaseStore>();
    builder.Services.AddSingleton<ITagStore, InMemoryTagStore>();
    builder.Services.AddSingleton<ITopicStore, InMemoryTopicStore>();
    builder.Services.AddSingleton<IDigestStore, InMemoryDigestStore>();
    builder.Services.AddSingleton<ISecretStore, InMemorySecretStore>();
}
else
{
    builder.Services.AddDbContextFactory<TechAntennaDbContext>(o => o.UseNpgsql(connectionString));
    builder.Services.AddSingleton<IArticleStore, EfArticleStore>();
    builder.Services.AddSingleton<IEventStore, EfEventStore>();
    builder.Services.AddSingleton<IBookStore, EfBookStore>();
    builder.Services.AddSingleton<INewReleaseStore, EfNewReleaseStore>();
    builder.Services.AddSingleton<ITagStore, EfTagStore>();
    builder.Services.AddSingleton<ITopicStore, EfTopicStore>();
    builder.Services.AddSingleton<IDigestStore, EfDigestStore>();
    builder.Services.AddSingleton<ISecretStore, EfSecretStore>();
}

// 外部 API のキー・トークンの実行時解決。**設定の入口は画面(外部連携)だけ**で、
// 暗号化して DB に保存する。各収集元・LLM は実行のたびに引くので、設定した直後に効く
builder.Services.AddSingleton<ApiCredentials>();

var collection = builder.Configuration
    .GetSection(CollectionOptions.SectionName)
    .Get<CollectionOptions>() ?? new CollectionOptions();
foreach (var feed in collection.Feeds)
{
    builder.Services.AddSingleton<IArticleSource>(sp => new FeedArticleSource(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<TimeProvider>(),
        feed.Name,
        new Uri(feed.Url),
        topicCatalog,
        feed.Kind));
}

// arXiv も記事ソースの1つとして「トレンドの収集」で回る。選択中のトピックが検索語なので、
// 選択が空なら問い合わせない(Arxiv:Enabled=false で止められる)
// 話題の論文(Hugging Face Daily Papers)。**トピックの選択に依存しない**ので、
// 記事の RSS と同じ「巡回」の扱いにして `IArticleSource` として登録する
// —— 検索の arXiv / J-STAGE(`IPaperSource`)とはボタンも画面も分ける
var huggingFace = builder.Configuration
    .GetSection(HuggingFacePapersOptions.SectionName)
    .Get<HuggingFacePapersOptions>() ?? new HuggingFacePapersOptions();
if (huggingFace.Enabled)
{
    builder.Services.AddHttpClient(HuggingFacePapersSource.HttpClientName, ConfigureFeedClient);
    builder.Services.AddSingleton<IArticleSource>(sp => new HuggingFacePapersSource(
        sp.GetRequiredService<IHttpClientFactory>(), topicCatalog));
}

var arxiv = builder.Configuration
    .GetSection(ArxivOptions.SectionName)
    .Get<ArxivOptions>() ?? new ArxivOptions();
if (arxiv.Enabled)
{
    builder.Services.AddHttpClient(ArxivPaperSource.HttpClientName, ConfigureFeedClient);
    builder.Services.AddSingleton<IPaperSource>(sp => new ArxivPaperSource(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<TimeProvider>(),
        sp.GetRequiredService<ITopicStore>(),
        topicCatalog,
        arxiv.MaxResults,
        TimeSpan.FromSeconds(arxiv.DelaySeconds)));
}

// J-STAGE は日本語の論文。arXiv(英語)と役割が違うので両方登録する
var jstage = builder.Configuration
    .GetSection(JstageOptions.SectionName)
    .Get<JstageOptions>() ?? new JstageOptions();
if (jstage.Enabled)
{
    builder.Services.AddHttpClient(JstagePaperSource.HttpClientName, ConfigureFeedClient);
    builder.Services.AddSingleton<IPaperSource>(sp => new JstagePaperSource(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<TimeProvider>(),
        sp.GetRequiredService<ITopicStore>(),
        topicCatalog,
        jstage.MaxResults,
        jstage.WithinYears,
        TimeSpan.FromSeconds(jstage.DelaySeconds)));
}

// はてなブックマークの件数 API(キー不要)。全ソース横断の人気指標として、
// 記事収集の最後とトピック収集(話題度の材料)で直近の記事・ニュースの件数を引き直す
builder.Services.AddHttpClient(HatenaBookmarkCounts.HttpClientName, ConfigureFeedClient);
builder.Services.AddSingleton(sp => new HatenaBookmarkCounts(
    sp.GetRequiredService<IHttpClientFactory>()));
builder.Services.AddSingleton<BookmarkCountRefresher>();

builder.Services.AddSingleton<ArticleCollectionRunner>();

// --- イベント収集(connpass)---
// **キーの有無で登録を分岐しない**(画面から実行時に設定できるため)。キーは
// クライアント生成のたびに ApiCredentials から解決し、無ければソース側がスキップする
var connpass = builder.Configuration
    .GetSection(ConnpassOptions.SectionName)
    .Get<ConnpassOptions>() ?? new ConnpassOptions();
builder.Services.AddHttpClient(ConnpassEventSource.HttpClientName, (sp, client) =>
{
    // v2 は X-API-Key と User-Agent が必須。連絡先はリポジトリ URL のみ
    var apiKey = sp.GetRequiredService<ApiCredentials>().Get("Connpass:ApiKey");
    if (!string.IsNullOrWhiteSpace(apiKey))
    {
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
    }
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "TechAntenna/0.1 (+https://github.com/rtcode337/tech-antenna)");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.MaxResponseContentBufferSize = MaxResponseBytes;
});

builder.Services.AddSingleton<IEventSource>(sp => new ConnpassEventSource(
    sp.GetRequiredService<IHttpClientFactory>(),
    sp.GetRequiredService<TimeProvider>(),
    connpass.Keywords,
    topicStore: sp.GetRequiredService<ITopicStore>(),
    catalog: topicCatalog,
    apiKeyProvider: () => sp.GetRequiredService<ApiCredentials>().Get("Connpass:ApiKey"),
    // 購読の名簿も画面から直せるので、キーと同じく実行のたびに解決する
    followedProvider: () => FollowSettings.Resolve(sp.GetRequiredService<ApiCredentials>())));

// --- イベント収集(connpass の面掃き)---
// **検索語も名簿も使わず、月ごとに全件なめて参加者数で切る。** 名前を知らない大型イベントを
// 拾える唯一の経路だが、1か月ぶんで数十リクエストかかるので**既定では動かさない**。
// **登録は無条件で、走らせるかどうかは実行のたびに画面の設定を読む**(キーと同じ扱い)——
// 起動時に見て分岐すると、画面で入れても再起動するまで効かない
if (connpass.Sweep is { Months: > 0 } sweep)
{
    builder.Services.AddSingleton<IEventSource>(sp => new ConnpassSweepEventSource(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<TimeProvider>(),
        sweep.MinParticipants,
        sweep.Months,
        TimeSpan.FromSeconds(sweep.DelayBetweenRequestsSeconds),
        catalog: topicCatalog,
        apiKeyProvider: () => sp.GetRequiredService<ApiCredentials>().Get("Connpass:ApiKey"),
        enabledProvider: () => SweepSettings.IsEnabled(
            sp.GetRequiredService<ApiCredentials>(), SweepSettings.ConnpassName)));
}

// --- イベント収集(Doorkeeper)---
var doorkeeper = builder.Configuration
    .GetSection(DoorkeeperOptions.SectionName)
    .Get<DoorkeeperOptions>() ?? new DoorkeeperOptions();
// **キーワードの有無で登録を分岐しない**(connpass と同じ)。検索語は選択中のトピックから
// 取るし、グループの購読は検索語を使わない —— appsettings の Keywords を空にしたら
// 購読まで止まる、という繋がりを作らないため。トークン未設定なら収集元側がスキップする
{
    builder.Services.AddHttpClient(DoorkeeperEventSource.HttpClientName, (sp, client) =>
    {
        var accessToken = sp.GetRequiredService<ApiCredentials>().Get("Doorkeeper:AccessToken");
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
        }
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "TechAntenna/0.1 (+https://github.com/rtcode337/tech-antenna)");
        client.Timeout = TimeSpan.FromSeconds(30);
        client.MaxResponseContentBufferSize = MaxResponseBytes;
    });

    builder.Services.AddSingleton<IEventSource>(sp => new DoorkeeperEventSource(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<TimeProvider>(),
        doorkeeper.Keywords,
        topicStore: sp.GetRequiredService<ITopicStore>(),
        catalog: topicCatalog,
        accessTokenProvider: () =>
            sp.GetRequiredService<ApiCredentials>().Get("Doorkeeper:AccessToken"),
        followedProvider: () => FollowSettings.Resolve(sp.GetRequiredService<ApiCredentials>())));

    // --- イベント収集(Doorkeeper の面掃き)---
    // connpass の面掃きと同じ役割。**`q` を付けずに期間で引く**ので、こちらが名前を
    // 知らないイベントも拾える。**既定では動かさない**(Doorkeeper:Sweep で明示する)
    if (doorkeeper.Sweep is { Months: > 0 } doorkeeperSweep)
    {
        builder.Services.AddSingleton<IEventSource>(sp => new DoorkeeperSweepEventSource(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            doorkeeperSweep.MinParticipants,
            doorkeeperSweep.Months,
            TimeSpan.FromSeconds(doorkeeperSweep.DelayBetweenRequestsSeconds),
            accessTokenProvider: () =>
                sp.GetRequiredService<ApiCredentials>().Get("Doorkeeper:AccessToken"),
            enabledProvider: () => SweepSettings.IsEnabled(
                sp.GetRequiredService<ApiCredentials>(), SweepSettings.DoorkeeperName)));
    }
}

// --- イベント収集(TECH PLAY の RSS)---
// キーも申請も要らない代わりに検索ができず、最新のイベントが流れてくるだけなので、
// 巡回して差分を溜める。企業主催のウェビナーはこの経路が一番厚い
var techPlay = builder.Configuration
    .GetSection(TechPlayOptions.SectionName)
    .Get<TechPlayOptions>() ?? new TechPlayOptions();
if (Uri.TryCreate(techPlay.FeedUrl, UriKind.Absolute, out var techPlayFeedUrl))
{
    builder.Services.AddSingleton<IEventSource>(sp => new TechPlayEventSource(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<TimeProvider>(),
        techPlayFeedUrl,
        topicCatalog));
}

// 記事の言及数(注目度の3つめの材料)。**外部は叩かない**ので、収集の最後に必ず通す
builder.Services.AddSingleton(sp => new EventMentionRefresher(
    sp.GetRequiredService<IEventStore>(),
    sp.GetRequiredService<IArticleStore>(),
    topicCatalog,
    sp.GetRequiredService<TimeProvider>()));

builder.Services.AddSingleton<EventCollectionRunner>();

// --- 書籍収集(Google Books + openBD)---
builder.Services.Configure<BooksOptions>(
    builder.Configuration.GetSection(BooksOptions.SectionName));

var books = builder.Configuration
    .GetSection(BooksOptions.SectionName)
    .Get<BooksOptions>() ?? new BooksOptions();
// 検索語は選択中のトピックなので、設定を見ずに常に登録する(選択が空なら何もしないだけ)。
// API キーが無くても登録するのは、ボタンごと消えるより 429 の理由を画面に出したほうが
// 打つ手が分かるため(GoogleBooksCatalog がキー未設定かどうかを見分けて投げる)
builder.Services.AddHttpClient(GoogleBooksCatalog.HttpClientName, ConfigureBookClient);

builder.Services.AddSingleton<IBookCatalog>(sp => new GoogleBooksCatalog(
    sp.GetRequiredService<IHttpClientFactory>(),
    sp.GetRequiredService<TimeProvider>(),
    () => sp.GetRequiredService<ApiCredentials>().Get("Books:GoogleBooksApiKey"),
    catalog: topicCatalog));

// openBD はキーワード検索を持たないため、検索結果を補う後段として使う
if (books.UseOpenBd)
{
    builder.Services.AddHttpClient(OpenBdEnricher.HttpClientName, ConfigureBookClient);
    builder.Services.AddSingleton<IBookEnricher, OpenBdEnricher>();
}

// 楽天ブックスはレビュー(読まれている度合い)専用の後段。アプリ ID は画面から
// 実行時に設定できるので常に登録し、無ければエンリッチャ側が何もしない
builder.Services.Configure<RakutenOptions>(
    builder.Configuration.GetSection(RakutenOptions.SectionName));

var rakuten = builder.Configuration
    .GetSection(RakutenOptions.SectionName)
    .Get<RakutenOptions>() ?? new RakutenOptions();
builder.Services.AddHttpClient(RakutenBooksEnricher.HttpClientName, ConfigureBookClient);
builder.Services.AddSingleton<IBookEnricher>(sp => new RakutenBooksEnricher(
    sp.GetRequiredService<IHttpClientFactory>(),
    () => sp.GetRequiredService<ApiCredentials>().Get("Rakuten:ApplicationId"),
    () => sp.GetRequiredService<ApiCredentials>().Get("Rakuten:AccessKey"),
    TimeSpan.FromSeconds(rakuten.DelaySeconds)));

// 書影の補完は**最後**に置く。openBD(技術書の書影をほとんど持たない)と楽天
// (レビューと同じ応答に書影が入るので追加コスト無し)で埋まらなかったぶんだけを、
// Google Books へ ISBN で引きに行く —— 1 冊 1 リクエストなので、他で埋まるなら引かない
builder.Services.AddSingleton<IBookEnricher>(sp => new GoogleBooksCoverEnricher(
    sp.GetRequiredService<IHttpClientFactory>(),
    () => sp.GetRequiredService<ApiCredentials>().Get("Books:GoogleBooksApiKey"),
    sp.GetRequiredService<ILogger<GoogleBooksCoverEnricher>>(),
    TimeSpan.FromSeconds(books.CoverLookupDelaySeconds)));

// 「読むべき技術書」を挙げた記事から薦められている本を拾う。書籍の検索とは独立した経路
var qiita = builder.Configuration
    .GetSection(QiitaOptions.SectionName)
    .Get<QiitaOptions>() ?? new QiitaOptions();
if (qiita.Enabled && qiita.Queries.Count > 0)
{
    builder.Services.AddHttpClient(QiitaBookRecommendationSource.HttpClientName, ConfigureBookClient);
    builder.Services.AddSingleton<IBookRecommendationSource>(sp => new QiitaBookRecommendationSource(
        sp.GetRequiredService<IHttpClientFactory>(),
        qiita.Queries,
        qiita.MaxArticles,
        () => sp.GetRequiredService<ApiCredentials>().Get("Qiita:AccessToken"),
        TimeSpan.FromSeconds(qiita.DelaySeconds)));
}

builder.Services.AddSingleton<BookCollectionRunner>();
// 定番(推薦本)の収集。書籍の収集と分けてある —— トピックの選択に依存しない第三の軸
builder.Services.AddSingleton<ClassicsCollectionRunner>();

// --- 出版トレンド(最近出た本からテーマを数える)---
// **キーも検索語も要らない**(NDC と刊行日で引く)ので、トレンドの軸に置ける。
// 集めた本は書籍(Book)とは別の表に入る —— 読ませるためではなく数えるための観測
builder.Services.Configure<NewReleaseOptions>(
    builder.Configuration.GetSection(NewReleaseOptions.SectionName));

var newReleases = builder.Configuration
    .GetSection(NewReleaseOptions.SectionName)
    .Get<NewReleaseOptions>() ?? new NewReleaseOptions();
if (newReleases.Enabled)
{
    builder.Services.AddHttpClient(NdlSearchNewReleaseSource.HttpClientName, ConfigureFeedClient);
    builder.Services.AddSingleton<INewReleaseSource>(sp => new NdlSearchNewReleaseSource(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<TimeProvider>(),
        topicCatalog,
        newReleases.NdcCodes,
        newReleases.MaxItems,
        TimeSpan.FromSeconds(newReleases.DelayBetweenPagesSeconds)));
}

builder.Services.AddSingleton<NewReleaseCollectionRunner>();
// 論文は記事と別のボタン(検索なので収集対象のトピックが要る)
builder.Services.AddSingleton<PaperCollectionRunner>();
// 語彙のスナップショット(TopicCatalog)を DB から組み直す。起動時と整備のあとに呼ぶ
builder.Services.AddSingleton<TopicCatalogRefresher>();
// 保存済みデータのタグを数え直してタグの一覧へ反映する(収集と整備の両方から呼ぶ)
builder.Services.AddSingleton<TagObserver>();
// 語彙の初期投入(DB が空のときだけ topic-seed.json を流し込む)
builder.Services.AddSingleton<TopicSeeder>();
// トピックを別のトピックへ寄せる(画面からの手直しと、LLM の統合パスで共有する)
builder.Services.AddSingleton<TopicMerger>();
// 語彙と仕分けのファイル持ち出し・取り込み(本番で LLM に仕分けさせた結果を別の環境で使う)
builder.Services.AddSingleton<TopicExporter>();
builder.Services.AddSingleton<TopicImporter>();
// トピックの整備。話題度の取り直し(LLM なし)とタグの仕分け直し(LLM あり)の2つの入口を持つ
builder.Services.AddSingleton<TopicMaintenanceRunner>();
// タグの正規化規則を変えたときに保存済みデータを追従させる(外部へは出ないので常に登録する)
builder.Services.AddSingleton<TagRenormalizationRunner>();
// 外部連携の一覧(/integrations)。設定を読むだけなので常に登録する
builder.Services.AddSingleton<IntegrationCatalog>();

// --- LLM 要約 ---
// 方式は2つあり、Claude Code(サブスクリプションの枠)を優先する。両方未設定なら要約しない。
//  1. Claude Code: OAuth トークンがあるとき。従量課金にならない。CLI はこのイメージに
//     入っておらず、別コンテナの CLI ブリッジ(chiezo-bridge)へ HTTP で頼む
//  2. Anthropic API: API キーがあるとき。呼び出しの固定費が小さい
// キーはどちらも画面(外部連携)から設定する
// **どちらを使うかは起動時ではなく実行のたびに LlmGateway が決める** —— キーは画面
// (外部連携)から設定でき、再起動なしで効かせるため。未設定でもボタンは disabled で出る
builder.Services.Configure<AnthropicOptions>(
    builder.Configuration.GetSection(AnthropicOptions.SectionName));
builder.Services.Configure<ClaudeCodeOptions>(
    builder.Configuration.GetSection(ClaudeCodeOptions.SectionName));
builder.Services.Configure<DigestOptions>(
    builder.Configuration.GetSection(DigestOptions.SectionName));
// Chiezo(LAN 内の知識サーバー)経由で相手を選ぶ経路。**URL を設定したときだけ使う** ——
// あちらが Gemini・Claude Code・推論サーバの鍵を持っているので、こちらは相手を選ぶだけでよい
builder.Services.Configure<ChiezoOptions>(
    builder.Configuration.GetSection(ChiezoOptions.SectionName));
builder.Services.AddHttpClient(ChiezoAiClient.HttpClientName);
builder.Services.AddSingleton<ChiezoAi>();
// ブリッジへの1回の待ちは呼び出しごとに決める(CliBridgeClient が上限秒数を設定する)ので、
// ここでは名前を登録するだけ
builder.Services.AddHttpClient(CliBridgeClient.HttpClientName);

builder.Services.AddSingleton<LlmGateway>();

// 応答を圧縮する。**トピックのツリーは全件(1000 行超)を出すので HTML が 1MB を超える** ——
// 素のままだとスマホや外出先の回線で待たされる(Kestrel は既定で圧縮しない)。
// HTTPS では既定で無効のまま(EnableForHttps を触らない) —— 圧縮と TLS の併用には
// BREACH の懸念があり、TLS を終端するのは前段のプロキシなのでそちらに任せる
builder.Services.AddResponseCompression();

// 画面の手動ボタンからも呼ぶので、要約が未設定でも常に登録する(未設定なら何もしない)
builder.Services.AddSingleton<SummaryRunner>();
builder.Services.AddSingleton<TitleTranslationRunner>();
builder.Services.AddSingleton<DigestRunner>();

// **定期実行のワーカーは1本だけ。** 設定した時刻になったら、チェックの入ったジョブを
// 決まった順(ScheduledJobs)で通しで走らせる。ワーカーは常に登録し、時刻もオン/オフも
// 周回ごとに画面設定を見る(既定は時刻なし = 走らない)ので、切り替えは再起動なしで効く。
// **登録はここ**(サマリーまで含めた全 Runner が出そろってからでないと組み立てられない)
builder.Services.AddSingleton<ScheduledJobs>();
// 定期実行の中身。**時刻で走るのも画面の「今すぐ実行」も同じ Runner を通る**
builder.Services.AddSingleton<ScheduleRunner>();
builder.Services.AddHostedService<ScheduleWorker>();

// 今日のサマリーの ntfy 通知。**接続先(BaseUrl / Topic / トークン)は画面から実行時に
// 設定できるので常に登録し**、送信のたびに解決する —— 未設定・無効なら送らないだけ。
// 通知のオン/オフ(Ntfy:Enabled)は接続先とは独立の設定(設定画面のチェックボックス)。
// ClickUrl(通知タップで開くホームの公開 URL)だけはデプロイ側の事実なので環境変数のまま
builder.Services.Configure<NtfyOptions>(
    builder.Configuration.GetSection(NtfyOptions.SectionName));
var ntfy = builder.Configuration
    .GetSection(NtfyOptions.SectionName)
    .Get<NtfyOptions>() ?? new NtfyOptions();
builder.Services.AddHttpClient(NtfyDigestNotifier.HttpClientName, ConfigureFeedClient);
builder.Services.AddSingleton<IDigestNotifier>(sp => new NtfyDigestNotifier(
    sp.GetRequiredService<IHttpClientFactory>(),
    () =>
    {
        var credentials = sp.GetRequiredService<ApiCredentials>();
        if (!NtfySettings.IsEnabled(credentials)
            || credentials.Get(NtfySettings.TopicName) is not { } topic)
        {
            return null;
        }

        return new NtfyTarget(
            credentials.Get(NtfySettings.BaseUrlName) ?? NtfySettings.DefaultBaseUrl,
            topic,
            credentials.Get(NtfySettings.AccessTokenName),
            string.IsNullOrWhiteSpace(ntfy.ClickUrl) ? null : ntfy.ClickUrl);
    }));

var app = builder.Build();

// 個人運用前提のため、未適用のマイグレーションは起動時に適用する
if (!string.IsNullOrWhiteSpace(connectionString))
{
    await using var db = await app.Services
        .GetRequiredService<IDbContextFactory<TechAntennaDbContext>>()
        .CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

// **語彙の権威は DB。** DB が空なら topic-seed.json を初期値として流し込み、
// そのうえで DB からカタログ(読み取り用のスナップショット)を組み立てる。
// 起動のたびに組み直すので、コンテナを作り直しても語彙は DB から復元される
{
    await app.Services.GetRequiredService<TopicSeeder>().SeedAsync(seedEntries);
    await app.Services.GetRequiredService<TopicCatalogRefresher>().RefreshAsync();
    // 画面から設定した API キーを DB から読み込む(マイグレーション適用後に呼ぶ)
    await app.Services.GetRequiredService<ApiCredentials>().RefreshAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    if (httpsConfigured)
    {
        app.UseHsts();
    }
}
// 圧縮は応答を包むので、静的アセットやコンポーネントより前に置く
app.UseResponseCompression();

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (httpsConfigured)
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapStaticAssets();

// 語彙と仕分けの持ち出し。**ダウンロードは GET 1本で足りる**ので、Blazor のフォームではなく
// 最小 API に置く(Content-Disposition を付けてファイルとして落とさせるため)。
// パスを /settings/topics/… の下に置かないのは、`/settings/topics/{tag}`(トピックの詳細)と
// 紛れないようにするため
app.MapGet("/export/topics.json", async (
    TopicExporter exporter, TimeProvider clock, CancellationToken cancellationToken) =>
{
    var file = await exporter.BuildAsync(cancellationToken);
    var json = Encoding.UTF8.GetBytes(TopicExportJson.Serialize(file));

    return Results.File(
        json,
        "application/json",
        $"tech-antenna-topics-{JapanTime.FormatStamp(clock.GetUtcNow())}.json");
});

// 収集対象のチェックを**押した瞬間に保存する**入口(wwwroot/topic-select.js が fetch で叩く)。
// JS が無い環境では従来どおり `/settings/topics` のフォーム(「選択を保存」)が効くので、
// これは上乗せ —— 1000 行のページで 1 個チェックするたびに再読み込みしないためにある。
//
// **1 件だけを切り替える**(一覧を丸ごと置き換える保存とは別の経路)。
// パスを `/settings/topics/…` の下に置かないのは、`/settings/topics/{tag}`(トピックの詳細)と
// 紛れないようにするため —— 書き出しの `/export/topics.json` と同じ理由。
//
// **フォーム形式で受ける**のは、`UseAntiforgery()` が自動で検証してくれるのがこの形だから
// (JSON の本文だと検証されない)。トークンは画面のフォームの隠しフィールドから写して送る。
app.MapPost("/api/topics/select", async (
    IFormCollection form, ITopicStore topics, CancellationToken cancellationToken) =>
{
    var key = form["key"].ToString();
    var selected = form["selected"] == "true";

    if (string.IsNullOrWhiteSpace(key))
    {
        return Results.BadRequest(new { error = "トピックが指定されていません。" });
    }

    if (!await topics.SetSelectedAsync(key, selected, cancellationToken))
    {
        // 語彙から消えた語をチェックしようとした場合。**画面側で元に戻せるよう理由を返す**
        return Results.NotFound(new { error = $"「{key}」は語彙にありませんでした。" });
    }

    // 「収集対象 N 件」を画面に出すため、保存のたびに数え直す(語彙は数百件なので安い)
    var count = (await topics.GetSelectedAsync(cancellationToken)).Count;

    return Results.Ok(new { key, selected, count });
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// 記事・論文の収集先へ共通で使う HttpClient の設定。
// 連絡先はリポジトリ URL のみを名乗る(個人のメールアドレスは入れない)
static void ConfigureFeedClient(HttpClient client)
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "TechAntenna/0.1 (+https://github.com/rtcode337/tech-antenna)");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.MaxResponseContentBufferSize = MaxResponseBytes;
}

// 書籍まわりの収集先へ共通で使う HttpClient の設定。
// 連絡先はリポジトリ URL のみを名乗る(個人のメールアドレスは入れない)
static void ConfigureBookClient(HttpClient client)
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "TechAntenna/0.1 (+https://github.com/rtcode337/tech-antenna)");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.MaxResponseContentBufferSize = MaxResponseBytes;
}

using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure;
using TechAntenna.Infrastructure.Books;
using TechAntenna.Infrastructure.Events;
using TechAntenna.Infrastructure.Feeds;
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
    builder.Services.AddSingleton<ITagStore, InMemoryTagStore>();
    builder.Services.AddSingleton<ITopicStore, InMemoryTopicStore>();
}
else
{
    builder.Services.AddDbContextFactory<TechAntennaDbContext>(o => o.UseNpgsql(connectionString));
    builder.Services.AddSingleton<IArticleStore, EfArticleStore>();
    builder.Services.AddSingleton<IEventStore, EfEventStore>();
    builder.Services.AddSingleton<IBookStore, EfBookStore>();
    builder.Services.AddSingleton<ITagStore, EfTagStore>();
    builder.Services.AddSingleton<ITopicStore, EfTopicStore>();
}

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
var connpass = builder.Configuration
    .GetSection(ConnpassOptions.SectionName)
    .Get<ConnpassOptions>() ?? new ConnpassOptions();
if (!string.IsNullOrWhiteSpace(connpass.ApiKey))
{
    builder.Services.AddHttpClient(ConnpassEventSource.HttpClientName, client =>
    {
        // v2 は X-API-Key と User-Agent が必須。連絡先はリポジトリ URL のみ
        client.DefaultRequestHeaders.Add("X-API-Key", connpass.ApiKey);
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
        catalog: topicCatalog));
}

// --- イベント収集(Doorkeeper)---
var doorkeeper = builder.Configuration
    .GetSection(DoorkeeperOptions.SectionName)
    .Get<DoorkeeperOptions>() ?? new DoorkeeperOptions();
if (!string.IsNullOrWhiteSpace(doorkeeper.AccessToken) && doorkeeper.Keywords.Count > 0)
{
    builder.Services.AddHttpClient(DoorkeeperEventSource.HttpClientName, client =>
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", doorkeeper.AccessToken);
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
        catalog: topicCatalog));
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

builder.Services.AddSingleton<EventCollectionRunner>();

// **定期実行は AutoRun のときだけで、既定は false**。消し忘れたサーバーが
// 収集先を叩き続けないようにするため。既定では手動(画面のボタン)だけで動く
if (collection.AutoRun)
{
    builder.Services.AddHostedService<ArticleCollectionWorker>();
    builder.Services.AddHostedService<EventCollectionWorker>();
}

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
    books.GoogleBooksApiKey,
    catalog: topicCatalog));

// openBD はキーワード検索を持たないため、検索結果を補う後段として使う
if (books.UseOpenBd)
{
    builder.Services.AddHttpClient(OpenBdEnricher.HttpClientName, ConfigureBookClient);
    builder.Services.AddSingleton<IBookEnricher, OpenBdEnricher>();
}

// 楽天ブックスはレビュー(読まれている度合い)専用の後段。アプリ ID が無ければ登録しない
builder.Services.Configure<RakutenOptions>(
    builder.Configuration.GetSection(RakutenOptions.SectionName));

var rakuten = builder.Configuration
    .GetSection(RakutenOptions.SectionName)
    .Get<RakutenOptions>() ?? new RakutenOptions();
if (rakuten.ApplicationId.Length > 0)
{
    builder.Services.AddHttpClient(RakutenBooksEnricher.HttpClientName, ConfigureBookClient);
    builder.Services.AddSingleton<IBookEnricher>(sp => new RakutenBooksEnricher(
        sp.GetRequiredService<IHttpClientFactory>(),
        rakuten.ApplicationId,
        rakuten.AccessKey,
        TimeSpan.FromSeconds(rakuten.DelaySeconds)));
}

if (books.AutoRun)
{
    builder.Services.AddHostedService<BookCollectionWorker>();
}

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
        qiita.AccessToken,
        TimeSpan.FromSeconds(qiita.DelaySeconds)));
}

builder.Services.AddSingleton<BookCollectionRunner>();
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
//  1. Claude Code のヘッドレス実行: CLAUDE_CODE_OAUTH_TOKEN があるとき。従量課金にならない
//  2. Anthropic API: Anthropic__ApiKey があるとき。呼び出しの固定費が小さい
builder.Services.Configure<AnthropicOptions>(
    builder.Configuration.GetSection(AnthropicOptions.SectionName));
builder.Services.Configure<ClaudeCodeOptions>(
    builder.Configuration.GetSection(ClaudeCodeOptions.SectionName));

var anthropic = builder.Configuration
    .GetSection(AnthropicOptions.SectionName)
    .Get<AnthropicOptions>() ?? new AnthropicOptions();
var claudeCode = builder.Configuration
    .GetSection(ClaudeCodeOptions.SectionName)
    .Get<ClaudeCodeOptions>() ?? new ClaudeCodeOptions();

// トークンは CLI が環境変数から直接読む。アプリは有無だけを見る
var hasClaudeCodeToken = !string.IsNullOrWhiteSpace(
    builder.Configuration["CLAUDE_CODE_OAUTH_TOKEN"]);

if (hasClaudeCodeToken)
{
    builder.Services.AddSingleton<IProcessRunner, SystemProcessRunner>();
    builder.Services.AddSingleton<ISummarizer>(sp => new ClaudeCodeSummarizer(
        sp.GetRequiredService<IProcessRunner>(),
        claudeCode.ExecutablePath,
        string.IsNullOrWhiteSpace(claudeCode.Model) ? null : claudeCode.Model,
        TimeSpan.FromSeconds(claudeCode.TimeoutSeconds)));
    // 論文タイトルの翻訳も同じ方式を使う(要約と同じ枠・同じ CLI)
    builder.Services.AddSingleton<ITitleTranslator>(sp => new ClaudeCodeTitleTranslator(
        sp.GetRequiredService<IProcessRunner>(),
        claudeCode.ExecutablePath,
        string.IsNullOrWhiteSpace(claudeCode.Model) ? null : claudeCode.Model,
        TimeSpan.FromSeconds(claudeCode.TimeoutSeconds)));
    // カタログに無いタグの分類と、用語の一言説明も同じ方式(どちらも仕分けの中で動く)。
    // 1 つのインスタンスを 2 つの抽象として配る —— 分類の応答に説明を相乗りさせるので、
    // 実装も設定も分ける理由が無い
    builder.Services.AddSingleton(sp => new ClaudeCodeTopicClassifier(
        sp.GetRequiredService<IProcessRunner>(),
        claudeCode.ExecutablePath,
        string.IsNullOrWhiteSpace(claudeCode.Model) ? null : claudeCode.Model,
        TimeSpan.FromSeconds(claudeCode.TimeoutSeconds)));
    builder.Services.AddSingleton<ITopicClassifier>(
        sp => sp.GetRequiredService<ClaudeCodeTopicClassifier>());
    builder.Services.AddSingleton<ITopicDescriber>(
        sp => sp.GetRequiredService<ClaudeCodeTopicClassifier>());
    builder.Services.AddSingleton<ITopicMergeAdvisor>(
        sp => sp.GetRequiredService<ClaudeCodeTopicClassifier>());
}
else if (!string.IsNullOrWhiteSpace(anthropic.ApiKey))
{
    builder.Services.AddSingleton<ISummarizer>(
        new AnthropicSummarizer(anthropic.ApiKey, anthropic.Model));
    builder.Services.AddSingleton<ITitleTranslator>(
        new AnthropicTitleTranslator(anthropic.ApiKey, anthropic.Model));
    var topicClassifier = new AnthropicTopicClassifier(anthropic.ApiKey, anthropic.Model);
    builder.Services.AddSingleton<ITopicClassifier>(topicClassifier);
    builder.Services.AddSingleton<ITopicDescriber>(topicClassifier);
    builder.Services.AddSingleton<ITopicMergeAdvisor>(topicClassifier);
}

// 応答を圧縮する。**トピックのツリーは全件(1000 行超)を出すので HTML が 1MB を超える** ——
// 素のままだとスマホや外出先の回線で待たされる(Kestrel は既定で圧縮しない)。
// HTTPS では既定で無効のまま(EnableForHttps を触らない) —— 圧縮と TLS の併用には
// BREACH の懸念があり、TLS を終端するのは前段のプロキシなのでそちらに任せる
builder.Services.AddResponseCompression();

// 画面の手動ボタンからも呼ぶので、要約が未設定でも常に登録する(未設定なら何もしない)
builder.Services.AddSingleton<SummaryRunner>();
builder.Services.AddSingleton<TitleTranslationRunner>();

if (anthropic.AutoRun && (hasClaudeCodeToken || !string.IsNullOrWhiteSpace(anthropic.ApiKey)))
{
    builder.Services.AddHostedService<SummaryWorker>();
}

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
// パスを /topics/… の下に置かないのは、`/topics/{tag}`(トピックの詳細)と紛れないようにするため
app.MapGet("/export/topics.json", async (
    TopicExporter exporter, TimeProvider clock, CancellationToken cancellationToken) =>
{
    var file = await exporter.BuildAsync(cancellationToken);
    var json = Encoding.UTF8.GetBytes(TopicExportJson.Serialize(file));

    return Results.File(
        json,
        "application/json",
        $"tech-antenna-topics-{clock.GetUtcNow():yyyyMMdd-HHmm}.json");
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

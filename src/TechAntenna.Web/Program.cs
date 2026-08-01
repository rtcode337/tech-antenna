using System.Net.Http.Headers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Books;
using TechAntenna.Infrastructure.Events;
using TechAntenna.Infrastructure.Feeds;
using TechAntenna.Infrastructure.Persistence;
using TechAntenna.Infrastructure.Storage;
using TechAntenna.Infrastructure.Summarization;
using TechAntenna.Web;
using TechAntenna.Web.Components;
using TechAntenna.Web.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// --- 記事収集 ---
builder.Services.Configure<CollectionOptions>(
    builder.Configuration.GetSection(CollectionOptions.SectionName));

builder.Services.AddHttpClient(FeedArticleSource.HttpClientName, client =>
{
    // 収集先が連絡を取れるようリポジトリ URL を名乗る(個人のメールアドレスは入れない)
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "TechAntenna/0.1 (+https://github.com/rtcode337/tech-antenna)");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<TopicService>();

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
}
else
{
    builder.Services.AddDbContextFactory<TechAntennaDbContext>(o => o.UseNpgsql(connectionString));
    builder.Services.AddSingleton<IArticleStore, EfArticleStore>();
    builder.Services.AddSingleton<IEventStore, EfEventStore>();
    builder.Services.AddSingleton<IBookStore, EfBookStore>();
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
        new Uri(feed.Url)));
}

builder.Services.AddHostedService<ArticleCollectionWorker>();

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
    });

    builder.Services.AddSingleton<IEventSource>(sp => new ConnpassEventSource(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<TimeProvider>(),
        connpass.Keywords));
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
    });

    builder.Services.AddSingleton<IEventSource>(sp => new DoorkeeperEventSource(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<TimeProvider>(),
        doorkeeper.Keywords));
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
        techPlayFeedUrl));
}

builder.Services.AddHostedService<EventCollectionWorker>();

// --- 書籍収集(Google Books + openBD)---
builder.Services.Configure<BooksOptions>(
    builder.Configuration.GetSection(BooksOptions.SectionName));

var books = builder.Configuration
    .GetSection(BooksOptions.SectionName)
    .Get<BooksOptions>() ?? new BooksOptions();
if (books.Keywords.Count > 0)
{
    builder.Services.AddHttpClient(GoogleBooksCatalog.HttpClientName, ConfigureBookClient);

    builder.Services.AddSingleton<IBookCatalog>(sp => new GoogleBooksCatalog(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<TimeProvider>(),
        books.GoogleBooksApiKey));

    // openBD はキーワード検索を持たないため、検索結果を補う後段として使う
    if (books.UseOpenBd)
    {
        builder.Services.AddHttpClient(OpenBdEnricher.HttpClientName, ConfigureBookClient);
        builder.Services.AddSingleton<IBookEnricher, OpenBdEnricher>();
    }

    builder.Services.AddHostedService<BookCollectionWorker>();
}

// --- LLM 要約(Anthropic API)---
builder.Services.Configure<AnthropicOptions>(
    builder.Configuration.GetSection(AnthropicOptions.SectionName));

var anthropic = builder.Configuration
    .GetSection(AnthropicOptions.SectionName)
    .Get<AnthropicOptions>() ?? new AnthropicOptions();
if (!string.IsNullOrWhiteSpace(anthropic.ApiKey))
{
    builder.Services.AddSingleton<ISummarizer>(
        new AnthropicSummarizer(anthropic.ApiKey, anthropic.Model));
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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    if (httpsConfigured)
    {
        app.UseHsts();
    }
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (httpsConfigured)
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// 書籍まわりの収集先へ共通で使う HttpClient の設定。
// 連絡先はリポジトリ URL のみを名乗る(個人のメールアドレスは入れない)
static void ConfigureBookClient(HttpClient client)
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "TechAntenna/0.1 (+https://github.com/rtcode337/tech-antenna)");
    client.Timeout = TimeSpan.FromSeconds(30);
}

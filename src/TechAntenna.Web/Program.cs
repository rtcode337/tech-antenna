using Microsoft.EntityFrameworkCore;
using TechAntenna.Core.Abstractions;
using TechAntenna.Infrastructure.Feeds;
using TechAntenna.Infrastructure.Persistence;
using TechAntenna.Infrastructure.Storage;
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

// 接続文字列があれば PostgreSQL、無ければメモリ上のストアで動かす(DB なしのお試し起動用)
var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddSingleton<IArticleStore, InMemoryArticleStore>();
}
else
{
    builder.Services.AddDbContextFactory<TechAntennaDbContext>(o => o.UseNpgsql(connectionString));
    builder.Services.AddSingleton<IArticleStore, EfArticleStore>();
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
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

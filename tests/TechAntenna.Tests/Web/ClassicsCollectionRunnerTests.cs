using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Storage;
using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

public class ClassicsCollectionRunnerTests
{
    static readonly DateTimeOffset Now = new(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);

    /// <summary>決まった ISBN を薦める収集元。</summary>
    class StubSource(params string[] isbns) : IBookRecommendationSource
    {
        public string Name => "Qiita(推薦本)";

        public Task<IReadOnlyList<BookRecommendation>> FetchAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BookRecommendation>>(isbns
                .Select(isbn => new BookRecommendation(
                    isbn, [new RecommendedArticle("https://example.com/article", "読むべき技術書")]))
                .ToList());
    }

    /// <summary>渡された本を記録し、タイトルだけ埋める補完(openBD の代わり)。</summary>
    class RecordingEnricher : IBookEnricher
    {
        public List<Book> Received { get; } = [];

        public string Name => "テスト用の補完";

        public Task<IReadOnlyList<Book>> EnrichAsync(
            IReadOnlyList<Book> books, CancellationToken cancellationToken = default)
        {
            Received.AddRange(books);

            return Task.FromResult<IReadOnlyList<Book>>(books
                .Select(book => new Book
                {
                    Id = book.Id,
                    Title = book.Title is { Length: > 0 } ? book.Title : "補完したタイトル",
                    Isbn13 = book.Isbn13,
                    CoverUrl = book.CoverUrl,
                    SourceName = book.SourceName,
                    CollectedAt = book.CollectedAt,
                    RecommendedBy = book.RecommendedBy,
                })
                .ToList());
        }
    }

    static ClassicsCollectionRunner Runner(
        IBookRecommendationSource source, IBookEnricher enricher, InMemoryBookStore books) =>
        new([source], [enricher], books,
            new TagObserver(
                new InMemoryTagStore(), new InMemoryArticleStore(), new InMemoryEventStore(),
                books, new FakeTimeProvider(Now)),
            TopicCatalog.Empty,
            new FakeTimeProvider(Now),
            NullLogger<ClassicsCollectionRunner>.Instance);

    [Fact]
    public async Task 保存済みの書影を引き継いでから補完へ渡す()
    {
        // 拾えるのは ISBN だけなので毎回まっさらな本を組み立てることになる。そのまま渡すと
        // 書影の補完(Google Books は 1 冊 1 リクエスト・無料枠 1 日 1,000)が毎回全冊ぶん走る
        var books = new InMemoryBookStore();
        await books.AddRangeAsync([
            new Book
            {
                Title = "既にある本",
                Isbn13 = "9784111111111",
                CoverUrl = new Uri("https://books.google.com/cover.jpg"),
                SourceName = "Qiita(推薦本)",
                CollectedAt = Now.AddDays(-1),
            },
        ]);
        var enricher = new RecordingEnricher();

        await Runner(new StubSource("9784111111111", "9784222222222"), enricher, books)
            .RunOnceAsync();

        var carried = enricher.Received.Single(book => book.Isbn13 == "9784111111111");
        Assert.Equal("https://books.google.com/cover.jpg", carried.CoverUrl?.ToString());
        // まだ持っていない本は書影なしのまま渡す(補完に引かせる)
        var fresh = enricher.Received.Single(book => book.Isbn13 == "9784222222222");
        Assert.Null(fresh.CoverUrl);
    }
}

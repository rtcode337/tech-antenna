using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Storage;
using TechAntenna.Web;
using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

public class BookCollectionRunnerTests
{
    static readonly DateTimeOffset Now = new(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);

    /// <summary>トピックごとに決まった ISBN を返す引用の収集元。</summary>
    class StubCitationSource : IBookCitationSource
    {
        public List<string> Topics { get; } = [];

        public string Name => "Qiita(トピックの記事)";

        public Task<IReadOnlyList<BookCitation>> FetchAsync(
            string topic, CancellationToken cancellationToken = default)
        {
            Topics.Add(topic);

            return Task.FromResult<IReadOnlyList<BookCitation>>([
                new BookCitation("9784873115658", [
                    new SourceArticle($"https://qiita.com/items/{Topics.Count}", $"{topic} の記事"),
                ]),
            ]);
        }
    }

    /// <summary>タイトルだけ埋める補完(openBD の代わり)。</summary>
    class TitleEnricher : IBookEnricher
    {
        public string Name => "テスト用の補完";

        public Task<IReadOnlyList<Book>> EnrichAsync(
            IReadOnlyList<Book> books, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Book>>(books
                .Select(book => new Book
                {
                    Id = book.Id,
                    Title = book.Title is { Length: > 0 } ? book.Title : "リーダブルコード",
                    Isbn13 = book.Isbn13,
                    SourceName = book.SourceName,
                    CollectedAt = book.CollectedAt,
                    Tags = book.Tags,
                    RawTags = book.RawTags,
                    RecommendedBy = book.RecommendedBy,
                    CitedBy = book.CitedBy,
                })
                .ToList());
    }

    static async Task<InMemoryTopicStore> TopicsAsync(params string[] displays)
    {
        var store = new InMemoryTopicStore();
        await store.UpsertAsync(
            displays.Select(display => new Topic { Key = TagNormalizer.ToKey(display), Display = display })
                .ToList(),
            Now);
        foreach (var display in displays)
        {
            await store.SetSelectedAsync(TagNormalizer.ToKey(display), true);
        }

        return store;
    }

    static BookCollectionRunner Runner(
        IBookCitationSource citations, InMemoryBookStore books, ITopicStore topics) =>
        new([], [citations], [new TitleEnricher()], SourceTogglesTests.AllEnabled(), books, topics,
            new TagObserver(
                new InMemoryTagStore(), new InMemoryArticleStore(), new InMemoryEventStore(),
                books, new FakeTimeProvider(Now)),
            TopicCatalog.Empty,
            new FakeTimeProvider(Now),
            Options.Create(new BooksOptions { DelayBetweenKeywordsSeconds = 0 }),
            NullLogger<BookCollectionRunner>.Instance);

    [Fact]
    public async Task 選んだトピックごとに引用を拾い検索語をタグにする()
    {
        // タグが付かないと、その本は興味トピックの一覧のどのグループにも出てこない
        var books = new InMemoryBookStore();
        var citations = new StubCitationSource();

        var result = await Runner(citations, books, await TopicsAsync("機械学習", "生成AI")).RunOnceAsync();

        Assert.Equal(["機械学習", "生成AI"], citations.Topics);
        var stored = Assert.Single(await books.GetRecentAsync(10));
        Assert.Equal("リーダブルコード", stored.Title);
        // 同じ本が両方のトピックで引用されたので、タグも票も積み上がる
        Assert.Equal(["機械学習", "生成AI"], stored.RawTags);
        Assert.Equal(2, stored.CitationCount);
        Assert.Equal(2, BookPopularity.Endorsements(stored));
        Assert.Equal(0, result.FailedSources);
    }

    [Fact]
    public async Task 検索の収集元が無くても引用だけで動く()
    {
        // 引用はキーが要らない —— Google Books のキーを入れていない環境でもここは集まる
        var runner = Runner(new StubCitationSource(), new InMemoryBookStore(), await TopicsAsync("機械学習"));

        Assert.True(runner.IsConfigured);
        Assert.Equal(1, (await runner.RunOnceAsync()).Added);
    }

    [Fact]
    public async Task トピックを選んでいなければ理由を返して問い合わせない()
    {
        var citations = new StubCitationSource();

        var result = await Runner(citations, new InMemoryBookStore(), new InMemoryTopicStore()).RunOnceAsync();

        Assert.Empty(citations.Topics);
        Assert.Equal(0, result.Fetched);
        Assert.Contains("収集対象のトピックが選ばれていません", result.Note);
    }
}

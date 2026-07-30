using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Storage;

namespace TechAntenna.Tests.Core;

public class TopicServiceTests
{
    static readonly DateTimeOffset Now = new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);

    static Article NewArticle(string path, params string[] tags) => new()
    {
        Title = path,
        Url = new Uri($"https://example.com/{path}"),
        SourceName = "テスト",
        CollectedAt = Now,
        Tags = tags,
    };

    static TechEvent NewEvent(string path, params string[] tags) => new()
    {
        Title = path,
        Url = new Uri($"https://example.com/{path}"),
        SourceName = "テスト",
        StartsAt = Now.AddDays(7),
        CollectedAt = Now,
        Tags = tags,
    };

    static Book NewBook(string isbn, params string[] tags) => new()
    {
        Title = isbn,
        Isbn13 = isbn,
        SourceName = "テスト",
        CollectedAt = Now,
        Tags = tags,
    };

    static async Task<TopicService> BuildAsync(
        IEnumerable<Article>? articles = null,
        IEnumerable<TechEvent>? events = null,
        IEnumerable<Book>? books = null)
    {
        var articleStore = new InMemoryArticleStore();
        var eventStore = new InMemoryEventStore();
        var bookStore = new InMemoryBookStore();

        await articleStore.AddRangeAsync(articles ?? []);
        await eventStore.AddRangeAsync(events ?? []);
        await bookStore.AddRangeAsync(books ?? []);

        return new TopicService(articleStore, eventStore, bookStore);
    }

    [Fact]
    public async Task 記事イベント書籍がそろったタグを上位に出す()
    {
        // "blazor" は記事だけで件数が多く、"c#" は3種そろっているが件数は少ない
        var service = await BuildAsync(
            articles: [
                NewArticle("a1", "blazor"),
                NewArticle("a2", "blazor"),
                NewArticle("a3", "blazor"),
                NewArticle("a4", "c#"),
            ],
            events: [NewEvent("e1", "c#")],
            books: [NewBook("9784111111111", "c#")]);

        var topics = await service.GetTopicsAsync(10);

        Assert.Equal(["c#", "blazor"], topics.Select(t => t.Tag));
        Assert.Equal(3, topics[0].Coverage);
        Assert.Equal(1, topics[1].Coverage);
    }

    [Fact]
    public async Task そろっている種類数が同じなら総件数の多い順()
    {
        var service = await BuildAsync(
            articles: [
                NewArticle("a1", "many"),
                NewArticle("a2", "many"),
                NewArticle("a3", "few"),
            ]);

        var topics = await service.GetTopicsAsync(10);

        Assert.Equal(["many", "few"], topics.Select(t => t.Tag));
    }

    [Fact]
    public async Task 種類ごとの件数を数える()
    {
        var service = await BuildAsync(
            articles: [NewArticle("a1", "c#"), NewArticle("a2", "c#")],
            events: [NewEvent("e1", "c#")],
            books: []);

        var topic = Assert.Single(await service.GetTopicsAsync(10));

        Assert.Equal(2, topic.ArticleCount);
        Assert.Equal(1, topic.EventCount);
        Assert.Equal(0, topic.BookCount);
        Assert.Equal(3, topic.Total);
    }

    [Fact]
    public async Task タグを指定して記事イベント書籍をまとめて引ける()
    {
        var service = await BuildAsync(
            articles: [NewArticle("a1", "c#"), NewArticle("a2", "blazor")],
            events: [NewEvent("e1", "c#")],
            books: [NewBook("9784111111111", "c#")]);

        var detail = await service.GetTopicAsync("c#", 10);

        Assert.Equal("c#", detail.Tag);
        Assert.Equal(["a1"], detail.Articles.Select(a => a.Title));
        Assert.Equal(["e1"], detail.Events.Select(e => e.Title));
        Assert.Equal(["9784111111111"], detail.Books.Select(b => b.Title));
    }

    [Fact]
    public async Task 大文字で指定しても正規化して引ける()
    {
        var service = await BuildAsync(articles: [NewArticle("a1", "c#")]);

        // 保存時と同じ正規化を通すので "C#" でも引ける
        var detail = await service.GetTopicAsync("C#", 10);

        Assert.Equal("c#", detail.Tag);
        Assert.Single(detail.Articles);
    }

    [Fact]
    public async Task 何も無ければ空を返す()
    {
        var service = await BuildAsync();

        Assert.Empty(await service.GetTopicsAsync(10));
    }
}

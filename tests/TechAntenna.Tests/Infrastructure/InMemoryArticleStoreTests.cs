using TechAntenna.Core.Models;
using TechAntenna.Infrastructure.Storage;

namespace TechAntenna.Tests.Infrastructure;

public class InMemoryArticleStoreTests
{
    static Article NewArticle(string path, DateTimeOffset? publishedAt = null) => new()
    {
        Title = path,
        Url = new Uri($"https://example.com/{path}"),
        SourceName = "テスト",
        PublishedAt = publishedAt,
        CollectedAt = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public async Task 同じURLの記事は二重に追加されない()
    {
        var store = new InMemoryArticleStore();

        var first = await store.AddRangeAsync([NewArticle("a"), NewArticle("b")]);
        var second = await store.AddRangeAsync([NewArticle("a"), NewArticle("c")]);

        Assert.Equal(2, first);
        Assert.Equal(1, second);
        Assert.Equal(3, (await store.GetRecentAsync(10)).Count);
    }

    [Fact]
    public async Task 公開日時の新しい順に返す()
    {
        var store = new InMemoryArticleStore();
        var baseTime = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        await store.AddRangeAsync([
            NewArticle("old", baseTime),
            NewArticle("new", baseTime.AddDays(2)),
            NewArticle("mid", baseTime.AddDays(1)),
        ]);

        var recent = await store.GetRecentAsync(2);

        Assert.Equal(["new", "mid"], recent.Select(a => a.Title));
    }

    [Fact]
    public async Task 公開日時が無い記事は収集日時で並べる()
    {
        var store = new InMemoryArticleStore();
        var published = new DateTimeOffset(2026, 7, 29, 0, 0, 0, TimeSpan.Zero);
        // NewArticle の CollectedAt は 2026-07-30 固定なので、日付なし記事が先頭に来るはず
        await store.AddRangeAsync([NewArticle("dated", published), NewArticle("undated")]);

        var recent = await store.GetRecentAsync(10);

        Assert.Equal(["undated", "dated"], recent.Select(a => a.Title));
    }
}

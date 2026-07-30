using TechAntenna.Core.Models;
using TechAntenna.Infrastructure.Storage;

namespace TechAntenna.Tests.Infrastructure;

public class InMemoryBookStoreTests
{
    static Book NewBook(string title, string? isbn13 = null, DateTimeOffset? collectedAt = null) => new()
    {
        Title = title,
        Isbn13 = isbn13,
        SourceName = "テスト",
        CollectedAt = collectedAt ?? new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public async Task 同じISBNの書籍は二重に追加されない()
    {
        var store = new InMemoryBookStore();

        var first = await store.AddRangeAsync([
            NewBook("A", "9784111111111"),
            NewBook("B", "9784222222222"),
        ]);
        // タイトルが違っても ISBN が同じなら同一の書籍として扱う
        var second = await store.AddRangeAsync([
            NewBook("A(別の版元表記)", "9784111111111"),
            NewBook("C", "9784333333333"),
        ]);

        Assert.Equal(2, first);
        Assert.Equal(1, second);
        Assert.Equal(3, (await store.GetRecentAsync(10)).Count);
    }

    [Fact]
    public async Task 収集日時の新しい順に返す()
    {
        var store = new InMemoryBookStore();
        var baseTime = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        await store.AddRangeAsync([
            NewBook("old", "9784111111111", baseTime),
            NewBook("new", "9784222222222", baseTime.AddDays(2)),
            NewBook("mid", "9784333333333", baseTime.AddDays(1)),
        ]);

        var recent = await store.GetRecentAsync(2);

        Assert.Equal(["new", "mid"], recent.Select(b => b.Title));
    }
}

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

    static Book Tagged(string title, string isbn13, string tag, string rawTag) => new()
    {
        Title = title,
        Isbn13 = isbn13,
        SourceName = "テスト",
        CollectedAt = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero),
        Tags = [tag],
        RawTags = [rawTag],
    };

    static readonly DateTimeOffset ReadOn = new(2026, 8, 19, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task 読んだ印は押すたびに裏返る()
    {
        var store = new InMemoryBookStore();
        var book = NewBook("読む本", "9784111111111");
        await store.AddRangeAsync([book]);

        Assert.True(await store.ToggleReadAsync(book.Id, ReadOn));
        Assert.Equal(ReadOn, (await store.GetRecentAsync(10)).Single().ReadAt);

        Assert.False(await store.ToggleReadAsync(book.Id, ReadOn));
        Assert.Null((await store.GetRecentAsync(10)).Single().ReadAt);
    }

    [Fact]
    public async Task 知らない本の印は裏返さずnullを返す()
    {
        // 画面は「見つからなかった」を黙って読み直すだけにするので、例外にはしない
        var store = new InMemoryBookStore();

        Assert.Null(await store.ToggleReadAsync(Guid.NewGuid(), ReadOn));
    }

    [Fact]
    public async Task 再収集しても読んだ印は消えない()
    {
        // 読んだかどうかは外から取れる情報ではない。収集元の本は ReadAt が常に null なので、
        // 合流でそれを写すと再収集のたびに印が消える(BookMerge が触らないことの確認)
        var store = new InMemoryBookStore();
        var book = NewBook("読む本", "9784111111111");
        await store.AddRangeAsync([book]);
        await store.ToggleReadAsync(book.Id, ReadOn);

        await store.AddRangeAsync([Tagged("読む本", "9784111111111", "ai", "AI")]);

        var stored = (await store.GetRecentAsync(10)).Single();
        Assert.Equal(ReadOn, stored.ReadAt);
        // 合流そのものは効いている(タグは足されている)
        Assert.Equal(["ai"], stored.Tags);
    }

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
    public async Task 同じ本が別のトピックで見つかったらタグを足す()
    {
        // 足さないと、最初に見つかったトピックの一覧にしか出てこない
        var store = new InMemoryBookStore();
        await store.AddRangeAsync([Tagged("A", "9784111111111", "ai", "AI")]);

        var added = await store.AddRangeAsync([Tagged("A", "9784111111111", "llm", "LLM")]);

        var book = Assert.Single(await store.GetRecentAsync(10));
        Assert.Equal(0, added);
        Assert.Equal(["ai", "llm"], book.Tags);
        // 生タグも足す —— 片方しか無いと、再正規化でもう片方のタグが消える
        Assert.Equal(["AI", "LLM"], book.RawTags);
        Assert.Single(await store.GetByTagAsync("llm", 10));
    }

    [Fact]
    public async Task 同じ保存の中に同じ本が複数あってもタグをまとめる()
    {
        var store = new InMemoryBookStore();

        var added = await store.AddRangeAsync([
            Tagged("A", "9784111111111", "ai", "AI"),
            Tagged("A", "9784111111111", "llm", "LLM"),
        ]);

        var book = Assert.Single(await store.GetRecentAsync(10));
        Assert.Equal(1, added);
        Assert.Equal(["ai", "llm"], book.Tags);
    }

    [Fact]
    public async Task 既にある本のレビューは新しい値で更新する()
    {
        // 書誌情報と違ってレビューは増えていくので、次に見つけたときの値に差し替える
        var store = new InMemoryBookStore();
        var first = Tagged("A", "9784111111111", "ai", "AI");
        first.ReviewCount = 10;
        first.ReviewAverage = 4.0;
        await store.AddRangeAsync([first]);

        var again = Tagged("A", "9784111111111", "ai", "AI");
        again.ReviewCount = 25;
        again.ReviewAverage = 4.2;
        await store.AddRangeAsync([again]);

        var book = Assert.Single(await store.GetRecentAsync(10));
        Assert.Equal(25, book.ReviewCount);
        Assert.Equal(4.2, book.ReviewAverage);
    }

    [Fact]
    public async Task レビューが取れなかった回に既存の値を消さない()
    {
        // 取得元が一時的に落ちただけで指標が消えると、並び順が回ごとに入れ替わる
        var store = new InMemoryBookStore();
        var first = Tagged("A", "9784111111111", "ai", "AI");
        first.ReviewCount = 10;
        await store.AddRangeAsync([first]);

        await store.AddRangeAsync([Tagged("A", "9784111111111", "ai", "AI")]);

        Assert.Equal(10, Assert.Single(await store.GetRecentAsync(10)).ReviewCount);
    }

    [Fact]
    public async Task 既にある本に書影が無ければ後から埋める()
    {
        // 書影の補完を足す前に保存した本は CoverUrl が null のまま残っている。
        // ここで埋めないと、収集のたびに取り直しては捨てることになり表紙が出ない
        var store = new InMemoryBookStore();
        await store.AddRangeAsync([Tagged("A", "9784111111111", "ai", "AI")]);

        var again = Tagged("A", "9784111111111", "ai", "AI");
        again.CoverUrl = new Uri("https://books.google.com/cover.jpg");
        await store.AddRangeAsync([again]);

        var book = Assert.Single(await store.GetRecentAsync(10));
        Assert.Equal("https://books.google.com/cover.jpg", book.CoverUrl?.ToString());
    }

    [Fact]
    public async Task 既にある書影は上書きしない()
    {
        // 書誌情報と同じ扱い。取得元が変わるたびに表紙が入れ替わらないようにする
        var store = new InMemoryBookStore();
        var first = Tagged("A", "9784111111111", "ai", "AI");
        first.CoverUrl = new Uri("https://thumbnail.image.rakuten.co.jp/medium.jpg");
        await store.AddRangeAsync([first]);

        var again = Tagged("A", "9784111111111", "ai", "AI");
        again.CoverUrl = new Uri("https://books.google.com/cover.jpg");
        await store.AddRangeAsync([again]);

        var book = Assert.Single(await store.GetRecentAsync(10));
        Assert.Equal("https://thumbnail.image.rakuten.co.jp/medium.jpg", book.CoverUrl?.ToString());
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

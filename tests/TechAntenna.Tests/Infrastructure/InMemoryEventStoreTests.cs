using TechAntenna.Core;
using TechAntenna.Core.Models;
using TechAntenna.Infrastructure.Storage;

namespace TechAntenna.Tests.Infrastructure;

public class InMemoryEventStoreTests
{
    static TechEvent NewEvent(
        string path, int day = 10, string? organizer = null, int? participants = null) => new()
    {
        Title = path,
        Url = new Uri($"https://example.com/{path}"),
        SourceName = "テスト",
        StartsAt = new DateTimeOffset(2026, 8, day, 19, 0, 0, JapanTime.Offset),
        CollectedAt = new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero),
        Organizer = organizer,
        ParticipantCount = participants,
    };

    static DateTimeOffset Jst(int month, int day) =>
        new(2026, month, day, 0, 0, 0, JapanTime.Offset);

    [Fact]
    public async Task 同じURLのイベントは二重に追加されない()
    {
        var store = new InMemoryEventStore();

        var first = await store.AddRangeAsync([NewEvent("a"), NewEvent("b")]);
        var second = await store.AddRangeAsync([NewEvent("a"), NewEvent("c")]);

        Assert.Equal(2, first);
        Assert.Equal(1, second);
    }

    [Fact]
    public async Task 既存のイベントは参加者数と主催者を取り込む()
    {
        var store = new InMemoryEventStore();
        await store.AddRangeAsync([NewEvent("a", participants: 10)]);

        // 開催が近づいて参加者が増えた回
        await store.AddRangeAsync([NewEvent("a", organizer: "Microsoft", participants: 80)]);

        var stored = Assert.Single(await store.GetUpcomingAsync(Jst(8, 1), 10));
        Assert.Equal(80, stored.ParticipantCount);
        Assert.Equal("Microsoft", stored.Organizer);
    }

    [Fact]
    public async Task 取れなかった回はnullで上書きしない()
    {
        var store = new InMemoryEventStore();
        await store.AddRangeAsync([NewEvent("a", organizer: "Microsoft", participants: 80)]);

        // 同じ URL を、数を持たない収集元(TECH PLAY)経由で見かけた回
        await store.AddRangeAsync([NewEvent("a")]);

        var stored = Assert.Single(await store.GetUpcomingAsync(Jst(8, 1), 10));
        Assert.Equal(80, stored.ParticipantCount);
        Assert.Equal("Microsoft", stored.Organizer);
    }

    [Fact]
    public async Task 期間で切り出せる()
    {
        var store = new InMemoryEventStore();
        await store.AddRangeAsync([NewEvent("7月末", day: 1), NewEvent("月中", day: 15), NewEvent("翌月", day: 31)]);

        var inRange = await store.GetInRangeAsync(Jst(8, 10), Jst(8, 20), 100);

        // 終端は含めない(月の境界で二重に出さないため)
        Assert.Equal(["月中"], inRange.Select(e => e.Title));
    }

    [Fact]
    public async Task 主催者ごとの件数を多い順に返す()
    {
        var store = new InMemoryEventStore();
        await store.AddRangeAsync([
            NewEvent("a", organizer: "Microsoft"),
            NewEvent("b", organizer: "Microsoft"),
            NewEvent("c", organizer: "地域コミュニティ"),
            NewEvent("d"),
        ]);

        var counts = await store.GetOrganizerCountsAsync();

        // 主催者が取れていないイベントは数えない
        Assert.Equal([("Microsoft", 2), ("地域コミュニティ", 1)],
            counts.Select(c => (c.Organizer, c.Count)));
    }
}

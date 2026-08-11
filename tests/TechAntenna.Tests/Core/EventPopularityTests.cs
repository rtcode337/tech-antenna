using TechAntenna.Core;
using TechAntenna.Core.Models;

namespace TechAntenna.Tests.Core;

public class EventPopularityTests
{
    static readonly OfficialOrganizers Official = OfficialOrganizers.Parse("Microsoft");

    static TechEvent Event(string title, string? organizer = null, int? participants = null, int day = 1) =>
        new()
        {
            Title = title,
            Url = new Uri($"https://example.com/{title}"),
            SourceName = "test",
            StartsAt = new DateTimeOffset(2026, 9, day, 19, 0, 0, TimeSpan.FromHours(9)),
            CollectedAt = DateTimeOffset.UnixEpoch,
            Organizer = organizer,
            ParticipantCount = participants,
        };

    [Fact]
    public void 公式は参加者数の下駄を履く()
    {
        var official = EventPopularity.Score(Event("a", "Microsoft"), Official);
        var community = EventPopularity.Score(Event("b", "地域コミュニティ"), Official);

        Assert.True(official > community);
        // 下駄は参加者およそ 9 人ぶん(log10(1+9) = 1)
        Assert.Equal(EventPopularity.Score(Event("c", null, 9), Official), official, 6);
    }

    [Fact]
    public void 参加者数が多いほど高い()
    {
        var large = EventPopularity.Score(Event("a", null, 300), Official);
        var small = EventPopularity.Score(Event("b", null, 20), Official);

        Assert.True(large > small);
    }

    [Fact]
    public void 参加者数を取れない収集元は0人として扱う()
    {
        // null を後ろに回す作りにすると、数を持たない TECH PLAY のイベントが
        // 公式判定ごと沈む。そのぶん「後ろに来る」ことは画面に書いてある
        Assert.Equal(0, EventPopularity.Score(Event("a"), Official));
        Assert.Equal(
            EventPopularity.Score(Event("b", null, 0), Official),
            EventPopularity.Score(Event("c"), Official));
    }

    [Fact]
    public void 注目度が同じなら開催日の早い順()
    {
        var later = Event("later", participants: 50, day: 20);
        var sooner = Event("sooner", participants: 50, day: 3);

        var ordered = new[] { later, sooner }.ByPopularity(Official).ToList();

        Assert.Equal(["sooner", "later"], ordered.Select(e => e.Title));
    }

    [Fact]
    public void 注目度の高い順に並べる()
    {
        var events = new[]
        {
            Event("小さな勉強会", "地域コミュニティ", 5, day: 2),
            Event("大規模カンファレンス", "コミュニティ", 400, day: 25),
            Event("提供元のセミナー", "Microsoft", 20, day: 10),
        };

        var ordered = events.ByPopularity(Official).Select(e => e.Title).ToList();

        Assert.Equal(["大規模カンファレンス", "提供元のセミナー", "小さな勉強会"], ordered);
    }
}

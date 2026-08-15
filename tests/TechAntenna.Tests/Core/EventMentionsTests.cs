using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;

namespace TechAntenna.Tests.Core;

public class EventMentionsTests
{
    static TechEvent Event(string title) => new()
    {
        Title = title,
        Url = new Uri("https://example.com/event/1"),
        SourceName = "connpass",
        StartsAt = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.FromHours(9)),
        CollectedAt = DateTimeOffset.UnixEpoch,
    };

    static Article Article(string title) => new()
    {
        Title = title,
        Url = new Uri($"https://example.com/{Guid.NewGuid()}"),
        SourceName = "Zenn",
        CollectedAt = DateTimeOffset.UnixEpoch,
    };

    [Theory]
    [InlineData("RubyKaigi 2026", "RubyKaigi")]
    [InlineData("【東京開催】DroidKaigi 2026", "DroidKaigi")]
    [InlineData("第12回 JJUG CCC", "JJUG CCC")]
    [InlineData("Kaigi on Rails 2026 | 参加者募集", "Kaigi on Rails")]
    [InlineData("PHPerKaigi 2026 #phperkaigi", "PHPerKaigi")]
    public void 飾りと回数と年を落として名前だけを照合語にする(string title, string expected)
    {
        Assert.Equal(expected, EventMentions.KeyFor(Event(title)));
    }

    [Theory]
    [InlineData("もくもく会")]           // どのイベントにも付く一般名
    [InlineData("勉強会")]
    [InlineData("LT会")]
    [InlineData("第3回")]                // 落とすと何も残らない
    [InlineData("AI")]                   // 短すぎて必ず誤爆する
    public void 一般名や短すぎる語は照合語にしない(string title)
    {
        // **測れないなら測らない。** 誤った言及数は、注目度の並びを静かに壊す
        Assert.Null(EventMentions.KeyFor(Event(title)));
    }

    [Fact]
    public void 技術名そのものは照合語にしない()
    {
        // 「Kubernetes」という名前のイベントを測ると、Kubernetes の記事が全部当たる
        var catalog = new TopicCatalog([new TopicCatalogEntry("Kubernetes", [], null)]);

        Assert.Null(EventMentions.KeyFor(Event("Kubernetes"), catalog));
        // 語彙を渡さなければ長さの条件しか見ない(語彙は任意)
        Assert.Equal("Kubernetes", EventMentions.KeyFor(Event("Kubernetes")));
    }

    [Fact]
    public void タイトルに照合語を含む記事を数える()
    {
        var articles = new[]
        {
            Article("RubyKaigi 2026 に参加しました"),
            Article("RubyKaigi の歩き方"),
            Article("Go の話"),
        };

        Assert.Equal(2, EventMentions.Count("RubyKaigi", articles));
    }

    [Fact]
    public void 訳題も見る()
    {
        var article = Article("Notes from RubyKaigi");
        article.TitleJa = "RubyKaigi の記録";

        Assert.Equal(1, EventMentions.Count("RubyKaigi", [article]));
    }

    [Fact]
    public void 語の境界を守る()
    {
        // KeywordMatcher と同じ規則。地続きの英数字には当てない
        Assert.Equal(0, EventMentions.Count("Rails", [Article("Guardrails の話")]));
    }
}

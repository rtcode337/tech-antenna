using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Storage;

namespace TechAntenna.Tests.Core;

public class InMemoryTopicStoreTests
{
    static readonly DateTimeOffset CollectedAt = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    static TopicUpdate Update(string tag, double score, string? display = null) =>
        new(tag, display ?? tag, null, score, 1, 0, 0, 0);

    [Fact]
    public async Task 話題度の高い順に返す()
    {
        var store = new InMemoryTopicStore();

        await store.UpsertAsync([Update("記事だけ", 10), Update("3種", 30)], CollectedAt);

        var topics = await store.GetTopicsAsync(10);

        Assert.Equal(["3種", "記事だけ"], topics.Select(topic => topic.Tag));
        Assert.Equal(CollectedAt, topics[0].CollectedAt);
    }

    [Fact]
    public async Task 今回現れなかったトピックは消さずに話題度だけ0にする()
    {
        // 消すと選択(IsSelected)ごと失われ、収集キーワードが空になって収集が止まる
        var store = new InMemoryTopicStore();
        await store.UpsertAsync([Update("前回だけ", 30), Update("両方", 10)], CollectedAt);
        await store.UpdateSelectionAsync(["前回だけ"]);

        await store.UpsertAsync([Update("両方", 20), Update("今回だけ", 5)], CollectedAt.AddHours(1));

        var topics = await store.GetTopicsAsync(10);
        Assert.Equal(["両方", "今回だけ", "前回だけ"], topics.Select(topic => topic.Tag));
        Assert.Equal(0, topics.Single(topic => topic.Tag == "前回だけ").TrendScore);
        Assert.Equal(["前回だけ"], (await store.GetSelectedAsync()).Select(topic => topic.Tag));
    }

    [Fact]
    public async Task 選択したトピックはキーと正式表記の両方を返す()
    {
        // 検索語には正式表記が要り、記事のタグとの突き合わせにはキーが要る
        var store = new InMemoryTopicStore();
        await store.UpsertAsync([Update("生成ai", 10, display: "生成AI")], CollectedAt);

        await store.UpdateSelectionAsync(["生成AI"]);

        var selected = Assert.Single(await store.GetSelectedAsync());
        Assert.Equal("生成ai", selected.Tag);
        Assert.Equal("生成AI", selected.Display);
    }
}

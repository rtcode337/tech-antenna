using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Storage;

namespace TechAntenna.Tests.Core;

public class InMemoryTagStoreTests
{
    static readonly DateTimeOffset Now = new(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task 観測は状態を触らない()
    {
        // 収集のたびに仕分けが巻き戻ると、同じ語を何度も LLM に聞くことになる
        var store = new InMemoryTagStore();
        await store.ObserveAsync([new TagObservation("rag", ArticleCount: 1)], Now);
        await store.DecideAsync([new TagDecision("rag", TagStatus.Promoted, "rag")], Now);

        await store.ObserveAsync([new TagObservation("rag", ArticleCount: 5)], Now.AddDays(1));

        var tag = Assert.Single(await store.GetAllAsync());
        Assert.Equal(TagStatus.Promoted, tag.Status);
        Assert.Equal("rag", tag.TopicKey);
        Assert.Equal(5, tag.ArticleCount);
        // 最初に見かけた時刻は保つ(いつ現れた語かが分かるように)
        Assert.Equal(Now, tag.FirstSeenAt);
        Assert.Equal(Now.AddDays(1), tag.LastSeenAt);
    }

    [Fact]
    public async Task 渡されなかったタグは件数を0に戻す()
    {
        // 別名がまとまってタグが消えたときに、古い件数が残らないようにする
        var store = new InMemoryTagStore();
        await store.ObserveAsync(
            [new TagObservation("前回だけ", ArticleCount: 3), new TagObservation("両方", ArticleCount: 1)],
            Now);

        await store.ObserveAsync(
            [new TagObservation("両方", ArticleCount: 2)], Now.AddHours(1), resetMissing: true);

        var tags = (await store.GetAllAsync()).ToDictionary(tag => tag.Key);
        Assert.Equal(0, tags["前回だけ"].ArticleCount);
        Assert.Equal(2, tags["両方"].ArticleCount);
    }

    [Fact]
    public async Task 次に聞くのは未仕分けと期限切れの保留だけ()
    {
        var store = new InMemoryTagStore();
        await store.ObserveAsync(
        [
            new TagObservation("未仕分け", ArticleCount: 3),
            new TagObservation("件数不足", ArticleCount: 1),
            new TagObservation("トレンド由来", TrendScore: 0.5),
            new TagObservation("済み", ArticleCount: 9),
            new TagObservation("保留中", ArticleCount: 4),
            new TagObservation("保留の期限切れ", ArticleCount: 5),
        ], Now);
        await store.DecideAsync(
        [
            new TagDecision("済み", TagStatus.Promoted, "済み"),
            new TagDecision("保留中", TagStatus.Unresolved, RetryAfter: Now.AddDays(7)),
            new TagDecision("保留の期限切れ", TagStatus.Unresolved, RetryAfter: Now.AddDays(-1)),
        ], Now);

        var pending = await store.GetPendingAsync(Now, minCount: 3);

        // **件数の下限は集めたデータの分だけに掛ける** ——
        // トレンド由来の語は手元の件数が 0 なのが普通で、そこで落とすと新語が入らない
        Assert.Equal(["保留の期限切れ", "未仕分け", "トレンド由来"], pending.Select(tag => tag.Key));
    }

    [Fact]
    public async Task 仕分けは既にあるタグにだけ書く()
    {
        // 行の無いキーに書くと、観測していない語が状態だけ持って現れてしまう
        var store = new InMemoryTagStore();

        await store.DecideAsync([new TagDecision("いない語", TagStatus.NotTopic)], Now);

        Assert.Empty(await store.GetAllAsync());
    }
}

using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Storage;

namespace TechAntenna.Tests.Core;

public class InMemoryTopicStoreTests
{
    static readonly DateTimeOffset UpdatedAt = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    static Topic NewTopic(string key, double score, string? display = null) => new()
    {
        Key = key,
        Display = display ?? key,
        TrendScore = score,
        SubtreeTrendScore = score,
    };

    [Fact]
    public async Task 話題度の高い順に返す()
    {
        var store = new InMemoryTopicStore();

        await store.UpsertAsync([NewTopic("記事だけ", 10), NewTopic("3種", 30)], UpdatedAt);

        var topics = await store.GetAllAsync();

        Assert.Equal(["3種", "記事だけ"], topics.Select(topic => topic.Key));
        Assert.Equal(UpdatedAt, topics[0].UpdatedAt);
    }

    [Fact]
    public async Task 今回現れなかったトピックは消さずに話題度だけ0にする()
    {
        // 消すと選択(IsSelected)ごと失われ、収集キーワードが空になって収集が止まる
        var store = new InMemoryTopicStore();
        await store.UpsertAsync([NewTopic("前回だけ", 30), NewTopic("両方", 10)], UpdatedAt);
        await store.UpdateSelectionAsync(["前回だけ"]);

        await store.UpsertAsync([NewTopic("両方", 20), NewTopic("今回だけ", 5)], UpdatedAt.AddHours(1));

        var topics = await store.GetAllAsync();
        // 選択済み(前回だけ)は話題度 0 でも先頭
        Assert.Equal(["前回だけ", "両方", "今回だけ"], topics.Select(topic => topic.Key));
        Assert.Equal(0, topics.Single(topic => topic.Key == "前回だけ").TrendScore);
        Assert.Equal(["前回だけ"], (await store.GetSelectedAsync()).Select(topic => topic.Key));
    }

    [Fact]
    public async Task 選択は正規化して突き合わせる()
    {
        // 画面から来る値は正式表記のこともある(`生成AI` → `生成ai`)
        var store = new InMemoryTopicStore();
        await store.UpsertAsync([NewTopic("生成ai", 10, "生成AI")], UpdatedAt);

        await store.UpdateSelectionAsync(["生成AI"]);

        var selected = Assert.Single(await store.GetSelectedAsync());
        Assert.Equal("生成ai", selected.Key);
        Assert.Equal("生成AI", selected.Display);
    }

    [Fact]
    public async Task 選択済みは消さない()
    {
        // 消すと収集キーワードごと失われる
        var store = new InMemoryTopicStore();
        await store.UpsertAsync([NewTopic("残す", 0), NewTopic("消す", 0)], UpdatedAt);
        await store.UpdateSelectionAsync(["残す"]);

        var removed = await store.RemoveAsync(["残す", "消す"]);

        Assert.Equal(1, removed);
        Assert.Equal(["残す"], (await store.GetAllAsync()).Select(topic => topic.Key));
    }

    [Fact]
    public async Task 一件だけ切り替えても他の選択は触らない()
    {
        // 画面のチェックはその場で 1 件ずつ保存する(POST /api/topics/select)。
        // ここで他の行まで触ると、閉じた枝や検索で絞られている行の選択が巻き添えで外れる
        var store = new InMemoryTopicStore();
        await store.UpsertAsync([NewTopic("生成ai", 10, "生成AI"), NewTopic("rag", 5)], UpdatedAt);
        await store.UpdateSelectionAsync(["生成ai"]);

        Assert.True(await store.SetSelectedAsync("rag", true));

        Assert.Equal(["rag", "生成ai"], (await store.GetSelectedAsync()).Select(topic => topic.Key));

        Assert.True(await store.SetSelectedAsync("生成ai", false));

        Assert.Equal(["rag"], (await store.GetSelectedAsync()).Select(topic => topic.Key));
    }

    [Fact]
    public async Task 一件の切り替えも正規化して突き合わせ_無い語は切り替えない()
    {
        // 正式表記(生成AI)で来ても当てる。語彙に無い語は false を返して画面が元に戻せるようにする
        var store = new InMemoryTopicStore();
        await store.UpsertAsync([NewTopic("生成ai", 10, "生成AI")], UpdatedAt);

        Assert.True(await store.SetSelectedAsync("生成AI", true));
        Assert.False(await store.SetSelectedAsync("知らない語", true));

        Assert.Equal(["生成ai"], (await store.GetSelectedAsync()).Select(topic => topic.Key));
    }

    [Fact]
    public async Task 更新でも選択は触らない()
    {
        // 選択を変えるのは画面の操作だけ。整備が上書きすると収集対象が勝手に変わる
        var store = new InMemoryTopicStore();
        await store.UpsertAsync([NewTopic("生成ai", 10)], UpdatedAt);
        await store.UpdateSelectionAsync(["生成ai"]);

        await store.UpsertAsync([NewTopic("生成ai", 20)], UpdatedAt.AddHours(1));

        Assert.True((await store.GetAsync("生成ai"))!.IsSelected);
    }
}

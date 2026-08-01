using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Infrastructure.Storage;
using TechAntenna.Web;
using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

public class SummaryRunnerTests
{
    /// <summary>要約の呼び出しを記録するだけの ISummarizer。</summary>
    class StubSummarizer(Func<IReadOnlyList<Article>, IReadOnlyList<SummaryResult>> respond)
        : ISummarizer
    {
        public int CallCount { get; private set; }

        /// <summary>要約に入ったことを外から待てるようにする(同時実行の検証で使う)。</summary>
        public TaskCompletionSource Entered { get; } = new();

        /// <summary>ここを完了させるまで要約を返さない。</summary>
        public TaskCompletionSource Release { get; } = new();

        public string Name => "スタブ";

        public async Task<IReadOnlyList<SummaryResult>> SummarizeAsync(
            IReadOnlyList<Article> articles, CancellationToken cancellationToken = default)
        {
            CallCount++;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            return respond(articles);
        }
    }

    static Article Article(string title, string? snippet = "本文") => new()
    {
        Title = title,
        Url = new Uri($"https://example.com/{Uri.EscapeDataString(title)}"),
        SourceName = "Zenn",
        ContentSnippet = snippet,
        CollectedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
    };

    static SummaryRunner Runner(IEnumerable<ISummarizer> summarizers, IArticleStore store) =>
        new(summarizers,
            store,
            Options.Create(new AnthropicOptions { BatchSize = 20 }),
            NullLogger<SummaryRunner>.Instance);

    static async Task<InMemoryArticleStore> StoreWith(params Article[] articles)
    {
        var store = new InMemoryArticleStore();
        await store.AddRangeAsync(articles);
        return store;
    }

    [Fact]
    public async Task 要約が未設定なら実行しない()
    {
        var runner = Runner([], await StoreWith(Article("一つ目")));

        Assert.False(runner.IsConfigured);
        Assert.Equal(SummaryRunResult.Nothing, await runner.RunOnceAsync());
    }

    [Fact]
    public async Task 未要約の記事をまとめて要約して保存する()
    {
        var store = await StoreWith(Article("一つ目"), Article("二つ目"));
        var summarizer = new StubSummarizer(articles =>
            articles.Select(a => new SummaryResult(a.Id, $"{a.Title}の要約")).ToList());
        summarizer.Release.SetResult();

        var result = await Runner([summarizer], store).RunOnceAsync();

        Assert.Equal(2, result.Requested);
        Assert.Equal(2, result.Summarized);
        // 1回の呼び出しにまとめて渡している
        Assert.Equal(1, summarizer.CallCount);
        Assert.Empty(await store.GetUnsummarizedAsync(10));
    }

    [Fact]
    public async Task 要約できなかった記事は空の要約で確定させる()
    {
        var store = await StoreWith(Article("材料なし", snippet: null));
        var summarizer = new StubSummarizer(
            articles => articles.Select(a => new SummaryResult(a.Id, null)).ToList());
        summarizer.Release.SetResult();

        var result = await Runner([summarizer], store).RunOnceAsync();

        Assert.Equal(0, result.Summarized);
        // 空でも確定させるので、次回また挑むことはない
        Assert.Empty(await store.GetUnsummarizedAsync(10));
    }

    [Fact]
    public async Task 結果に含まれなかった記事は次回に持ち越す()
    {
        var store = await StoreWith(Article("一つ目"), Article("二つ目"));
        // 1件しか返さない実装。残り1件は未処理のまま
        var summarizer = new StubSummarizer(
            articles => [new SummaryResult(articles[0].Id, "要約")]);
        summarizer.Release.SetResult();

        var result = await Runner([summarizer], store).RunOnceAsync();

        Assert.Equal(1, result.Skipped);
        Assert.Single(await store.GetUnsummarizedAsync(10));
    }

    [Fact]
    public async Task 実行中にもう一度呼んでも二重に走らせない()
    {
        // 定期実行と画面のボタンが重なると、同じ記事を二度要約して LLM の枠を無駄にする
        var store = await StoreWith(Article("一つ目"));
        var summarizer = new StubSummarizer(
            articles => articles.Select(a => new SummaryResult(a.Id, "要約")).ToList());
        var runner = Runner([summarizer], store);

        var first = runner.RunOnceAsync();
        await summarizer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 1本目が要約の途中にいる状態で2本目を呼ぶ
        Assert.True(runner.IsRunning);
        Assert.Equal(SummaryRunResult.Nothing, await runner.RunOnceAsync());

        summarizer.Release.SetResult();
        Assert.Equal(1, (await first).Summarized);
        Assert.Equal(1, summarizer.CallCount);
        Assert.False(runner.IsRunning);
    }
}

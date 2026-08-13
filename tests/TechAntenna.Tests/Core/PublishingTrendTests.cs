using TechAntenna.Core.Models;

namespace TechAntenna.Tests.Core;

public class PublishingTrendTests
{
    static NewRelease Release(string title, DateOnly publishedOn, params string[] tags) => new()
    {
        Title = title,
        Url = new Uri($"https://ndlsearch.ndl.go.jp/books/{Uri.EscapeDataString(title)}"),
        PublishedOn = publishedOn,
        SourceName = "NDL サーチ",
        CollectedAt = new DateTimeOffset(2026, 8, 13, 0, 0, 0, TimeSpan.Zero),
        Tags = tags,
    };

    [Fact]
    public void テーマごとに冊数を数えて多い順に返す()
    {
        var themes = PublishingTrend.Themes([
            Release("生成AIの本 1", new DateOnly(2026, 7, 1), "生成ai"),
            Release("生成AIの本 2", new DateOnly(2026, 6, 1), "生成ai", "llm"),
            Release("生成AIの本 3", new DateOnly(2026, 5, 1), "生成ai"),
            Release("LLM の本", new DateOnly(2026, 4, 1), "llm"),
            Release("Rust の本", new DateOnly(2026, 4, 1), "rust"),
        ]);

        Assert.Equal(["生成ai", "llm"], themes.Select(theme => theme.Tag));
        Assert.Equal(3, themes[0].Count);
        // 1 冊だけのテーマは出さない(偶然が混じる)
        Assert.DoesNotContain(themes, theme => theme.Tag == "rust");
    }

    [Fact]
    public void 代表タイトルは新しい順に数件だけ返す()
    {
        var themes = PublishingTrend.Themes([
            Release("古い本", new DateOnly(2026, 3, 1), "生成ai"),
            Release("新しい本", new DateOnly(2026, 7, 1), "生成ai"),
            Release("中くらいの本", new DateOnly(2026, 5, 1), "生成ai"),
        ]);

        var theme = Assert.Single(themes);
        Assert.Equal(3, theme.Count);
        Assert.Equal(["新しい本", "中くらいの本"], theme.Examples.Select(release => release.Title));
    }

    [Fact]
    public void 同じ冊数なら新しい本を含むほうが先()
    {
        // 同じ 2 冊なら、先月出たテーマのほうが「いま」に近い
        var themes = PublishingTrend.Themes([
            Release("古い A", new DateOnly(2026, 2, 1), "古いテーマ"),
            Release("古い B", new DateOnly(2026, 2, 1), "古いテーマ"),
            Release("新しい A", new DateOnly(2026, 7, 1), "新しいテーマ"),
            Release("新しい B", new DateOnly(2026, 6, 1), "新しいテーマ"),
        ]);

        Assert.Equal(["新しいテーマ", "古いテーマ"], themes.Select(theme => theme.Tag));
    }

    [Fact]
    public void 同じ本の同じタグは1冊として数える()
    {
        var release = Release("重複タグの本", new DateOnly(2026, 7, 1));
        release.Tags = ["生成ai", "生成ai"];

        var themes = PublishingTrend.Themes([release, Release("もう1冊", new DateOnly(2026, 6, 1), "生成ai")]);

        Assert.Equal(2, Assert.Single(themes).Count);
    }
}

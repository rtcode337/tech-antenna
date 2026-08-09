using System.Text.Json;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Infrastructure.Summarization;

namespace TechAntenna.Tests.Infrastructure;

public class DigestPromptTests
{
    static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    static Article Article(string title, string url, int? bookmarks = null) => new()
    {
        Title = title,
        Url = new Uri(url),
        SourceName = "Zenn",
        BookmarkCount = bookmarks,
        CollectedAt = Now,
    };

    static TechEvent Event(string title, string url) => new()
    {
        Title = title,
        Url = new Uri(url),
        SourceName = "connpass",
        StartsAt = Now.AddDays(3),
        IsOnline = true,
        CollectedAt = Now,
    };

    static DigestMaterials Materials() => new(
        [Article("話題の記事", "https://example.com/hot", bookmarks: 120)],
        [Article("興味の記事", "https://example.com/interest")],
        [Event("LLM 勉強会", "https://example.com/event")],
        ["生成AI", "LLM"]);

    [Fact]
    public void 入力にはトピックと記事とイベントが載る()
    {
        var input = DigestPrompt.ForMaterials(Materials());

        Assert.Contains("生成AI、LLM", input);
        Assert.Contains("話題の記事", input);
        Assert.Contains("はてブ 120", input);
        Assert.Contains("https://example.com/hot", input);
        Assert.Contains("興味の記事", input);
        Assert.Contains("LLM 勉強会", input);
        Assert.Contains("オンライン", input);
    }

    [Fact]
    public void 応答からダイジェストを組む()
    {
        using var doc = JsonDocument.Parse(
            """
            {"lead":"今日は生成AIの話題が中心。",
             "items":[{"title":"見出し","body":"本文。","url":"https://example.com/hot"}]}
            """);

        var digest = DigestPrompt.Read(doc.RootElement, Materials(), "テスト", Now);

        Assert.Equal("今日は生成AIの話題が中心。", digest.Lead);
        Assert.Equal("テスト", digest.GeneratorName);
        var item = Assert.Single(digest.Items);
        Assert.Equal("見出し", item.Title);
        Assert.Equal("https://example.com/hot", item.Url);
    }

    [Fact]
    public void 材料に無いURLはリンクにしない()
    {
        // LLM が作った(材料に無い)URL を画面の href に出さないための検証
        using var doc = JsonDocument.Parse(
            """
            {"lead":"導入。","items":[
              {"title":"捏造リンク","body":"本文。","url":"https://evil.example.com/"},
              {"title":"URL無し","body":"本文。","url":""}]}
            """);

        var digest = DigestPrompt.Read(doc.RootElement, Materials(), "テスト", Now);

        Assert.All(digest.Items, item => Assert.Null(item.Url));
    }

    [Fact]
    public void 壊れた項目は捨てて項目数は上限で切る()
    {
        var items = string.Join(",", Enumerable.Range(1, 20)
            .Select(i => $"{{\"title\":\"見出し{i}\",\"body\":\"本文。\"}}"));
        using var doc = JsonDocument.Parse(
            $"{{\"lead\":\"導入。\",\"items\":[{{\"title\":\"\",\"body\":\"本文。\"}},{items}]}}");

        var digest = DigestPrompt.Read(doc.RootElement, Materials(), "テスト", Now);

        Assert.Equal(DigestPrompt.MaxItems, digest.Items.Count);
        Assert.DoesNotContain(digest.Items, item => item.Title.Length == 0);
    }

    [Fact]
    public void 中身が空の応答は例外にする()
    {
        using var doc = JsonDocument.Parse("""{"lead":"","items":[]}""");

        Assert.Throws<FormatException>(
            () => DigestPrompt.Read(doc.RootElement, Materials(), "テスト", Now));
    }
}

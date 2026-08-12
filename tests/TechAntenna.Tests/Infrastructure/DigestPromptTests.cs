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

    static DigestMaterials Materials(DigestScope scope = DigestScope.Interests) => new(
        scope,
        [Article("話題の記事", "https://example.com/hot", bookmarks: 120),
         Article("興味の記事", "https://example.com/interest")],
        [Event("LLM 勉強会", "https://example.com/event")],
        // 興味トピックは全体のサマリーには渡さない(材料の作り方に合わせる)
        scope == DigestScope.Interests ? ["生成AI", "LLM"] : []);

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

    // 2本を別々の目で書かせるための出し分け。同じ指示・同じ見出しだと、
    // どちらも「話題の総ざらい」になって読み分けられない
    [Fact]
    public void 守備範囲で指示文と材料の見出しが変わる()
    {
        Assert.Contains("技術界隈全体", DigestPrompt.SystemFor(DigestScope.Overall));
        Assert.Contains("興味トピック", DigestPrompt.SystemFor(DigestScope.Interests));

        Assert.Contains("## 直近の話題", DigestPrompt.ForMaterials(Materials(DigestScope.Overall)));
        Assert.Contains(
            "## 興味トピックに当たる直近の記事",
            DigestPrompt.ForMaterials(Materials(DigestScope.Interests)));
    }

    // 保存先とホームの出し分け・通知のタイトルが守備範囲で決まるので、
    // 材料の範囲がそのままダイジェストに乗ること
    [Fact]
    public void ダイジェストは材料の守備範囲を引き継ぐ()
    {
        using var doc = JsonDocument.Parse("""{"lead":"導入。","items":[]}""");

        Assert.Equal(
            DigestScope.Interests,
            DigestPrompt.Read(doc.RootElement, Materials(DigestScope.Interests), "テスト", Now).Scope);
        Assert.Equal(
            DigestScope.Overall,
            DigestPrompt.Read(doc.RootElement, Materials(DigestScope.Overall), "テスト", Now).Scope);
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

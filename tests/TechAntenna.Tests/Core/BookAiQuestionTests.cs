using TechAntenna.Core;
using TechAntenna.Core.Models;

namespace TechAntenna.Tests.Core;

public class BookAiQuestionTests
{
    static Book NewBook(
        string title = "リーダブルコード",
        IReadOnlyList<string>? authors = null,
        string? publisher = "オライリー・ジャパン",
        string? isbn13 = "9784873115658") =>
        new()
        {
            Title = title,
            Authors = authors ?? ["Dustin Boswell", "Trevor Foucher"],
            Publisher = publisher,
            Isbn13 = isbn13,
            SourceName = "テスト",
            CollectedAt = DateTimeOffset.UnixEpoch,
        };

    [Fact]
    public void 書名に著者と出版社とISBNを添えて聞く()
    {
        var prompt = BookAiQuestion.Prompt(NewBook());

        Assert.StartsWith(
            "『リーダブルコード』(Dustin Boswell、Trevor Foucher、オライリー・ジャパン、ISBN 9784873115658)",
            prompt,
            StringComparison.Ordinal);
        // 頼むのは解説と、買える場所(一覧には紹介文が無いので、そこを聞かせる)
        Assert.Contains("解説してください", prompt, StringComparison.Ordinal);
        Assert.Contains("購入できるサイトのリンク", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void 取れていない書誌は書かない()
    {
        // 引用で拾った本は ISBN しか持たず、出版社も著者も埋まらないことがある。
        // 「著者不明」と書くと、AI がそれを手がかりに別の本を探しに行く
        var prompt = BookAiQuestion.Prompt(NewBook(authors: [], publisher: null, isbn13: null));

        Assert.Equal('『', prompt[0]);
        Assert.DoesNotContain("(", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("不明", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void 長い書名と多すぎる著者は切る()
    {
        // プロンプトは URL に載るので、NDL 由来の長い書名がそのまま入ると URL が伸びる
        var book = NewBook(
            title: new string('あ', 200),
            authors: ["甲", "乙", "丙", "丁", "戊"]);

        var prompt = BookAiQuestion.Prompt(book);

        Assert.Contains(new string('あ', 120) + "…", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('あ', 121), prompt, StringComparison.Ordinal);
        Assert.Contains("甲、乙、丙 ほか", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("丁", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void 行き先はGoogleのAIモード()
    {
        // Gemini 本体は URL でプロンプトを渡せない(拡張機能が要る)ので、
        // 拡張なしでどの端末でも開く AI モード(udm=50)へ送る
        var url = BookAiQuestion.Url(NewBook());

        Assert.Equal("https", url.Scheme);
        Assert.Equal("www.google.com", url.Host);
        Assert.Equal("/search", url.AbsolutePath);
        Assert.Contains("udm=50", url.Query, StringComparison.Ordinal);
        Assert.Contains(
            Uri.EscapeDataString("『リーダブルコード』"), url.Query, StringComparison.Ordinal);
    }
}

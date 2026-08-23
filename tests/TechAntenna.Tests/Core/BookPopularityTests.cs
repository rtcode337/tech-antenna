using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Tests.Core;

public class BookPopularityTests
{
    /// <summary>収集日時は既定で同じ(並びの最後のキーなので、指定した順で差を付けたいときだけ動かす)。</summary>
    static Book NewBook(string title, int collectedDay = 1) => new()
    {
        Title = title,
        SourceName = "テスト",
        CollectedAt = new DateTimeOffset(2026, 8, collectedDay, 0, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void 読んだ本は名指しされていても後ろへ回る()
    {
        // 「読んだかどうか」は外から取れる指標より優先する軸 ——
        // 読み終えた定番がいつまでも先頭にいると、次に読む本を選ぶ用途を果たさない
        var read = NewBook("読み終えた定番");
        read.RecommendedBy = [new SourceArticle("https://example.com/matome")];
        read.ReadAt = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

        var books = new[] { read, NewBook("新しく集めた本", 3), NewBook("先に集めた本") };

        Assert.Equal(
            ["新しく集めた本", "先に集めた本", "読み終えた定番"],
            books.ByPopularity().Select(book => book.Title));
    }

    [Fact]
    public void 名指しが同数なら新しく集めたものを先に出す()
    {
        // レビューを見ていた項は取得元(楽天ブックス)ごと外したので、
        // 票数が並んだときの決め手は収集日時だけになった
        var books = new[] { NewBook("先に集めた本"), NewBook("後から集めた本", 5) };

        Assert.Equal(
            ["後から集めた本", "先に集めた本"],
            books.ByPopularity().Select(book => book.Title));
    }

    [Fact]
    public void 推薦と引用は1票ずつ合算する()
    {
        // 列は分けたまま(まとめ記事の名指しか、トピックの記事での言及か)、
        // 並べ替えのときだけ1つの数にする
        var recommended = NewBook("まとめ記事が1本");
        recommended.RecommendedBy = [new SourceArticle("https://example.com/matome")];
        var cited = NewBook("トピックの記事が2本");
        cited.CitedBy =
            [new SourceArticle("https://example.com/1"), new SourceArticle("https://example.com/2")];

        Assert.Equal(1, BookPopularity.Endorsements(recommended));
        Assert.Equal(2, BookPopularity.Endorsements(cited));
        Assert.Equal(
            ["トピックの記事が2本", "まとめ記事が1本"],
            new[] { recommended, cited }.ByPopularity().Select(book => book.Title));
    }

    [Fact]
    public void 同じ記事が推薦と引用の両方に入っても1票()
    {
        // 「読むべき技術書100選」のようなまとめ記事は技術書のタグと分野のタグを両方持つので、
        // 推薦の固定クエリにも引用のトピック検索にも当たる —— 単純に足すと1本が2票になる
        var book = NewBook("両方に入った本");
        book.RecommendedBy = [new SourceArticle("https://qiita.com/items/aaaa", "読むべき技術書100選")];
        book.CitedBy =
        [
            new SourceArticle("https://qiita.com/items/aaaa", "読むべき技術書100選"),
            new SourceArticle("https://qiita.com/items/bbbb", "機械学習の前処理"),
        ];

        Assert.Equal(2, BookPopularity.Endorsements(book));
    }

    [Fact]
    public void 読んだ本を後ろへ回しても元の並びは崩さない()
    {
        // ReadLast は安定な並べ替え —— 収集日時順で取ってきた一覧(トピック詳細)に
        // 後から掛けても、未読・既読それぞれの中の順序は変わらない
        var second = NewBook("2番目");
        second.ReadAt = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

        var books = new[] { NewBook("1番目"), second, NewBook("3番目"), NewBook("4番目") };

        Assert.Equal(
            ["1番目", "3番目", "4番目", "2番目"],
            books.ReadLast().Select(book => book.Title));
    }
}

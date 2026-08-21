using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Tests.Core;

public class BookPopularityTests
{
    static Book Reviewed(string title, int? count, double? average = null) => new()
    {
        Title = title,
        SourceName = "テスト",
        CollectedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        ReviewCount = count,
        ReviewAverage = average,
    };

    [Fact]
    public void レビューが取れていない本はスコアを出さない()
    {
        // 0(読まれていない)と null(分からない)を混ぜると、取得元を設定する前の本が
        // まとめて最下位に沈む
        Assert.Null(BookPopularity.Score(Reviewed("未取得", null)));
        Assert.Equal(0, BookPopularity.Score(Reviewed("レビュー0件", 0)));
    }

    [Fact]
    public void 件数の少ない高評価より広く読まれた本を上に置く()
    {
        var many = BookPopularity.Score(Reviewed("定番", 200, 4.2));
        var few = BookPopularity.Score(Reviewed("レビュー1件で星5", 1, 5.0));

        Assert.True(many > few, $"定番 {many} > レビュー1件 {few} のはず");
    }

    [Fact]
    public void 同じ件数なら評価の高いほうが上()
    {
        Assert.True(
            BookPopularity.Score(Reviewed("高評価", 50, 4.6))
            > BookPopularity.Score(Reviewed("低評価", 50, 2.8)));
    }

    [Fact]
    public void 読まれている順に並べ未取得は後ろへ回す()
    {
        var books = new[]
        {
            Reviewed("未取得", null),
            Reviewed("レビュー0件", 0),
            Reviewed("そこそこ", 10, 4.0),
            Reviewed("定番", 300, 4.3),
        };

        Assert.Equal(
            ["定番", "そこそこ", "レビュー0件", "未取得"],
            books.ByPopularity().Select(book => book.Title));
    }

    [Fact]
    public void 読んだ本は読まれている度合いに関わらず後ろへ回る()
    {
        // 「読んだかどうか」は外から取れる指標より優先する軸 ——
        // 読み終えた定番がいつまでも先頭にいると、次に読む本を選ぶ用途を果たさない
        var read = Reviewed("読み終えた定番", 300, 4.3);
        read.ReadAt = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

        var books = new[] { read, Reviewed("未取得", null), Reviewed("そこそこ", 10, 4.0) };

        Assert.Equal(
            ["そこそこ", "未取得", "読み終えた定番"],
            books.ByPopularity().Select(book => book.Title));
    }

    [Fact]
    public void 記事に名指しされた本はレビューの多い本より上に来る()
    {
        // 名指し(推薦・引用)は「詳しい人が本文で挙げた」ぶん精度が高い ——
        // レビュー数は「読まれた量」で、一般向けの本ほど有利になる
        var cited = Reviewed("記事が挙げた本", 3, 4.0);
        cited.CitedBy = [new SourceArticle("https://example.com/a", "機械学習の入門")];

        var books = new[] { Reviewed("よく読まれている本", 300, 4.3), cited };

        Assert.Equal(
            ["記事が挙げた本", "よく読まれている本"],
            books.ByPopularity().Select(book => book.Title));
    }

    [Fact]
    public void 推薦と引用は1票ずつ合算する()
    {
        // 列は分けたまま(まとめ記事の名指しか、トピックの記事での言及か)、
        // 並べ替えのときだけ1つの数にする
        var recommended = Reviewed("まとめ記事が1本", 0);
        recommended.RecommendedBy = [new SourceArticle("https://example.com/matome")];
        var cited = Reviewed("トピックの記事が2本", 0);
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
        var book = Reviewed("両方に入った本", 0);
        book.RecommendedBy = [new SourceArticle("https://qiita.com/items/aaaa", "読むべき技術書100選")];
        book.CitedBy =
        [
            new SourceArticle("https://qiita.com/items/aaaa", "読むべき技術書100選"),
            new SourceArticle("https://qiita.com/items/bbbb", "機械学習の前処理"),
        ];

        Assert.Equal(2, BookPopularity.Endorsements(book));
    }

    [Fact]
    public void 票数が同じならレビューの指標で決める()
    {
        var cited = Reviewed("引用1件・レビュー多め", 200, 4.2);
        cited.CitedBy = [new SourceArticle("https://example.com/1")];
        var recommended = Reviewed("推薦1件・レビュー少なめ", 3, 4.0);
        recommended.RecommendedBy = [new SourceArticle("https://example.com/matome")];

        Assert.Equal(
            ["引用1件・レビュー多め", "推薦1件・レビュー少なめ"],
            new[] { recommended, cited }.ByPopularity().Select(book => book.Title));
    }

    [Fact]
    public void 読んだ本を後ろへ回しても元の並びは崩さない()
    {
        // ReadLast は安定な並べ替え —— 収集日時順で取ってきた一覧(トピック詳細)に
        // 後から掛けても、未読・既読それぞれの中の順序は変わらない
        var second = Reviewed("2番目", 0);
        second.ReadAt = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

        var books = new[] { Reviewed("1番目", 0), second, Reviewed("3番目", 0), Reviewed("4番目", 0) };

        Assert.Equal(
            ["1番目", "3番目", "4番目", "2番目"],
            books.ReadLast().Select(book => book.Title));
    }
}

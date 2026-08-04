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
}

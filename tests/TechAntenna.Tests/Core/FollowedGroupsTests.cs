using TechAntenna.Core;

namespace TechAntenna.Tests.Core;

public class FollowedGroupsTests
{
    [Fact]
    public void 収集元と識別子と表示名を読む()
    {
        var groups = FollowedGroups.Parse("connpass:rubykaigi RubyKaigi");

        var group = Assert.Single(groups.All);
        Assert.Equal("connpass", group.Source);
        Assert.Equal("rubykaigi", group.Id);
        Assert.Equal("RubyKaigi", group.Label);
    }

    [Fact]
    public void 表示名を省いたら識別子をそのまま使う()
    {
        var group = Assert.Single(FollowedGroups.Parse("doorkeeper:example").All);

        Assert.Equal("example", group.Id);
        Assert.Equal("example", group.Label);
    }

    [Fact]
    public void 表示名は空白を含んでよい()
    {
        // カードにそのまま出る名前なので、「Google Cloud Japan」を1件として書けること
        var group = Assert.Single(FollowedGroups.Parse("connpass:gcpug Google Cloud Japan").All);

        Assert.Equal("Google Cloud Japan", group.Label);
    }

    [Fact]
    public void 収集元ごとに引ける()
    {
        var groups = FollowedGroups.Parse("""
            connpass:a
            doorkeeper:b
            connpass:c
            """);

        Assert.Equal(["a", "c"], groups.For(FollowedGroups.Connpass).Select(g => g.Id));
        Assert.Equal(["b"], groups.For(FollowedGroups.Doorkeeper).Select(g => g.Id));
    }

    [Fact]
    public void コメント行と空行は読み飛ばす()
    {
        var groups = FollowedGroups.Parse("""
            # 去年から追いかけている
            connpass:a

            """);

        Assert.Single(groups.All);
        Assert.Empty(FollowedGroups.Rejected("# メモだけ"));
    }

    [Fact]
    public void 同じ収集元の同じ識別子は1件にまとめる()
    {
        var groups = FollowedGroups.Parse("""
            connpass:a あ
            connpass:A い
            """);

        Assert.Single(groups.All);
    }

    [Theory]
    [InlineData("connpass")]          // 識別子が無い
    [InlineData("connpass:")]         // 同上
    [InlineData("unknown:a")]         // 知らない収集元
    [InlineData("ただのメモ")]        // 書式ですらない
    public void 読めない行は捨てて拾えるようにする(string line)
    {
        // 1行の打ち間違いで保存ごと失敗させない。代わりに画面へ出して直してもらう
        Assert.Empty(FollowedGroups.Parse(line).All);
        Assert.Equal([line], FollowedGroups.Rejected(line));
    }

    [Fact]
    public void 未設定は何も購読していない扱い()
    {
        // 初期値は持たない —— 実在のシリーズ ID をリポジトリに憶測で書けないため
        Assert.Empty(FollowedGroups.Parse(null).All);
        Assert.Empty(FollowedGroups.Parse("").All);
        Assert.Empty(FollowedGroups.Empty.All);
    }

    [Fact]
    public void 書き出したものを読み直すと同じになる()
    {
        var groups = FollowedGroups.Parse("""
            connpass:rubykaigi RubyKaigi
            doorkeeper:example
            """);

        var round = FollowedGroups.Parse(FollowedGroups.Format(groups.All));

        Assert.Equal(
            groups.All.Select(g => (g.Source, g.Id, g.Label)),
            round.All.Select(g => (g.Source, g.Id, g.Label)));
    }
}

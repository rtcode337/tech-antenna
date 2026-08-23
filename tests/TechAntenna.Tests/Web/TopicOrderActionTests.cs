using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

/// <summary>
/// 並べ替えボタンの value(`up:&lt;キー&gt;` / `down:&lt;キー&gt;`)の読み書き。
/// 描くのと受けるのが離れているので、書式がずれると押しても何も起きない形で静かに壊れる。
/// </summary>
public class TopicOrderActionTests
{
    [Fact]
    public void 上と下を向きで見分ける()
    {
        Assert.True(TopicOrderAction.TryRead(TopicOrderAction.UpValue("生成ai"), out var up, out var upDelta));
        Assert.Equal(("生成ai", -1), (up, upDelta));

        Assert.True(TopicOrderAction.TryRead(TopicOrderAction.DownValue("生成ai"), out var down, out var downDelta));
        Assert.Equal(("生成ai", 1), (down, downDelta));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    // 「読んだ」の切り替えと同じフォームに載るので、他の submit を拾わないこと
    [InlineData("toggle:0b6f6d1e-0000-0000-0000-000000000000")]
    [InlineData("up:")]
    public void 他のボタンや空の値は読まない(string? action)
    {
        Assert.False(TopicOrderAction.TryRead(action, out _, out var delta));
        Assert.Equal(0, delta);
    }
}

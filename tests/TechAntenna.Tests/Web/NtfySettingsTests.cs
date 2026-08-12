using System.Text.RegularExpressions;
using TechAntenna.Web.Services;

namespace TechAntenna.Tests.Web;

public class NtfySettingsTests
{
    // ntfy が受けるトピック名は [-_A-Za-z0-9]{1,64}。こちらはさらに狭めて、
    // 見間違えやすい文字(l・1・I・0・O)を使わない小文字と数字だけにしてある
    [Fact]
    public void 生成したトピック名は打ち写せる文字だけで作る()
    {
        var topic = NtfySettings.GenerateTopic();

        Assert.Matches(new Regex("^tech-antenna-[a-km-z2-9]{16}$"), topic);
        Assert.InRange(topic.Length, 1, 64);
    }

    // 推測されると誰でも購読・投稿できてしまうので、呼ぶたびに違う名前になること
    // (定数を返す実装・種が固定の乱数への差し替えをここで止める)
    [Fact]
    public void 生成したトピック名は毎回違う()
    {
        var topics = Enumerable.Range(0, 50).Select(_ => NtfySettings.GenerateTopic()).ToList();

        Assert.Equal(topics.Count, topics.Distinct().Count());
    }
}

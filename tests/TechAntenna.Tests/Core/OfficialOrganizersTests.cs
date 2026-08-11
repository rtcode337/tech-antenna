using TechAntenna.Core;

namespace TechAntenna.Tests.Core;

public class OfficialOrganizersTests
{
    [Fact]
    public void 名簿の語を含む主催者を公式とみなす()
    {
        var organizers = OfficialOrganizers.Parse("Microsoft\nマイクロソフト");

        // 部分一致で拾う(法人格や部署名が前後に付くため)
        Assert.True(organizers.IsOfficial("日本マイクロソフト株式会社"));
        Assert.True(organizers.IsOfficial("Microsoft Developer Japan"));
        Assert.False(organizers.IsOfficial("札幌 IT 勉強会"));
    }

    [Fact]
    public void 語の端が英数字なら境界を見る()
    {
        var organizers = OfficialOrganizers.Parse("AI");

        // 「AI」が「Rails」に当たらない(KeywordMatcher の規則)
        Assert.False(organizers.IsOfficial("Rails コミュニティ"));
        Assert.True(organizers.IsOfficial("生成AI 推進室"));
    }

    [Fact]
    public void 主催者が取れていないイベントは公式にしない()
    {
        var organizers = OfficialOrganizers.Parse("Microsoft");

        // 「公式でない」ではなく「分からない」—— バッジを出さないだけにとどめる
        Assert.False(organizers.IsOfficial(null));
        Assert.False(organizers.IsOfficial("   "));
    }

    [Fact]
    public void 空の名簿は初期値に戻す()
    {
        // 空を保存できると「公式が1件も出ない」と「設定していない」が画面から見分けられない
        Assert.Equal(OfficialOrganizers.Defaults, OfficialOrganizers.Parse("").Names);
        Assert.Equal(OfficialOrganizers.Defaults, OfficialOrganizers.Parse(null).Names);
        Assert.Equal(OfficialOrganizers.Defaults, OfficialOrganizers.Parse("# メモだけ").Names);
    }

    [Fact]
    public void コメント行と重複を落として読む()
    {
        var organizers = OfficialOrganizers.Parse("""
            # 提供元だけを並べる
            Microsoft
            microsoft
              GitHub

            """);

        Assert.Equal(["Microsoft", "GitHub"], organizers.Names);
    }

    [Fact]
    public void 保存した形をそのまま読み直せる()
    {
        var saved = OfficialOrganizers.Format(["Microsoft", "GitHub"]);

        Assert.Equal(["Microsoft", "GitHub"], OfficialOrganizers.Parse(saved).Names);
    }

    [Fact]
    public void 初期値はベンダー名を持つ()
    {
        Assert.True(OfficialOrganizers.Default.IsOfficial("Google Cloud Japan"));
        Assert.True(OfficialOrganizers.Default.IsOfficial("AWS Japan"));
        // 名簿を持たないものは何も公式にしない(テスト用のつなぎ)
        Assert.False(OfficialOrganizers.Empty.IsOfficial("Google Cloud Japan"));
    }
}

using TechAntenna.Core;
using TechAntenna.Core.Models;

namespace TechAntenna.Tests.Core;

/// <summary>
/// 公式の名簿 → 購読の名簿の橋渡し。外部へ問い合わせずに識別子を起こすのが要点なので、
/// 「URL から正しく起こせるか」「起こせないものを候補にしないか」を見張る。
/// </summary>
public class FollowSuggestionsTests
{
    // 名簿は英語表記と日本語表記を別の行で持つ(初期値と同じ流儀)——
    // 「Microsoft」の部分一致では「日本マイクロソフト株式会社」に当たらない
    static readonly OfficialOrganizers Official =
        OfficialOrganizers.Parse("Microsoft\nマイクロソフト\nGoogle");

    static OrganizerGroup Group(string organizer, string url, string source = "connpass", int count = 3) =>
        new(organizer, source, new Uri(url), count);

    [Fact]
    public void 公式の主催者だけを候補にする()
    {
        var suggestions = FollowSuggestions.From(
            [
                Group("日本マイクロソフト株式会社", "https://msdevjp.connpass.com/event/1/"),
                Group("地域コミュニティ", "https://local.connpass.com/event/2/"),
            ],
            Official,
            FollowedGroups.Empty);

        var only = Assert.Single(suggestions);
        Assert.Equal("connpass", only.Source);
        Assert.Equal("msdevjp", only.Id);
        Assert.Equal("日本マイクロソフト株式会社", only.Label);
    }

    [Fact]
    public void すでに購読しているグループは出さない()
    {
        // 押しても何も変わらない行を並べない
        var followed = FollowedGroups.Parse("connpass:msdevjp  Microsoft");

        Assert.Empty(FollowSuggestions.From(
            [Group("Microsoft", "https://msdevjp.connpass.com/event/1/")], Official, followed));
    }

    [Fact]
    public void Doorkeeper_のコミュニティも起こせる()
    {
        var suggestions = FollowSuggestions.From(
            [Group("Google Japan", "https://gdgtokyo.doorkeeper.jp/events/9", source: "Doorkeeper")],
            Official,
            FollowedGroups.Empty);

        var only = Assert.Single(suggestions);
        Assert.Equal("doorkeeper", only.Source);
        Assert.Equal("gdgtokyo", only.Id);
    }

    [Fact]
    public void グループを持たない_URL_は候補にしない()
    {
        // サブドメインの無い connpass の個人イベントや、グループの概念が無い TECH PLAY
        Assert.Empty(FollowSuggestions.From(
            [
                Group("Microsoft", "https://connpass.com/event/1/"),
                Group("Microsoft", "https://techplay.jp/event/999"),
                Group("Microsoft", "https://www.doorkeeper.jp/events/9"),
            ],
            Official,
            FollowedGroups.Empty));
    }

    [Fact]
    public void 同じグループに複数の主催者名があれば件数を足す()
    {
        // 表記ゆれ・部署違いで主催者名が割れることがある。表示名は件数の多かったほう
        var suggestions = FollowSuggestions.From(
            [
                Group("Microsoft", "https://msdevjp.connpass.com/event/1/", count: 2),
                Group("日本マイクロソフト", "https://msdevjp.connpass.com/event/2/", count: 5),
            ],
            Official,
            FollowedGroups.Empty);

        var only = Assert.Single(suggestions);
        Assert.Equal(7, only.Count);
        Assert.Equal("日本マイクロソフト", only.Label);
    }

    [Fact]
    public void 件数の多い順に並べる()
    {
        var suggestions = FollowSuggestions.From(
            [
                Group("Microsoft", "https://msdevjp.connpass.com/event/1/", count: 2),
                Group("Google", "https://gdgtokyo.connpass.com/event/2/", count: 9),
            ],
            Official,
            FollowedGroups.Empty);

        Assert.Equal(["Google", "Microsoft"], suggestions.Select(s => s.Label));
    }

    [Fact]
    public void 名簿にそのまま写せる行を作る()
    {
        var suggestion = Assert.Single(FollowSuggestions.From(
            [Group("Microsoft", "https://msdevjp.connpass.com/event/1/")], Official, FollowedGroups.Empty));

        // 書式は FollowedGroups が読める形(読み書きで同じ表記)
        Assert.Equal("connpass:msdevjp Microsoft", suggestion.Line);
        var parsed = Assert.Single(FollowedGroups.Parse(suggestion.Line).All);
        Assert.Equal("msdevjp", parsed.Id);
        Assert.Equal("Microsoft", parsed.Label);
    }
}

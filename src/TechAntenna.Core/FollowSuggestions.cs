using TechAntenna.Core.Models;

namespace TechAntenna.Core;

/// <summary>
/// 購読の候補1件。名簿の 1 行(<c>&lt;収集元&gt;:&lt;識別子&gt; &lt;表示名&gt;</c>)にそのまま写せる形。
/// </summary>
/// <param name="Count">その候補で既に集まっているイベントの件数(多い順に出すため)。</param>
public sealed record FollowSuggestion(string Source, string Id, string Label, int Count)
{
    /// <summary>名簿に足す 1 行。<b>書式は <see cref="FollowedGroups"/> の読み取りと同じ</b>。</summary>
    public string Line => FollowedGroups.Format([new FollowedGroup(Source, Id, Label)]);
}

/// <summary>
/// <b>公式の名簿(<see cref="OfficialOrganizers"/>)を、購読の名簿(<see cref="FollowedGroups"/>)へ橋渡しする。</b>
///
/// 「公式」は<b>集めたイベントに付ける印</b>でしかなく、公式のイベントを狙って取りに行く経路は無い ——
/// 提供元のイベントが一覧に載るかどうかは、たまたま検索語に当たったかどうかに委ねられていた。
/// 一方で<b>グループを購読すればトピックに関係なく全部入る</b>ので、
/// 「すでに集まっているイベントのうち、公式と判定された主催者のグループ」を候補として出せば、
/// 名簿を人が調べて書き写す手間なしに購読へ移せる。
///
/// <b>グループの識別子は保存済みイベントの URL から起こす</b>(connpass も Doorkeeper も
/// サブドメインがグループ)—— 主催者名から ID は引けないし、引くには外部への問い合わせが要る。
/// </summary>
public static class FollowSuggestions
{
    const string ConnpassSuffix = ".connpass.com";
    const string DoorkeeperSuffix = ".doorkeeper.jp";

    /// <summary>グループのページではないサブドメイン(ここを候補にすると存在しない名簿の行ができる)。</summary>
    static readonly string[] NotGroups = ["www", "api", "rss"];

    /// <summary>
    /// 候補を作る。<b>公式と判定される主催者だけ</b>を対象にし、
    /// <b>すでに購読しているものは出さない</b>(押しても何も変わらない行を並べない)。
    /// 並びは件数の多い順 —— 実際に集まっている数が「追いかける価値」の目安になる。
    /// </summary>
    public static IReadOnlyList<FollowSuggestion> From(
        IEnumerable<OrganizerGroup> groups, OfficialOrganizers official, FollowedGroups followed)
    {
        var byKey = new Dictionary<string, FollowSuggestion>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            if (!official.IsOfficial(group.Organizer)
                || !TryGroupOf(group.SampleUrl, out var source, out var id)
                || followed.Contains(source, id))
            {
                continue;
            }

            // 同じグループに複数の主催者名が紐づくことがある(表記ゆれ・部署違い)。
            // **件数は足し、表示名は件数の多かったほうを採る**
            var key = $"{source}:{id}";
            byKey[key] = byKey.TryGetValue(key, out var existing)
                ? existing with
                {
                    Label = existing.Count >= group.Count ? existing.Label : group.Organizer,
                    Count = existing.Count + group.Count,
                }
                : new FollowSuggestion(source, id, group.Organizer, group.Count);
        }

        return [.. byKey.Values.OrderByDescending(s => s.Count).ThenBy(s => s.Label, StringComparer.Ordinal)];
    }

    /// <summary>
    /// イベントの URL からグループを起こす。<b>収集元は URL のホストで見分ける</b> ——
    /// <c>SourceName</c> は経路ごとに違う名前(「connpass」「connpass(面掃き)」)を持つので、
    /// そちらで分岐すると経路を足すたびにここも直すことになる。
    /// TECH PLAY のようにグループの概念が無い収集元は false(候補にしない)。
    /// </summary>
    public static bool TryGroupOf(Uri url, out string source, out string id)
    {
        source = "";
        id = "";

        var host = url.Host.ToLowerInvariant();
        var (suffix, name) = host.EndsWith(ConnpassSuffix, StringComparison.Ordinal)
            ? (FollowedGroups.Connpass, host[..^ConnpassSuffix.Length])
            : host.EndsWith(DoorkeeperSuffix, StringComparison.Ordinal)
                ? (FollowedGroups.Doorkeeper, host[..^DoorkeeperSuffix.Length])
                : ("", "");

        // グループを持たないイベント(https://connpass.com/event/… )はサブドメインが無い
        if (suffix.Length == 0 || name.Length == 0 || name.Contains('.') || NotGroups.Contains(name))
        {
            return false;
        }

        source = suffix;
        id = name;

        return true;
    }
}

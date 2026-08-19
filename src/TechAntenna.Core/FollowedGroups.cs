namespace TechAntenna.Core;

/// <summary>
/// 追いかけるグループ(connpass のシリーズ / Doorkeeper のコミュニティ)1件。
/// </summary>
/// <param name="Source">収集元の名前。<see cref="FollowedGroups.Connpass"/> か <see cref="FollowedGroups.Doorkeeper"/>。</param>
/// <param name="Id">
/// 収集元での識別子。connpass は<b>シリーズ ID(数字)かサブドメイン</b>
/// (<c>https://&lt;サブドメイン&gt;.connpass.com/</c> の頭。数字でなければ収集元が ID に解決する)、
/// Doorkeeper は<b>グループ名</b>(<c>https://&lt;グループ名&gt;.doorkeeper.jp/</c> の頭)。
/// </param>
/// <param name="Label">
/// 画面に出す名前。イベントが<b>なぜトピックの選択に関係なく載っているか</b>の説明になるので、
/// 「rubykaigi」ではなく「RubyKaigi」のように人が読める形で書く。省略時は <paramref name="Id"/>。
/// </param>
public sealed record FollowedGroup(string Source, string Id, string Label);

/// <summary>
/// <b>「探す」のではなく「発信元を購読する」</b>ための名簿。
///
/// キーワード検索だけでイベントを集めると、<b>固有名詞のカンファレンス</b>
/// (RubyKaigi・DroidKaigi・AWS Summit)が構造的に落ちる —— 収集語(AI・LLM…)が
/// タイトルに入っていないためで、代わりに「AI」を名前に持つ小さな勉強会ばかりが残る。
/// そこで<b>グループ単位で直接引く経路</b>を持ち、検索語に一致するかどうかを問わない。
///
/// 名簿の作りは <see cref="OfficialOrganizers"/> と同じ流儀 —— 1行1件のテキストで
/// DB(<c>Secrets</c>)に持ち、画面から直せる。違うのは<b>初期値を持たないこと</b>で、
/// シリーズ ID もグループ名も実在の値なので、リポジトリに憶測で書けない
/// (未設定なら「この経路では何も集めない」が正しい状態)。
/// </summary>
public sealed class FollowedGroups
{
    /// <summary>収集元の名前。名簿の行頭に書く語で、収集元側の <c>IEventSource.Name</c> とは別(こちらは小文字固定)。</summary>
    public const string Connpass = "connpass";

    /// <summary>同上。</summary>
    public const string Doorkeeper = "doorkeeper";

    /// <summary>名簿に書ける収集元。ここに無い行は読み飛ばす(打ち間違いを黙って別物として扱わない)。</summary>
    public static IReadOnlyList<string> KnownSources { get; } = [Connpass, Doorkeeper];

    readonly IReadOnlyList<FollowedGroup> _groups;

    FollowedGroups(IReadOnlyList<FollowedGroup> groups) => _groups = groups;

    /// <summary>何も購読していない名簿。<b>これが未設定時の既定</b>(初期値は持たない)。</summary>
    public static FollowedGroups Empty { get; } = new([]);

    /// <summary>名簿の全件(書いた順)。</summary>
    public IReadOnlyList<FollowedGroup> All => _groups;

    /// <summary>その収集元ぶんだけ。収集元は自分の分だけを見る。</summary>
    public IReadOnlyList<FollowedGroup> For(string source) =>
        [.. _groups.Where(group => string.Equals(group.Source, source, StringComparison.OrdinalIgnoreCase))];

    /// <summary>
    /// 画面で編集した名簿(1行1件)を読む。書式は <c>&lt;収集元&gt;:&lt;識別子&gt; [表示名]</c> で、
    /// <c>#</c> で始まる行はコメントとして落とす(なぜ入れたかを名簿に書き残せるように)。
    ///
    /// <b>読めない行は黙って捨てる</b> —— 名簿は人が手で書くものなので、1行の打ち間違いで
    /// 保存ごと失敗すると直す手間のほうが大きい。捨てた行は <see cref="Rejected"/> で
    /// 拾えるようにしてあり、画面がそれを出して気づけるようにしている。
    /// </summary>
    public static FollowedGroups Parse(string? text) => ParseWithRejected(text).Groups;

    /// <summary>読めなかった行(画面に出して直してもらうため)。</summary>
    public static IReadOnlyList<string> Rejected(string? text) => ParseWithRejected(text).Rejected;

    static (FollowedGroups Groups, IReadOnlyList<string> Rejected) ParseWithRejected(string? text)
    {
        var groups = new List<FollowedGroup>();
        var rejected = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in (text ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            if (TryParseLine(line, out var group) && seen.Add($"{group.Source}:{group.Id}"))
            {
                groups.Add(group);
            }
            else
            {
                rejected.Add(line);
            }
        }

        return (new FollowedGroups(groups), rejected);
    }

    static bool TryParseLine(string line, out FollowedGroup group)
    {
        group = default!;

        var colon = line.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        var source = line[..colon].Trim();
        if (!KnownSources.Contains(source, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // 識別子のあとは表示名。空白より後ろは全部が表示名(「Google Cloud Japan」のように
        // 空白を含む名前をそのまま書けるようにするため)
        var rest = line[(colon + 1)..].Trim();
        if (rest.Length == 0)
        {
            return false;
        }

        var space = rest.IndexOfAny([' ', '\t', '　']);
        var id = space < 0 ? rest : rest[..space].Trim();
        var label = space < 0 ? "" : rest[(space + 1)..].Trim();
        if (id.Length == 0)
        {
            return false;
        }

        group = new FollowedGroup(source.ToLowerInvariant(), id, label.Length > 0 ? label : id);

        return true;
    }

    /// <summary>
    /// もう名簿に入っているか。<b>収集元と識別子の組で見る</b>(表示名は問わない)——
    /// 購読の候補を出す側が「すでに追いかけているもの」を除くために使う。
    /// </summary>
    public bool Contains(string source, string id) =>
        _groups.Any(group =>
            string.Equals(group.Source, source, StringComparison.OrdinalIgnoreCase)
            && string.Equals(group.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>保存する形。読み書きで同じ表記にそろえる。</summary>
    public static string Format(IEnumerable<FollowedGroup> groups) =>
        string.Join("\n", groups.Select(group =>
            group.Label == group.Id
                ? $"{group.Source}:{group.Id}"
                : $"{group.Source}:{group.Id} {group.Label}"));
}

namespace TechAntenna.Core;

/// <summary>
/// 「公式のイベント」= <b>その技術の提供元(ベンダー)自身が主催しているイベント</b>を、
/// 主催者名の名簿で見分ける。コミュニティの勉強会と、提供元が出す一次情報の場を
/// 分けて読めるようにするためのもの。
///
/// 判定は保存せず、表示のたびに名簿と突き合わせる(保存するのは
/// <see cref="Models.TechEvent.Organizer"/> = 主催者名のほう)。名簿は画面から直せるので、
/// 直した結果が過去のイベントにも効いてほしい —— タグを <c>RawTags</c> から作り直すのと同じ考え方。
///
/// 突き合わせは <see cref="KeywordMatcher"/> の部分一致。「日本マイクロソフト株式会社」を
/// 「Microsoft」で拾いたいので完全一致にはできないが、素の <c>Contains</c> だと
/// 「AI」が「Rails」に当たるので、語の端が英数字のときだけ境界を求める規則を使う。
/// 誤って公式になる主催者は出るので、名簿を直せる画面(設定 → 主催者)から
/// 実際の主催者一覧を見て調整できるようにしてある。
/// </summary>
public sealed class OfficialOrganizers
{
    /// <summary>
    /// 名簿の初期値。技術の提供元(ベンダー)の名前だけを並べる ——
    /// ユーザーグループやコミュニティの名前(JAWS-UG など)は入れない。
    /// 日本法人が主催者名になることが多いので、日本語表記も併せて持つ。
    /// </summary>
    public static IReadOnlyList<string> Defaults { get; } =
    [
        "Microsoft",
        "マイクロソフト",
        "Google",
        "グーグル",
        "Amazon Web Services",
        "AWS",
        "アマゾン ウェブ サービス",
        "Anthropic",
        "OpenAI",
        "GitHub",
        "GitLab",
        "Atlassian",
        "JetBrains",
        "Docker",
        "HashiCorp",
        "Red Hat",
        "レッドハット",
        "Oracle",
        "オラクル",
        "IBM",
        "日本アイ・ビー・エム",
        "NVIDIA",
        "Intel",
        "Elastic",
        "MongoDB",
        "Datadog",
        "Cloudflare",
        "Vercel",
        "Databricks",
        "Snowflake",
        "Salesforce",
        "Confluent",
        "Grafana Labs",
        "SUSE",
        "VMware",
        "Cisco",
        "さくらインターネット",
        "サイボウズ",
        "LINEヤフー",
    ];

    readonly IReadOnlyList<string> _names;

    OfficialOrganizers(IReadOnlyList<string> names) => _names = names;

    /// <summary>名簿に載っている名前(画面に出す用。設定していなければ初期値がそのまま並ぶ)。</summary>
    public IReadOnlyList<string> Names => _names;

    /// <summary>名簿を持たない(何も公式にしない)もの。テストと未設定時のつなぎ。</summary>
    public static OfficialOrganizers Empty { get; } = new([]);

    /// <summary>初期値そのままの名簿。</summary>
    public static OfficialOrganizers Default { get; } = new(Defaults);

    /// <summary>
    /// 画面で編集した名簿(1行1名)を読む。空なら初期値に戻す ——
    /// 空の名簿を保存できてしまうと「公式のバッジが1つも出ない」状態と
    /// 「設定していない」状態が画面から区別できなくなる。
    /// <c>#</c> で始まる行はコメントとして落とす(なぜ入れたかを名簿に書き残せるように)。
    /// </summary>
    public static OfficialOrganizers Parse(string? text)
    {
        var names = (text ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return names.Count == 0 ? Default : new OfficialOrganizers(names);
    }

    /// <summary>保存する形。読み書きで同じ表記にそろえる。</summary>
    public static string Format(IEnumerable<string> names) => string.Join("\n", names);

    /// <summary>
    /// その主催者名が名簿に当たるか。主催者が取れていない(null)イベントは常に false ——
    /// 「公式でない」ではなく「分からない」なので、画面では公式のバッジを出さないだけにとどめ、
    /// 一覧から落としたり順位を下げたりはしない。
    /// </summary>
    public bool IsOfficial(string? organizer) =>
        !string.IsNullOrWhiteSpace(organizer)
        && _names.Any(name => KeywordMatcher.Contains(organizer, name));
}

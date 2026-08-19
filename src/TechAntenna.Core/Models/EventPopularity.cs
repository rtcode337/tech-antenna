namespace TechAntenna.Core.Models;

/// <summary>
/// イベントの注目度。「提供元が出す一次情報か」「どれだけ人が集まっているか」
/// 「外でどれだけ書かれているか」の3つを1つの数にまとめる。
///
/// 記事のはてブ数・書籍のレビュー数と違い、イベントには単独で順位になる数値が無い ——
/// 参加者数は収集元によっては取れず(TECH PLAY の RSS には無い)、公式かどうかは
/// 数値ですらない。そこで<b>公式に一定の下駄を履かせ、参加者数の対数を足す</b>。
/// 対数にするのは、500 人のカンファレンスを 50 人の勉強会の 10 倍には扱わないため。
///
/// 3つめが記事の言及数(<see cref="EventMentions"/>)。参加者数は「その収集元が
/// 数を出しているか」に左右され、自社サイトで完結する大型カンファレンスでは常に 0 になる ——
/// 記事が何本書かれたかは収集元を問わず測れるので、そこを足して沈まないようにする。
/// </summary>
public static class EventPopularity
{
    /// <summary>
    /// 公式(提供元主催)であることの重み。参加者およそ 9 人ぶん
    /// (<c>log10(1+9) = 1</c>)—— 小さな公式セミナーが大規模なコミュニティイベントを
    /// 押しのけない程度に留め、同じくらいの規模なら公式が上に来るようにした値。
    /// </summary>
    const double OfficialBonus = 1.0;

    /// <summary>
    /// 記事の言及 1 本あたりの重み。<b>参加者数より重く見る</b> ——
    /// 記事が書かれるのは参加するよりずっと稀で、書かれたという事実のほうが
    /// 「外から注目されている」に近いため。この値だと参加者数への換算はおおよそ
    /// <b>言及 1 本 = 8 人 / 3 本 = 32 人 / 10 本 = 400 人</b>で、
    /// 「10 本書かれた年1回のカンファレンス」が「400 人集まった勉強会」と釣り合う。
    ///
    /// <b>ここを動かすと注目度順の並びが大きく変わる</b>ので、変えたら実際の一覧で
    /// 見比べること(理屈で決められる値ではない)。
    /// </summary>
    const double MentionWeight = 2.5;

    /// <summary>参加者数のバッジ・カードの強調をこの段階で分ける(「集まっている」の目安)。</summary>
    public const int MidThreshold = 30;

    /// <summary>同上。ここを超えると大きめのカンファレンス・大規模セミナーの規模。</summary>
    public const int HighThreshold = 100;

    /// <summary>
    /// 言及数でカードを強調しはじめる本数。参加者数よりずっと小さい ——
    /// イベントについて記事が書かれること自体が稀なので、2 本でも「書かれている」に値する。
    /// </summary>
    public const int MidMentions = 2;

    /// <summary>同上。ここまで書かれていれば、規模を問わず注目されているとみてよい。</summary>
    public const int HighMentions = 5;

    /// <summary>
    /// <b>注目度の材料を1つでも持っているか</b>(公式・人が集まっている・記事に書かれている)。
    ///
    /// 定番のイベント一覧(<c>/classics/events</c>)がここで足切りする ——
    /// トピックの選択で絞らない一覧なので、材料を持たないものまで並べると
    /// 「注目度の高いイベント」ではなく単なる全件になる。
    /// <b>カードの強調(hot-mid)と同じ規則</b>にしてあるのが要点で、
    /// 「目立たせる基準」と「並べる基準」がずれると、強調されていないものが上位に来る。
    /// </summary>
    public static bool IsNotable(TechEvent techEvent, OfficialOrganizers official) =>
        official.IsOfficial(techEvent.Organizer)
        // null(未取得)は比較が false になるので、そのまま「材料なし」に落ちる
        || techEvent.ParticipantCount >= MidThreshold
        || techEvent.MentionCount >= MidMentions;

    /// <summary>
    /// 注目度。参加者数が取れていない(null)イベントは 0 人として扱う ——
    /// 書籍の <c>ReviewCount</c> のように null を後ろへ回す作りにすると、
    /// 参加者数を持たない収集元(TECH PLAY)のイベントが公式判定ごと沈むため。
    /// そのぶん「注目度順では、数の取れない収集元は後ろに来る」ことを画面に書いてある。
    /// </summary>
    public static double Score(TechEvent techEvent, OfficialOrganizers official) =>
        (official.IsOfficial(techEvent.Organizer) ? OfficialBonus : 0)
        + Math.Log10(1 + Math.Max(0, techEvent.ParticipantCount ?? 0))
        // 言及数も対数にする。参加者数と同じ理由(10 本書かれたイベントを
        // 1 本のイベントの 10 倍には扱わない)に加えて、単位をそろえないと
        // 重み(MentionWeight)が「何人ぶん」の意味を持たなくなる
        + MentionWeight * Math.Log10(1 + Math.Max(0, techEvent.MentionCount ?? 0));

    /// <summary>
    /// 注目度の高い順に並べる。同じ注目度なら開催日の早い順 ——
    /// イベントは日付が主軸なので、並べ替えても「近いものが先」を保つ。
    /// </summary>
    public static IOrderedEnumerable<TechEvent> ByPopularity(
        this IEnumerable<TechEvent> events, OfficialOrganizers official) =>
        events
            .OrderByDescending(techEvent => Score(techEvent, official))
            .ThenBy(techEvent => techEvent.StartsAt);
}

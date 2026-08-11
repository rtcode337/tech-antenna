namespace TechAntenna.Core.Models;

/// <summary>
/// イベントの注目度。**「提供元が出す一次情報か」と「どれだけ人が集まっているか」の
/// 2つを1つの数にまとめる**。
///
/// 記事のはてブ数・書籍のレビュー数と違い、イベントには単独で順位になる数値が無い ——
/// 参加者数は収集元によっては取れず(TECH PLAY の RSS には無い)、公式かどうかは
/// 数値ですらない。そこで<b>公式に一定の下駄を履かせ、参加者数の対数を足す</b>。
/// 対数にするのは、500 人のカンファレンスを 50 人の勉強会の 10 倍には扱わないため。
/// </summary>
public static class EventPopularity
{
    /// <summary>
    /// 公式(提供元主催)であることの重み。**参加者およそ 9 人ぶん**
    /// (<c>log10(1+9) = 1</c>)—— 小さな公式セミナーが大規模なコミュニティイベントを
    /// 押しのけない程度に留め、同じくらいの規模なら公式が上に来るようにした値。
    /// </summary>
    const double OfficialBonus = 1.0;

    /// <summary>参加者数のバッジ・カードの強調をこの段階で分ける(「集まっている」の目安)。</summary>
    public const int MidThreshold = 30;

    /// <summary>同上。ここを超えると大きめのカンファレンス・大規模セミナーの規模。</summary>
    public const int HighThreshold = 100;

    /// <summary>
    /// 注目度。**参加者数が取れていない(null)イベントは 0 人として扱う** ——
    /// 書籍の <c>ReviewCount</c> のように null を後ろへ回す作りにすると、
    /// 参加者数を持たない収集元(TECH PLAY)のイベントが公式判定ごと沈むため。
    /// そのぶん「注目度順では、数の取れない収集元は後ろに来る」ことを画面に書いてある。
    /// </summary>
    public static double Score(TechEvent techEvent, OfficialOrganizers official) =>
        (official.IsOfficial(techEvent.Organizer) ? OfficialBonus : 0)
        + Math.Log10(1 + Math.Max(0, techEvent.ParticipantCount ?? 0));

    /// <summary>
    /// 注目度の高い順に並べる。**同じ注目度なら開催日の早い順** ——
    /// イベントは日付が主軸なので、並べ替えても「近いものが先」を保つ。
    /// </summary>
    public static IOrderedEnumerable<TechEvent> ByPopularity(
        this IEnumerable<TechEvent> events, OfficialOrganizers official) =>
        events
            .OrderByDescending(techEvent => Score(techEvent, official))
            .ThenBy(techEvent => techEvent.StartsAt);
}

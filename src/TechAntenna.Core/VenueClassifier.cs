namespace TechAntenna.Core;

/// <summary>
/// 会場の表記からオンライン開催かどうかを見分ける。
/// どの収集元もオンライン開催のフラグを持たず、会場名に「オンライン」等と書くだけなので、
/// 判定を1か所にまとめて収集元ごとにぶれないようにする。
/// </summary>
public static class VenueClassifier
{
    /// <summary>
    /// 与えられた表記(会場名・住所など)のどれかがオンライン開催を示していれば true。
    /// </summary>
    public static bool IsOnline(params string?[] venueTexts) =>
        venueTexts.Any(text => text is not null
            && (text.Contains("オンライン")
                || text.Contains("online", StringComparison.OrdinalIgnoreCase)));
}

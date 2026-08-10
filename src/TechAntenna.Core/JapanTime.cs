using System.Globalization;

namespace TechAntenna.Core;

/// <summary>
/// 人に見せる日時は日本時間(JST)にそろえる。**画面・通知・LLM への入力は必ずここを通す**。
///
/// 保存は UTC だが、収集元から受け取った <c>DateTimeOffset</c> は元の時差を持ったままなので、
/// そのまま書式化すると一覧の中で <c>+00:00</c> と <c>+09:00</c> と <c>-05:00</c> が混ざる。
/// かといって <c>ToLocalTime()</c> は動かしている環境の TZ 次第で、コンテナ(TZ=Asia/Tokyo)と
/// 開発ホストで表示がずれる —— **読む人は日本にいる**ので、環境に関わらず JST に固定する。
///
/// 時差は <c>TimeZoneInfo</c> ではなく固定の +09:00 で持つ。JST に夏時間は無く(1951 年で終了)、
/// タイムゾーン DB を引かないぶんコンテナに tzdata が無くても壊れないため。
/// </summary>
public static class JapanTime
{
    /// <summary>JST の時差(夏時間は無いので固定)。</summary>
    public static readonly TimeSpan Offset = TimeSpan.FromHours(9);

    /// <summary>JST に直した値。書式を自分で決めたいときに使う。</summary>
    public static DateTimeOffset To(DateTimeOffset value) => value.ToOffset(Offset);

    /// <summary>一覧・詳細に出す絶対時刻(<c>2026-08-10 21:30 JST</c>)。</summary>
    public static string Format(DateTimeOffset value) => $"{Text(value, "yyyy-MM-dd HH:mm")} JST";

    /// <summary>年を省いた短い形(<c>8/10 21:30 JST</c>)。見出しや通知のタイトル向け。</summary>
    public static string FormatShort(DateTimeOffset value) => $"{Text(value, "M/d HH:mm")} JST";

    /// <summary>日付だけ(<c>2026-08-10</c>)。</summary>
    public static string FormatDate(DateTimeOffset value) => Text(value, "yyyy-MM-dd");

    /// <summary>
    /// ゾーンの字を落とした形(<c>2026-08-10 21:30</c>)。行が何百と並ぶ表で、
    /// 全行に付く「JST」が邪魔になる列に使う。
    /// </summary>
    public static string FormatCompact(DateTimeOffset value) => Text(value, "yyyy-MM-dd HH:mm");

    /// <summary>ファイル名に入れる形(<c>20260810-2130</c>)。</summary>
    public static string FormatStamp(DateTimeOffset value) => Text(value, "yyyyMMdd-HHmm");

    // 書式は文化圏に依らせない(和暦や区切りの違いで、同じ画面の日時が揃わなくなるため)
    static string Text(DateTimeOffset value, string format) =>
        To(value).ToString(format, CultureInfo.InvariantCulture);
}

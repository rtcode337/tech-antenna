using System.Globalization;
using TechAntenna.Core;

namespace TechAntenna.Web.Services;

/// <summary>
/// 定期実行の設定。**ジョブごとのオン/オフ**と、**1日のうち何時に走らせるか**(何個でも)を持つ。
///
/// かつてはジョブごとに「N 分ごと」の周期で回していたが、それだと収集・仕分け・サマリーが
/// 互いの結果を待たずにばらばらに走り、サマリーが古い材料で作られることがあった。
/// いまは<b>指定した時刻に1回だけ、決まった順で通しで走らせる</b>
/// (順番は <see cref="ScheduledJobs"/>)。
///
/// 値は API キーと同じく DB(<c>Secrets</c>)に持ち、実行のたびに読むので**再起動なしで効く**。
/// 既定はすべて無効・時刻なし —— 消し忘れたサーバーが収集先を叩き続けたり、
/// LLM や外部 API の無料枠を使い切ったりしないため。
///
/// **時刻は日本時間で解釈する**(画面に出す時刻と同じ。CLAUDE.md「日時の表示」)。
/// </summary>
public static class ScheduleSettings
{
    /// <summary>実行する時刻の一覧(<c>"07:00, 19:00"</c> の形で保存する)。</summary>
    public const string TimesName = "Schedule:Times";

    /// <summary>最後に定期実行を走らせた時刻(ISO 8601)。**設定ではなく状態**。</summary>
    public const string LastRunName = "Schedule:LastRunAt";

    /// <summary>時刻はいくつでも足せるが、際限なく増やされても意味が無いのでここで止める。</summary>
    public const int MaxTimes = 24;

    /// <summary>ジョブごとのオン/オフの設定キー。</summary>
    public static string EnabledName(string jobKey) => $"Schedule:Job:{jobKey}";

    /// <summary>そのジョブを定期実行に含めるか(既定は無効)。</summary>
    public static bool IsEnabled(ApiCredentials credentials, string jobKey) =>
        credentials.Get(EnabledName(jobKey)) == "true";

    /// <summary>
    /// 「次にいつ何件走るか」の1行。**画面と、チェックのその場保存の応答で同じ文を出す**ため、
    /// 組み立てをここ1か所に置く —— 別々に書くと、チェックを入れた直後の画面だけ
    /// 「走るジョブがありません」のような古い文言が残る。
    ///
    /// **強調(太字)は入れない。** JS が差し替えるので、混ぜると差し替えた行だけ
    /// 見た目が変わる。
    /// </summary>
    public static string Describe(
        IReadOnlyList<TimeOnly> times, int enabledCount, DateTimeOffset now)
    {
        if (times.Count == 0)
        {
            return "いまは定期実行しません（時刻が未設定）。";
        }

        if (enabledCount == 0)
        {
            return "時刻は設定されていますが、走るジョブがありません（チェックが1つも入っていません）。";
        }

        return NextOccurrence(times, now) is { } next
            ? $"次は {JapanTime.Format(next)} に {enabledCount} 件のジョブが走ります。"
            : "";
    }

    /// <summary>設定されている実行時刻(早い順)。未設定なら空 = 定期実行しない。</summary>
    public static IReadOnlyList<TimeOnly> GetTimes(ApiCredentials credentials) =>
        TryParseTimes(credentials.Get(TimesName), out var times, out _) ? times : [];

    /// <summary>保存する形。読み書きで同じ表記にそろえる。</summary>
    public static string Format(IEnumerable<TimeOnly> times) =>
        string.Join(", ", times.Select(time => time.ToString("HH:mm", CultureInfo.InvariantCulture)));

    /// <summary>
    /// 画面に打たれた時刻を読む。区切りはカンマ(半角・全角)・空白・改行のどれでもよい ——
    /// 「07:00, 19:00」と「07:00 19:00」で結果が変わると、打ち直しの理由が画面から分からない。
    /// 重複は落として早い順にそろえる。
    /// </summary>
    public static bool TryParseTimes(
        string? input, out IReadOnlyList<TimeOnly> times, out string? error)
    {
        var parsed = new List<TimeOnly>();
        error = null;

        foreach (var token in (input ?? "").Split(
            [',', '、', ' ', '\t', '\r', '\n', '　'], StringSplitOptions.RemoveEmptyEntries))
        {
            // 「7:00」も「07:00」も受ける(打ち方の違いで弾かない)
            if (!TimeOnly.TryParseExact(token, ["HH:mm", "H:mm"], CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var time))
            {
                times = [];
                error = $"「{token}」は時刻として読めません(HH:mm の形で書いてください)。";
                return false;
            }

            parsed.Add(time);
        }

        var distinct = parsed.Distinct().Order().ToList();
        if (distinct.Count > MaxTimes)
        {
            times = [];
            error = $"時刻は {MaxTimes} 個までです({distinct.Count} 個ありました)。";
            return false;
        }

        times = distinct;
        return true;
    }

    /// <summary>
    /// 前回の実行から今までのあいだに、指定した時刻を跨いだか。
    ///
    /// **跨いだ回数は数えない**(何度止まっていても走るのは1回)—— 止まっていたぶんを
    /// まとめて取り返しても、集まるものは同じで外部を余計に叩くだけ。
    /// </summary>
    public static bool IsDue(
        IReadOnlyList<TimeOnly> times, DateTimeOffset lastRun, DateTimeOffset now) =>
        NextOccurrence(times, lastRun) is { } next && next <= now;

    /// <summary>
    /// <paramref name="after"/> より後で最初に来る実行時刻。時刻が無ければ null。
    /// 画面の「次は 19:00」にも使う。
    /// </summary>
    public static DateTimeOffset? NextOccurrence(
        IReadOnlyList<TimeOnly> times, DateTimeOffset after)
    {
        DateTimeOffset? earliest = null;
        var day = JapanTime.To(after);

        foreach (var time in times)
        {
            // 時刻は日本時間。同じ日で追い越せなければ翌日の同じ時刻
            var candidate = new DateTimeOffset(
                day.Year, day.Month, day.Day, time.Hour, time.Minute, 0, JapanTime.Offset);
            if (candidate <= after)
            {
                candidate = candidate.AddDays(1);
            }

            if (earliest is null || candidate < earliest)
            {
                earliest = candidate;
            }
        }

        return earliest;
    }
}

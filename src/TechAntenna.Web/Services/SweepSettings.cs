using System.Globalization;

namespace TechAntenna.Web.Services;

/// <summary>
/// 面掃き(検索語も名簿も使わず、期間の全件を参加者数で切る経路)のオン/オフ。
///
/// 値は定期実行のチェック・名簿と同じく DB(<c>Secrets</c>)に持つので、
/// 再起動なしで効き、コンテナを作り直しても残る。環境変数では設定できない ——
/// 入口が 2 つあると「どちらが効いているのか」を画面が説明し続けることになる
/// (<see cref="ScheduleSettings"/>・<see cref="FollowSettings"/> と同じ扱い)。
/// appsettings に残っているのは<b>数の設定</b>(参加者数のしきい値・何か月ぶん・待ち時間)
/// だけで、走らせるかどうかは画面が決める。
///
/// <b>既定は無効。</b> 1 回の収集で数十リクエストかかるので、
/// 消し忘れたサーバーが収集先を叩き続けないよう、明示的に入れたときだけ動かす
/// (定期実行の「既定はチェックなし」と同じ考え方)。
/// </summary>
public static class SweepSettings
{
    /// <summary>connpass の面掃きを行うか。</summary>
    public const string ConnpassName = "Events:Sweep:Connpass";

    /// <summary>Doorkeeper の面掃きを行うか。</summary>
    public const string DoorkeeperName = "Events:Sweep:Doorkeeper";

    /// <summary>いま有効か。未設定は無効(保存された "true" のときだけ走る)。</summary>
    public static bool IsEnabled(ApiCredentials credentials, string name) =>
        string.Equals(credentials.Get(name), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>画面の切り替え。オンは値を書き、オフは<b>行ごと消す</b>(既定＝無効に戻す)。</summary>
    public static Task SetAsync(ApiCredentials credentials, string name, bool enabled) =>
        enabled ? credentials.SetAsync(name, "true") : credentials.RemoveAsync(name);

    /// <summary>
    /// 全掃きへ戻す間隔。差分(前回から公開されたぶんだけ)では拾えないものがあるため、
    /// 週に一度は期間の全件を数え直す ——
    /// 参加者数が伸びてしきい値を越えたイベントは、公開日が変わらないので差分に出てこない。
    /// 「50 人だった勉強会が 150 人になった」を拾えるのは全掃きだけ。
    /// </summary>
    public static readonly TimeSpan FullInterval = TimeSpan.FromDays(7);

    /// <summary>
    /// 差分で引く起点(前回掃いた時刻)。null なら全掃き ——
    /// 記録が無い(初回)か、最後の全掃きから <see cref="FullInterval"/> 以上たっているとき。
    /// </summary>
    public static DateTimeOffset? IncrementalSince(
        ApiCredentials credentials, TimeProvider clock, string name)
    {
        if (!DateTimeOffset.TryParse(
                credentials.Get(LastFullName(name)), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var lastFull)
            || clock.GetUtcNow() - lastFull >= FullInterval)
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            credentials.Get(LastRunName(name)), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var lastRun)
            ? lastRun
            : null;
    }

    /// <summary>最後に<b>全掃き</b>した時刻の置き場。</summary>
    public static string LastFullName(string name) => $"{name}:LastFullAt";

    /// <summary>
    /// 掃いた時刻を記録する。全掃きだったかどうかも残す(次に差分で済むかの判断に要る)。
    /// </summary>
    public static async Task MarkRunAsync(
        ApiCredentials credentials, TimeProvider clock, string name, bool full)
    {
        var now = clock.GetUtcNow().ToString("o", CultureInfo.InvariantCulture);
        await credentials.SetAsync(LastRunName(name), now);
        if (full)
        {
            await credentials.SetAsync(LastFullName(name), now);
        }
    }

    /// <summary>
    /// 面掃きを回す間隔。1 日 1 回に絞る。
    ///
    /// 毎回の収集で掃き直す必要が無い。面掃きは「期間の全件を数え上げる」経路なので
    /// 1 回に最大 20 リクエスト(connpass。Doorkeeper は 10)かかる一方、中身は 1 日で
    /// ほとんど変わらない —— 拾いたいのは数か月先まで告知が出ている大型イベントで、
    /// 参加者数もその日のうちに桁は変わらない。定期実行を1日に何度か回す設定にしていると、
    /// そのたびに数十リクエストを投げることになる。
    ///
    /// 初回(前回の記録が無いとき)は必ず回る。入れた直後に何も起きないと、
    /// 動かしたつもりが効いていないのか、まだ時間ではないのかが画面から見分けられない。
    /// </summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromHours(24);

    /// <summary>最後に掃いた時刻の置き場(オン/オフの設定パス + <c>:LastRunAt</c>)。</summary>
    public static string LastRunName(string name) => $"{name}:LastRunAt";

    /// <summary>
    /// いま掃く番か。オン/オフとは別に見る —— 止めているかどうかは
    /// <see cref="IsEnabled"/>、間隔で待つかどうかがこちら。
    /// 読めない値は「まだ掃いていない」として扱う(掃かないより掃くほうが害が小さい)。
    /// </summary>
    public static bool IsDue(ApiCredentials credentials, TimeProvider clock, string name)
    {
        var raw = credentials.Get(LastRunName(name));

        return !DateTimeOffset.TryParse(
                   raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last)
            || clock.GetUtcNow() - last >= MinInterval;
    }


}

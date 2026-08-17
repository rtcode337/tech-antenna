namespace TechAntenna.Web.Services;

/// <summary>
/// 面掃き(検索語も名簿も使わず、期間の全件を参加者数で切る経路)のオン/オフ。
///
/// 値は定期実行のチェック・名簿と同じく DB(<c>Secrets</c>)に持つので、
/// **再起動なしで効き、コンテナを作り直しても残る**。**環境変数では設定できない** ——
/// 入口が 2 つあると「どちらが効いているのか」を画面が説明し続けることになる
/// (<see cref="ScheduleSettings"/>・<see cref="FollowSettings"/> と同じ扱い)。
/// appsettings に残っているのは<b>数の設定</b>(参加者数のしきい値・何か月ぶん・待ち時間)
/// だけで、**走らせるかどうかは画面が決める**。
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

    /// <summary>いま有効か。**未設定は無効**(保存された "true" のときだけ走る)。</summary>
    public static bool IsEnabled(ApiCredentials credentials, string name) =>
        string.Equals(credentials.Get(name), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>画面の切り替え。オンは値を書き、オフは<b>行ごと消す</b>(既定＝無効に戻す)。</summary>
    public static Task SetAsync(ApiCredentials credentials, string name, bool enabled) =>
        enabled ? credentials.SetAsync(name, "true") : credentials.RemoveAsync(name);
}

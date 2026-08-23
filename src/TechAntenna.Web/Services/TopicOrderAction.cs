namespace TechAntenna.Web.Services;

/// <summary>
/// 興味トピックを 1 つ分だけ上下へ動かすボタンが submit する値
/// (<c>up:&lt;キー&gt;</c> / <c>down:&lt;キー&gt;</c>)の組み立てと読み取り。
///
/// 書式を 1 か所に置くためだけのもの(<see cref="BookReadAction"/> と同じ流儀)——
/// 描くのと受けるのが離れていて、どちらかだけ直すと押しても何も起きない形で静かに壊れる。
///
/// <b>これはドラッグが効かないときの経路</b>(JS が動く環境ではボタンごと隠す)。
/// キーボードだけで操作するときもここを通るので、消さないこと。
/// </summary>
public static class TopicOrderAction
{
    const string Up = "up:";
    const string Down = "down:";

    /// <summary>上へ動かすボタンの value。</summary>
    public static string UpValue(string key) => Up + key;

    /// <summary>下へ動かすボタンの value。</summary>
    public static string DownValue(string key) => Down + key;

    /// <summary>
    /// 押されたボタンの value を読む。<paramref name="delta"/> は上が -1、下が +1。
    /// 読めなければ false(同じフォームに別の submit が増えても、ここで弾かれるだけで済む)。
    /// </summary>
    public static bool TryRead(string? action, out string key, out int delta)
    {
        key = "";
        delta = 0;

        if (action is not { Length: > 0 })
        {
            return false;
        }

        if (action.StartsWith(Up, StringComparison.Ordinal))
        {
            (key, delta) = (action[Up.Length..], -1);
        }
        else if (action.StartsWith(Down, StringComparison.Ordinal))
        {
            (key, delta) = (action[Down.Length..], 1);
        }

        if (key.Length > 0)
        {
            return true;
        }

        // **読めなかったら向きも戻す。** `up:` だけの値で -1 が残ると、
        // 戻り値を見ない呼び出しが「上へ動かす」を掴んでしまう
        delta = 0;

        return false;
    }
}

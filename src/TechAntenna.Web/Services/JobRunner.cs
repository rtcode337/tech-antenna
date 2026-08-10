namespace TechAntenna.Web.Services;

/// <summary>
/// 定期実行(<c>BackgroundService</c>)と画面の手動ボタンの両方から呼ばれるジョブ。
///
/// **同時に走らないよう直列化する** —— 二重に走ると同じ収集先へ続けて叩きに行ったり、
/// 同じ記事を二度要約して LLM の枠を無駄に使うことになる。
/// </summary>
public abstract class JobRunner
{
    readonly SemaphoreSlim _gate = new(1, 1);

    volatile bool _starting;
    volatile bool _running;

    /// <summary>ジョブの名前(画面とログに出す)。</summary>
    public abstract string Name { get; }

    /// <summary>実行できる状態か。収集元やキーが未設定なら false(画面のボタンが disabled になる)。</summary>
    public abstract bool IsConfigured { get; }

    /// <summary>
    /// 未設定(<see cref="IsConfigured"/> が false)のとき、ボタンの隣に出す理由。
    /// 何を設定すれば使えるようになるかを書く(JobButton が外部連携への導線を添える)。
    /// </summary>
    public virtual string? NotConfiguredReason => null;

    /// <summary>今まさに実行中か(バックグラウンド開始の直後も含む)。</summary>
    public bool IsRunning => _running || _starting;

    /// <summary>
    /// 実行中の進捗(画面に出す短い文)。長いジョブの中から適宜更新する。
    /// 実行していないときは null。
    /// </summary>
    public string? Progress { get; protected set; }

    /// <summary>直近の実行の結果の文言。画面が POST をまたいで結果を出すためにここへ持つ。</summary>
    public string? LastMessage { get; private set; }

    /// <summary>直近の実行が失敗したときの理由。成功したら null に戻る。</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// ジョブをバックグラウンドで1回実行する(既に実行中なら何もしない)。
    /// 中身は <see cref="RunAndRecordAsync"/> と同じものを渡す(結果の文言もそちらが残す)。
    ///
    /// **画面のボタンはこちらを使う。** 全ページ静的 SSR なので、応答を返し切るまで
    /// 画面は何も出ない —— 数分かかるジョブを await すると、押した人はただ白い画面を
    /// 待たされる。開始だけして応答を返し、進捗は自動リロード(JobButton の
    /// meta refresh)で見せる。
    /// </summary>
    public void StartInBackground(Func<CancellationToken, Task<bool>> run)
    {
        if (!IsConfigured || IsRunning)
        {
            return;
        }

        // 応答を返す時点で「実行中」に見せる(Task が走り出す前に画面が描画されても
        // meta refresh が付くように、開始フラグは同期的に立てる)
        _starting = true;

        _ = Task.Run(async () =>
        {
            try
            {
                await run(CancellationToken.None);
            }
            finally
            {
                _starting = false;
            }
        });
    }

    /// <summary>
    /// ジョブを1回実行して、結果の文言を <see cref="LastMessage"/> / <see cref="LastError"/> に残す。
    ///
    /// **定期実行から使う**(<see cref="StartInBackground"/> の await する版)——
    /// 定期実行は決まった順で通しで走らせるので、次のジョブへ進む前に終わりを待つ必要がある。
    /// 失敗しても投げ返さず false を返す —— 1つのジョブの失敗で残りを止めないため。
    /// 画面には手動で押したときと同じ文言が残る。
    /// </summary>
    public async Task<bool> RunAndRecordAsync<T>(
        Func<CancellationToken, Task<T>> run,
        Func<T, string> describe,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return false;
        }

        LastError = null;
        try
        {
            LastMessage = describe(await run(cancellationToken));
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 実行中でなければ <paramref name="body"/> を走らせる。既に走っていれば
    /// <paramref name="whenBusy"/> を返して何もしない。
    /// </summary>
    protected async Task<T> RunExclusiveAsync<T>(
        Func<Task<T>> body, T whenBusy, CancellationToken cancellationToken)
    {
        if (!IsConfigured || !await _gate.WaitAsync(0, cancellationToken))
        {
            return whenBusy;
        }

        _running = true;
        try
        {
            return await body();
        }
        finally
        {
            _running = false;
            Progress = null;
            _gate.Release();
        }
    }
}

/// <summary>収集を1巡だけ実行した結果。</summary>
/// <param name="Fetched">収集元から取得した件数。</param>
/// <param name="Added">そのうち新規に追加した件数。</param>
/// <param name="FailedSources">失敗した収集元の数。</param>
public record CollectionRunResult(int Fetched, int Added, int FailedSources)
{
    public static readonly CollectionRunResult Nothing = new(0, 0, 0);
}

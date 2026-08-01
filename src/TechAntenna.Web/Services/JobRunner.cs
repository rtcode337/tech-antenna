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

    /// <summary>ジョブの名前(画面とログに出す)。</summary>
    public abstract string Name { get; }

    /// <summary>実行できる状態か。収集元やキーが未設定なら false(画面にボタンを出さない)。</summary>
    public abstract bool IsConfigured { get; }

    /// <summary>今まさに実行中か。</summary>
    public bool IsRunning { get; private set; }

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

        IsRunning = true;
        try
        {
            return await body();
        }
        finally
        {
            IsRunning = false;
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

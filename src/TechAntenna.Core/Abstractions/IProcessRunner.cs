namespace TechAntenna.Core.Abstractions;

/// <summary>外部プロセスの実行結果。</summary>
/// <param name="ExitCode">終了コード。<paramref name="TimedOut"/> が true のときは意味を持たない。</param>
/// <param name="StandardOutput">標準出力の全内容。</param>
/// <param name="StandardError">標準エラーの全内容。</param>
/// <param name="TimedOut">制限時間を超えて打ち切ったか。</param>
public record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);

/// <summary>
/// 外部プロセスを実行する。テストで差し替えられるよう抽象にしている
/// (要約を Claude Code の CLI に投げる実装が使う)。
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// プロセスを起動し、<paramref name="standardInput"/> を標準入力へ流し込んで終了を待つ。
    ///
    /// **入力を引数ではなく標準入力で渡す**のは、Linux に単一引数の長さ上限
    /// (MAX_ARG_STRLEN = 128KiB)があり、記事をまとめると容易に超えて
    /// 実行前に E2BIG で落ちるため。
    /// </summary>
    Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

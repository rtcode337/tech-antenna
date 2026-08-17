using System.Diagnostics;
using System.Text;
using TechAntenna.Core.Abstractions;

namespace TechAntenna.Infrastructure;

/// <summary><see cref="Process"/> で外部プロセスを実行する既定の実装。</summary>
/// <param name="environmentProvider">
/// 子プロセスに上書きで渡す環境変数(起動のたびに評価する)。Claude Code の CLI は
/// <c>CLAUDE_CODE_OAUTH_TOKEN</c> を環境変数から読むので、画面で設定したトークンは
/// ここを通して子プロセスへ渡す(アプリ自身の環境変数は変えない)。
/// </param>
public class SystemProcessRunner(
    Func<IReadOnlyDictionary<string, string>?>? environmentProvider = null) : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            // ArgumentList を使うとシェルを介さずに渡るので、引数のクオートを自前で組まなくてよい
            startInfo.ArgumentList.Add(argument);
        }
        var environment = environmentProvider?.Invoke();
        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"{fileName} を起動できなかった。");

        // 制限時間を過ぎたら殺す。外部プロセスがハングしたときに巡回ごとプロセスが
        // 積み上がるのを防ぐ
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        // 出力を読み切る前に待つとパイプのバッファが埋まって相互に止まるため、先に読み始める
        var stdout = process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeoutSource.Token);

        try
        {
            await process.StandardInput.WriteAsync(
                standardInput.AsMemory(), timeoutSource.Token);
            process.StandardInput.Close();

            await process.WaitForExitAsync(timeoutSource.Token);
            return new ProcessResult(process.ExitCode, await stdout, await stderr, TimedOut: false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 呼び出し側のキャンセルではなく、こちらの制限時間切れ
            Kill(process);
            return new ProcessResult(-1, "", "", TimedOut: true);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
    }

    static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                // 子プロセスごと落とす(claude はさらに別プロセスを持つことがある)
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // 既に終了していた
        }
    }
}

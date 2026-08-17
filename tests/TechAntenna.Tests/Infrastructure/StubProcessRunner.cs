using TechAntenna.Core.Abstractions;

namespace TechAntenna.Tests.Infrastructure;

/// <summary>決まった結果を返す IProcessRunner。実際に claude を起動せず動作を確かめるために使う。</summary>
public class StubProcessRunner(ProcessResult result) : IProcessRunner
{
    /// <summary>渡された引数。組み立てを確認するために記録する。</summary>
    public List<string> Arguments { get; } = [];

    /// <summary>標準入力に流された内容。</summary>
    public string StandardInput { get; private set; } = "";

    public int CallCount { get; private set; }

    public static StubProcessRunner Returning(string standardOutput) =>
        new(new ProcessResult(0, standardOutput, "", TimedOut: false));

    public Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        Arguments.Clear();
        Arguments.AddRange(arguments);
        StandardInput = standardInput;
        return Task.FromResult(result);
    }
}

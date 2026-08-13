using TechAntenna.Core.Abstractions;

namespace TechAntenna.Tests.Infrastructure;

/// <summary>
/// 決まった応答を返す <see cref="ICliBridge"/>。実際にブリッジ(と CLI)を動かさずに
/// 呼び出し側の組み立てと読み取りを確かめるために使う。
/// </summary>
public class StubCliBridge(string response, Exception? failure = null) : ICliBridge
{
    public string Name => "Claude Code / test";

    /// <summary>渡されたシステムプロンプト(スキーマの指示を確認するために記録する)。</summary>
    public string SystemPrompt { get; private set; } = "";

    /// <summary>渡された本文。</summary>
    public string UserPrompt { get; private set; } = "";

    public int CallCount { get; private set; }

    public static StubCliBridge Failing(Exception failure) => new("", failure);

    public Task<string> RunAsync(
        string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        CallCount++;
        SystemPrompt = systemPrompt;
        UserPrompt = userPrompt;

        return failure is not null
            ? Task.FromException<string>(failure)
            : Task.FromResult(response);
    }
}

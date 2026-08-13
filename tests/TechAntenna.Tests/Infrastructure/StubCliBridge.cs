using TechAntenna.Core.Abstractions;

namespace TechAntenna.Tests.Infrastructure;

/// <summary>
/// 決まった応答を返す <see cref="ICliBridge"/>。実際にブリッジ(と CLI)を動かさずに
/// 呼び出し側の組み立てと読み取りを確かめるために使う。
/// </summary>
public class StubCliBridge : ICliBridge
{
    readonly IReadOnlyList<string> _responses;
    readonly Exception? _failure;

    /// <summary>毎回同じ応答を返す。</summary>
    public StubCliBridge(string response, Exception? failure = null)
    {
        _responses = [response];
        _failure = failure;
    }

    /// <summary>呼ばれた順に応答を返す(最後の1つはそれ以降も使い回す)。言い直しの確認用。</summary>
    public static StubCliBridge Sequence(params string[] responses) => new(responses);

    StubCliBridge(IReadOnlyList<string> responses)
    {
        _responses = responses;
        _failure = null;
    }

    public string Name => "Claude Code / test";

    /// <summary>渡されたシステムプロンプト(スキーマの指示を確認するために記録する)。</summary>
    public string SystemPrompt { get; private set; } = "";

    /// <summary>渡された本文。</summary>
    public string UserPrompt { get; private set; } = "";

    /// <summary>渡されたシステムプロンプトの全履歴(言い直しの文面を確認するために持つ)。</summary>
    public List<string> SystemPrompts { get; } = [];

    public int CallCount { get; private set; }

    public static StubCliBridge Failing(Exception failure) => new("", failure);

    public Task<string> RunAsync(
        string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        CallCount++;
        SystemPrompt = systemPrompt;
        SystemPrompts.Add(systemPrompt);
        UserPrompt = userPrompt;

        return _failure is not null
            ? Task.FromException<string>(_failure)
            : Task.FromResult(_responses[Math.Min(CallCount - 1, _responses.Count - 1)]);
    }
}

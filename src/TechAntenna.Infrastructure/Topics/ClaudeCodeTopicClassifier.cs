using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Summarization;

namespace TechAntenna.Infrastructure.Topics;

/// <summary>
/// Claude Code のヘッドレス実行(<c>claude -p</c>)で未知タグを分類する。
/// 呼び出し1回の固定費が大きいので、**未知タグは必ず1回にまとめて渡す**
/// (呼び出しの作法は要約と同じ <see cref="ClaudeCodeBatch"/> に集約してある)。
/// </summary>
public class ClaudeCodeTopicClassifier(
    IProcessRunner processRunner,
    string executablePath,
    string? model,
    TimeSpan timeout) : ITopicClassifier
{
    public string Name => "Claude Code";

    public async Task<IReadOnlyList<TopicClassifierVerdict>> ClassifyAsync(
        IReadOnlyList<string> tags,
        IReadOnlyList<TopicCatalogEntry> existingTopics,
        CancellationToken cancellationToken = default)
    {
        if (tags.Count == 0)
        {
            return [];
        }

        var stdout = await ClaudeCodeBatch.RunRawAsync(
            processRunner,
            executablePath,
            model,
            timeout,
            TopicClassificationPrompt.System,
            TopicClassificationPrompt.Schema,
            TopicClassificationPrompt.ForTags(tags, existingTopics),
            cancellationToken);

        return ClaudeCodeResponseParser.ReadStructuredOutput(
            stdout, TopicClassificationPrompt.ReadVerdicts);
    }
}

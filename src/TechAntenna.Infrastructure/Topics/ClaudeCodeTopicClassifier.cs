using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Infrastructure.Summarization;

namespace TechAntenna.Infrastructure.Topics;

/// <summary>
/// Claude Code のヘッドレス実行(<c>claude -p</c>)で未知タグを分類する。
/// 呼び出し1回の固定費が大きいのでまとめて渡すが、**1回に詰めすぎない** ——
/// 200 語を1回で渡したら応答(全語ぶんの構造化 JSON)の生成が長くなり、
/// 300 秒のタイムアウトで丸ごと失敗した(実測)。バッチに分ければ、
/// 途中で失敗してもそれまでのバッチの分類は生きる。
/// (呼び出しの作法は要約と同じ <see cref="ClaudeCodeBatch"/> に集約してある)
/// </summary>
public class ClaudeCodeTopicClassifier(
    IProcessRunner processRunner,
    string executablePath,
    string? model,
    TimeSpan timeout) : ITopicClassifier
{
    /// <summary>1回の呼び出しで渡す語数。固定費(1回3万トークン規模)と応答時間の折り合い。</summary>
    public const int BatchSize = 60;

    public string Name => "Claude Code";

    public async Task<IReadOnlyList<TopicClassifierVerdict>> ClassifyAsync(
        IReadOnlyList<string> tags,
        IReadOnlyList<TopicCatalogEntry> existingTopics,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var verdicts = new List<TopicClassifierVerdict>();
        var totalBatches = (tags.Count + BatchSize - 1) / BatchSize;

        for (var offset = 0; offset < tags.Count; offset += BatchSize)
        {
            var batch = tags.Skip(offset).Take(BatchSize).ToList();
            progress?.Invoke($"バッチ {offset / BatchSize + 1}/{totalBatches}({batch.Count} 語)を分類中");
            string stdout;
            try
            {
                stdout = await ClaudeCodeBatch.RunRawAsync(
                    processRunner,
                    executablePath,
                    model,
                    timeout,
                    TopicClassificationPrompt.System,
                    TopicClassificationPrompt.Schema,
                    TopicClassificationPrompt.ForTags(batch, existingTopics),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch when (verdicts.Count > 0)
            {
                // 後続バッチの失敗で全部を捨てない。ここまでの分類は返して保存させ、
                // 残りの語は未分類のまま次の収集でもう一度試す(最初のバッチから失敗した
                // ときは投げ直して、呼び出し側にエラーとして記録させる)
                break;
            }

            // 応答の番号はバッチ内の 1 始まりなので、全体の番号へずらして返す
            var batchOffset = offset;
            verdicts.AddRange(ClaudeCodeResponseParser
                .ReadStructuredOutput(stdout, TopicClassificationPrompt.ReadVerdicts)
                .Select(verdict => verdict with { Index = verdict.Index + batchOffset }));
        }

        return verdicts;
    }
}

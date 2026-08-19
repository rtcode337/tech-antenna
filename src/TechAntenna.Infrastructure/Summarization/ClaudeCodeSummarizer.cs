using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Summarization;

/// <summary>
/// Claude Code(同梱の CLI をプロセス起動)で要約を生成する。API の従量課金ではなく
/// サブスクリプションの枠で動かすための実装。認証はブリッジが共有ディレクトリの設定 DB から
/// 読むので、このクラスはトークンを扱わない。
///
/// 呼び出し1回の固定費が大きい。記事1件だけ渡しても Claude Code のハーネスが
/// 3万トークン規模で入力に乗るため、必ずバッチでまとめて渡すこと(実測: 1件だと 32,300 tok、
/// 5件まとめると 1件あたり 6,584 tok)。呼び出しの作法は <see cref="ClaudeCodeBatch"/> に集約。
/// </summary>
public class ClaudeCodeSummarizer(ICliBridge bridge) : ISummarizer
{
    public string Name => bridge.Name;

    public async Task<IReadOnlyList<SummaryResult>> SummarizeAsync(
        IReadOnlyList<Article> articles,
        CancellationToken cancellationToken = default)
    {
        // 材料の無い記事は呼び出しに含めず、空の要約として確定させる
        var targets = articles.Where(SummaryPrompt.CanSummarize).ToList();
        var results = articles
            .Where(a => !SummaryPrompt.CanSummarize(a))
            .Select(a => new SummaryResult(a.Id, null))
            .ToList();

        if (targets.Count == 0)
        {
            return results;
        }

        var entries = await ClaudeCodeBatch.RunAsync(
            bridge,
            SummaryPrompt.System,
            "summaries",
            "summary",
            SummaryPrompt.ForArticles(targets),
            cancellationToken);

        foreach (var entry in entries)
        {
            // 番号は 1 始まり。範囲外は応答の取り違えなので捨てる(誤った記事に紐づけない)
            if (entry.Index >= 1 && entry.Index <= targets.Count)
            {
                results.Add(new SummaryResult(targets[entry.Index - 1].Id, entry.Text));
            }
        }

        return results;
    }
}

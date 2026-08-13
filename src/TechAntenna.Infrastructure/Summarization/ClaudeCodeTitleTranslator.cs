using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Summarization;

/// <summary>
/// Claude Code(CLI ブリッジ経由)でタイトルを訳す。
/// 要約と同じくサブスクリプションの枠で動き、呼び出しの作法は <see cref="ClaudeCodeBatch"/> に集約。
///
/// **タイトルは1件あたりの入力が極端に小さい**ので、要約以上にまとめて渡す価値がある
/// (呼び出し1回の固定費が3万トークン規模なのに、タイトルは数十トークンしかない)。
/// </summary>
public class ClaudeCodeTitleTranslator(ICliBridge bridge) : ITitleTranslator
{
    public string Name => bridge.Name;

    public async Task<IReadOnlyList<TitleTranslation>> TranslateAsync(
        IReadOnlyList<Article> articles,
        CancellationToken cancellationToken = default)
    {
        // 日本語のタイトルは呼び出しに含めず、訳さないものとして確定させる
        var targets = articles.Where(TitleTranslationPrompt.NeedsTranslation).ToList();
        var results = articles
            .Where(a => !TitleTranslationPrompt.NeedsTranslation(a))
            .Select(a => new TitleTranslation(a.Id, null))
            .ToList();

        if (targets.Count == 0)
        {
            return results;
        }

        var entries = await ClaudeCodeBatch.RunAsync(
            bridge,
            TitleTranslationPrompt.System,
            "titles",
            "title_ja",
            TitleTranslationPrompt.ForArticles(targets),
            cancellationToken);

        foreach (var entry in entries)
        {
            // 番号は 1 始まり。範囲外は応答の取り違えなので捨てる(誤った記事に紐づけない)
            if (entry.Index >= 1 && entry.Index <= targets.Count)
            {
                results.Add(new TitleTranslation(targets[entry.Index - 1].Id, entry.Text));
            }
        }

        return results;
    }
}

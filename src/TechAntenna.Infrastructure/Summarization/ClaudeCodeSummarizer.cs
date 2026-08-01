using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Summarization;

/// <summary>
/// Claude Code のヘッドレス実行(<c>claude -p</c>)で要約を生成する。API の従量課金ではなく
/// サブスクリプションの枠で動かすための実装。認証は CLI 自身が環境変数
/// <c>CLAUDE_CODE_OAUTH_TOKEN</c> を読むので、このクラスはトークンを扱わない
/// (子プロセスは親の環境変数を引き継ぐ)。
///
/// **呼び出し1回の固定費が大きい**。記事1件だけ渡しても Claude Code のハーネスが
/// 3万トークン規模で入力に乗るため、必ずバッチでまとめて渡すこと(実測: 1件だと 32,300 tok、
/// 5件まとめると 1件あたり 6,584 tok)。
/// </summary>
public class ClaudeCodeSummarizer(
    IProcessRunner processRunner,
    string executablePath,
    string? model,
    TimeSpan timeout) : ISummarizer
{
    /// <summary>
    /// 応答の形を固定する JSON Schema。番号と要約の対応で返させ、記事との紐づけを確実にする。
    /// </summary>
    const string OutputSchema = """
        {"type":"object","properties":{"summaries":{"type":"array","items":{"type":"object",
        "properties":{"index":{"type":"integer"},"summary":{"type":"string"}},
        "required":["index","summary"]}}},"required":["summaries"]}
        """;

    /// <summary>
    /// 要約にツールは要らないので全部禁じる。許すと1ターンをツール呼び出しに使って
    /// 要約が返らないことがある。
    /// </summary>
    const string DisallowedTools =
        "Bash,Read,Edit,Write,Glob,Grep,WebSearch,WebFetch,Task,TodoWrite," +
        "NotebookEdit,BashOutput,KillShell,SlashCommand,ExitPlanMode";

    public string Name => "Claude Code";

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

        var arguments = new List<string>
        {
            "-p",
            "--max-turns", "1",
            "--system-prompt", SummaryPrompt.System,
            "--output-format", "json",
            "--json-schema", OutputSchema.ReplaceLineEndings(""),
            "--disallowed-tools", DisallowedTools,
        };
        if (!string.IsNullOrWhiteSpace(model))
        {
            arguments.Add("--model");
            arguments.Add(model);
        }

        var process = await processRunner.RunAsync(
            executablePath,
            arguments,
            SummaryPrompt.ForArticles(targets),
            timeout,
            cancellationToken);

        if (process.TimedOut)
        {
            throw new TimeoutException($"claude が {timeout.TotalSeconds:0} 秒で終わらなかった。");
        }

        if (process.ExitCode != 0)
        {
            // claude は失敗の詳細を stderr ではなく stdout の JSON に書く。stderr は空になりがち
            var detail = ClaudeCodeResponseParser.DescribeError(process.StandardOutput)
                ?? Excerpt(process.StandardError);
            throw new InvalidOperationException(
                $"claude が終了コード {process.ExitCode} で失敗した: {Excerpt(detail)}");
        }

        foreach (var entry in ClaudeCodeResponseParser.Parse(process.StandardOutput))
        {
            // 番号は 1 始まり。範囲外は応答の取り違えなので捨てる(誤った記事に紐づけない)
            if (entry.Index >= 1 && entry.Index <= targets.Count)
            {
                results.Add(new SummaryResult(targets[entry.Index - 1].Id, entry.Summary));
            }
        }

        return results;
    }

    /// <summary>例外メッセージにそのまま載せられる長さに切る。</summary>
    static string Excerpt(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500] + "…";
    }
}

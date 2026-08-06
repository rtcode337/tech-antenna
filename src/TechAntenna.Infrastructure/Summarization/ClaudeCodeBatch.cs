using TechAntenna.Core.Abstractions;

namespace TechAntenna.Infrastructure.Summarization;

/// <summary>
/// `claude -p` を1回呼んで、番号付きの結果を受け取る。要約とタイトル翻訳で共有する。
///
/// **ここに集めてあるのは、踏まないと分からない作法**:
/// - プロンプトは引数ではなく**標準入力**で渡す。Linux の単一引数の長さ上限
///   (MAX_ARG_STRLEN = 128KiB)を、記事をまとめると容易に超えて E2BIG で落ちる
/// - `--json-schema` で番号と値の対応を返させる(記事の Id を LLM に写させない)
/// - **失敗の詳細は stderr ではなく stdout の JSON** に入る(`result`/`api_error_status`)
/// - `--bare` は使えない。keychain と OAuth の読み取りを飛ばすため
///   `CLAUDE_CODE_OAUTH_TOKEN` が効かなくなる
/// </summary>
public static class ClaudeCodeBatch
{
    /// <summary>
    /// ツールは全部禁じる。許すと1ターンをツール呼び出しに使って結果が返らないことがある。
    /// </summary>
    const string DisallowedTools =
        "Bash,Read,Edit,Write,Glob,Grep,WebSearch,WebFetch,Task,TodoWrite," +
        "NotebookEdit,BashOutput,KillShell,SlashCommand,ExitPlanMode";

    /// <summary>番号と値の配列を返させる JSON Schema を組み立てる。</summary>
    // 波括弧だらけなので、生文字列の補間ではなく素直に連結する
    public static string Schema(string arrayName, string valueName) =>
        "{\"type\":\"object\",\"properties\":{\"" + arrayName
        + "\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":"
        + "{\"index\":{\"type\":\"integer\"},\"" + valueName + "\":{\"type\":\"string\"}},"
        + "\"required\":[\"index\",\"" + valueName + "\"]}}},\"required\":[\"" + arrayName + "\"]}";

    public static async Task<IReadOnlyList<ClaudeCodeResponseParser.Entry>> RunAsync(
        IProcessRunner processRunner,
        string executablePath,
        string? model,
        TimeSpan timeout,
        string systemPrompt,
        string arrayName,
        string valueName,
        string input,
        CancellationToken cancellationToken)
    {
        var stdout = await RunRawAsync(
            processRunner, executablePath, model, timeout,
            systemPrompt, Schema(arrayName, valueName), input, cancellationToken);

        return ClaudeCodeResponseParser.Parse(stdout, arrayName, valueName);
    }

    /// <summary>
    /// `claude -p` を1回呼んで stdout(応答 JSON)をそのまま返す。
    /// 番号+文字列の形に収まらないスキーマ(トピック分類など)はこちらを使い、
    /// 呼び出し側で <see cref="ClaudeCodeResponseParser.ReadStructuredOutput{T}"/> する。
    /// </summary>
    public static async Task<string> RunRawAsync(
        IProcessRunner processRunner,
        string executablePath,
        string? model,
        TimeSpan timeout,
        string systemPrompt,
        string schemaJson,
        string input,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "-p",
            "--max-turns", "1",
            "--system-prompt", systemPrompt,
            "--output-format", "json",
            "--json-schema", schemaJson,
            "--disallowed-tools", DisallowedTools,
        };
        if (!string.IsNullOrWhiteSpace(model))
        {
            arguments.Add("--model");
            arguments.Add(model);
        }

        var process = await processRunner.RunAsync(
            executablePath, arguments, input, timeout, cancellationToken);

        if (process.TimedOut)
        {
            throw new TimeoutException($"claude が {timeout.TotalSeconds:0} 秒で終わらなかった。");
        }

        if (process.ExitCode != 0)
        {
            var detail = ClaudeCodeResponseParser.DescribeError(process.StandardOutput)
                ?? process.StandardError;
            throw new InvalidOperationException(
                $"claude が終了コード {process.ExitCode} で失敗した: {Excerpt(detail)}");
        }

        return process.StandardOutput;
    }

    /// <summary>例外メッセージにそのまま載せられる長さに切る。</summary>
    static string Excerpt(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500] + "…";
    }
}

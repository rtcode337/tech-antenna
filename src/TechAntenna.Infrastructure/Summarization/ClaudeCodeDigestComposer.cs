using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Summarization;

/// <summary>
/// Claude Code のヘッドレス実行(<c>claude -p</c>)でダイジェストを書く。
/// 呼び出しの作法(標準入力・--json-schema・エラーの読み方)は <see cref="ClaudeCodeBatch"/>。
/// ダイジェストは1回の生成が1呼び出しなので、要約と違いバッチにまとめる相手がいない ——
/// 固定費はかかるが、実行は1日2回程度なので許容している。
/// </summary>
public class ClaudeCodeDigestComposer(
    IProcessRunner processRunner,
    string executablePath,
    string? model,
    TimeSpan timeout,
    TimeProvider clock) : IDigestComposer
{
    // モデル名まで画面に出す —— どのモデルがサブスク枠を使っているか見えるようにする。
    // model が null(CLI の既定に任せる)ときは、既定が何かこちらから分からないので付けない
    public string Name => model is null ? "Claude Code" : $"Claude Code / {model}";

    public async Task<Digest> ComposeAsync(
        DigestMaterials materials, CancellationToken cancellationToken = default)
    {
        var stdout = await ClaudeCodeBatch.RunRawAsync(
            processRunner,
            executablePath,
            model,
            timeout,
            DigestPrompt.SystemFor(materials.Scope),
            DigestPrompt.Schema,
            DigestPrompt.ForMaterials(materials),
            cancellationToken);

        return ClaudeCodeResponseParser.ReadStructuredOutput(
            stdout,
            output => DigestPrompt.Read(output, materials, Name, clock.GetUtcNow()));
    }
}

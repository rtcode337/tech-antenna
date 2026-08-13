using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Summarization;

/// <summary>
/// Claude Code(CLI ブリッジ経由)でダイジェストを書く。
/// 呼び出しの作法(スキーマの渡し方・応答の読み方)は <see cref="ClaudeCodeBatch"/>。
/// ダイジェストは1回の生成が1呼び出しなので、要約と違いバッチにまとめる相手がいない ——
/// 固定費はかかるが、実行は1日2回程度なので許容している。
/// </summary>
public class ClaudeCodeDigestComposer(ICliBridge bridge, TimeProvider clock) : IDigestComposer
{
    public string Name => bridge.Name;

    public async Task<Digest> ComposeAsync(
        DigestMaterials materials, CancellationToken cancellationToken = default)
    {
        var text = await ClaudeCodeBatch.RunRawAsync(
            bridge,
            DigestPrompt.SystemFor(materials.Scope),
            DigestPrompt.Schema,
            DigestPrompt.ForMaterials(materials),
            cancellationToken);

        return ClaudeCodeResponseParser.ReadJson(
            text,
            output => DigestPrompt.Read(output, materials, Name, clock.GetUtcNow()));
    }
}

using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>記事の要約生成(LLM 等)。</summary>
public interface ISummarizer
{
    /// <summary>記事の日本語要約を生成する。材料不足などで生成できない場合は null を返す。</summary>
    Task<string?> SummarizeAsync(Article article, CancellationToken cancellationToken = default);
}

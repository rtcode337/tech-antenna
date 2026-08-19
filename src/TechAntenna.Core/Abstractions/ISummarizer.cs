using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>1記事分の要約結果。</summary>
/// <param name="ArticleId">対象の記事。</param>
/// <param name="Summary">要約。材料不足などで生成できなかった場合は null(空の要約として確定させる)。</param>
public record SummaryResult(Guid ArticleId, string? Summary);

/// <summary>記事の要約生成(LLM 等)。</summary>
public interface ISummarizer
{
    /// <summary>実装の名前(ログに出す)。</summary>
    string Name { get; }

    /// <summary>
    /// 渡された記事をまとめて要約する。
    ///
    /// 1件ずつではなくバッチで渡すのは、呼び出し1回あたりの固定費が大きい実装があるため
    /// (記事1件を Claude Code のヘッドレス実行に投げると、記事本文とは別に3万トークン規模の
    /// ハーネスが毎回入力に乗る)。まとめて渡せばその固定費が薄まる。
    ///
    /// 結果に含めなかった記事は未処理として扱われ、次回の巡回で再試行される。
    /// </summary>
    Task<IReadOnlyList<SummaryResult>> SummarizeAsync(
        IReadOnlyList<Article> articles,
        CancellationToken cancellationToken = default);
}

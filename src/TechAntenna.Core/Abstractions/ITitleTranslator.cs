using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>1件分の訳題。</summary>
/// <param name="ArticleId">対象。</param>
/// <param name="TitleJa">日本語の訳題。訳す必要が無い・訳せなかった場合は null。</param>
public record TitleTranslation(Guid ArticleId, string? TitleJa);

/// <summary>
/// 英語のタイトルを日本語に訳す(LLM 等)。
///
/// 原題は消さない。訳題は別に持って併記する —— 原題が無いと、検索やほかの文献との
/// 突き合わせができなくなる。要約と同じく、呼び出し1回の固定費が大きい実装があるので
/// バッチで渡す。
/// </summary>
public interface ITitleTranslator
{
    /// <summary>実装の名前(ログに出す)。</summary>
    string Name { get; }

    /// <summary>渡された記事のタイトルをまとめて訳す。結果に含めなかった記事は次回に再試行される。</summary>
    Task<IReadOnlyList<TitleTranslation>> TranslateAsync(
        IReadOnlyList<Article> articles,
        CancellationToken cancellationToken = default);
}

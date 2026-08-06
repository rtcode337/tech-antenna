using TechAntenna.Core.Topics;

namespace TechAntenna.Core.Abstractions;

/// <summary>
/// LLM が返した分類1件(検証前の生の値)。<c>Index</c> は入力で振った 1 始まりの番号
/// (タグをそのまま写させると表記を崩す余地ができるため、番号で対応づける)。
/// <c>Kind</c> は alias / new / skip。alias なら <c>Target</c> に寄せ先、
/// new なら <c>Display</c> に正式表記と <c>Target</c> に親(無ければ null)。
/// </summary>
public record TopicClassifierVerdict(int Index, string Kind, string? Target, string? Display);

/// <summary>
/// カタログに無いタグを、既存トピックの同義語・新トピック(親付き)・トピック外に
/// 振り分ける分類器。実装は LLM(Claude Code ヘッドレス / Anthropic API)。
/// 応答は <see cref="Topics.TopicClassificationValidator"/> で検証してから使うこと ——
/// 存在しない寄せ先や自己参照の親をそのまま信じるとツリーが壊れる。
/// </summary>
public interface ITopicClassifier
{
    string Name { get; }

    /// <summary>未知タグをまとめて分類する。<paramref name="existingTopics"/> は判断材料の既存ツリー。</summary>
    Task<IReadOnlyList<TopicClassifierVerdict>> ClassifyAsync(
        IReadOnlyList<string> tags,
        IReadOnlyList<TopicCatalogEntry> existingTopics,
        CancellationToken cancellationToken = default);
}

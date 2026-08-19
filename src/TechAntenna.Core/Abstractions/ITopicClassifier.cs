using TechAntenna.Core.Topics;

namespace TechAntenna.Core.Abstractions;

/// <summary>
/// LLM が返した分類1件(検証前の生の値)。<c>Index</c> は入力で振った 1 始まりの番号
/// (タグをそのまま写させると表記を崩す余地ができるため、番号で対応づける)。
/// <c>Kind</c> は alias / new / skip。alias なら <c>Target</c> に寄せ先、
/// new なら <c>Display</c> に正式表記と <c>Target</c> に親(無ければ null)。
/// <c>Description</c> は new のときの一言説明(分類の応答に相乗りさせている ——
/// 説明のために呼び出しを増やさないため。知らない語では空で返る)。
/// </summary>
/// <c>English</c> は new のときの英語表記(arXiv のような英語の収集元へ投げる検索語。
/// 日本語のまま投げると 0 件になるため、分類の応答で一緒に受け取る)。
public record TopicClassifierVerdict(
    int Index,
    string Kind,
    string? Target,
    string? Display,
    string? Description = null,
    string? English = null);

/// <summary>
/// カタログに無いタグを、既存トピックの同義語・新トピック(親付き)・除外(トピックにしない)に
/// 振り分ける分類器。実装は LLM(Claude Code ヘッドレス / Anthropic API)。
/// 応答は <see cref="Topics.TopicClassificationValidator"/> で検証してから使うこと ——
/// 存在しない寄せ先や自己参照の親をそのまま信じるとツリーが壊れる。
/// </summary>
public interface ITopicClassifier
{
    string Name { get; }

    /// <summary>
    /// 未知タグをまとめて分類する。<paramref name="existingTopics"/> は判断材料の既存ツリー。
    /// <paramref name="progress"/> には進み具合の短い文を渡す(数分かかるので画面に出す)。
    /// </summary>
    Task<IReadOnlyList<TopicClassifierVerdict>> ClassifyAsync(
        IReadOnlyList<string> tags,
        IReadOnlyList<TopicCatalogEntry> existingTopics,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default);
}

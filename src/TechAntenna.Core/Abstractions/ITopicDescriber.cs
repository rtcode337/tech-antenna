namespace TechAntenna.Core.Abstractions;

/// <summary>
/// LLM が返した説明1件(検証前の生の値)。<c>Index</c> は入力で振った 1 始まりの番号
/// (用語をそのまま写させると表記を崩す余地ができるため、番号で対応づける。分類と同じ作法)。
/// </summary>
public record TopicDescriptionVerdict(int Index, string Text);

/// <summary>
/// トピックの用語に一言説明を付ける。実装は分類器と同じクラス
/// (Claude Code ヘッドレス / Anthropic API)で、方式の選び方も分類と共通。
///
/// **新しく見つかった語の説明は分類の応答に相乗りする**(呼び出しを増やさない)。
/// この抽象が要るのは、**既にカタログにあって説明だけ無い語を埋めるとき** ——
/// 分類済みの語をもう一度分類させる意味は無いため。
/// </summary>
public interface ITopicDescriber
{
    string Name { get; }

    /// <summary>
    /// 用語をまとめて説明させる。<paramref name="progress"/> には進み具合の短い文を渡す。
    /// 応答に無い番号・空文字は呼び出し側が捨てる(LLM の応答をそのまま信じない)。
    /// </summary>
    Task<IReadOnlyList<TopicDescriptionVerdict>> DescribeAsync(
        IReadOnlyList<string> terms,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default);
}

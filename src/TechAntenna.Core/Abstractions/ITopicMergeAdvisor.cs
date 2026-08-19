using TechAntenna.Core.Topics;

namespace TechAntenna.Core.Abstractions;

/// <summary>
/// LLM が返した統合の候補1件(検証前の生の値)。
/// <c>Index</c> は入力で振った 1 始まりの番号、<c>Into</c> は寄せ先トピックの表記。
/// </summary>
public record TopicMergeVerdict(int Index, string Into);

/// <summary>
/// 語彙の中の<b>同義のトピック</b>を見つけて寄せ先を答える。実装は分類器と同じクラス。
///
/// これが要るのは、シード無しで語彙を育てられるようにするため。初期値が無いと、
/// あるバッチが `AI` を、別のバッチが `人工知能` を新トピックとして作りうる
/// (分類の検証はキーの重複しか見ないので防げない)。後から寄せる手当てがあれば、
/// 語彙を空から始められる。
/// </summary>
public interface ITopicMergeAdvisor
{
    string Name { get; }

    /// <summary>
    /// 語彙を渡して、同義のものだけ「どれへ寄せるか」を答えさせる。
    /// 寄せる必要が無いトピックは応答に含めない(全件について答えさせない)。
    /// </summary>
    Task<IReadOnlyList<TopicMergeVerdict>> SuggestMergesAsync(
        IReadOnlyList<TopicCatalogEntry> topics,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default);
}

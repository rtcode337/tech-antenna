using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>
/// ダイジェストの材料。集める側(Runner)が選別まで済ませて渡す —— LLM に全量を
/// 渡すとトークンを浪費するうえ、選別の基準(話題度・興味トピック)はデータ側の知識なので
/// プロンプトに埋めるより集める側に置くほうが検証できる。
/// </summary>
/// <param name="Scope">この材料で書くサマリーの守備範囲。材料と一緒に持ち回る ——
/// 指示文・通知のタイトル・保存先の出し分けが、材料の選び方と1対1で決まるため。</param>
/// <param name="Articles">材料にする記事。全体は話題度の高い順、
/// 興味トピックは選んだトピック(配下込み)に当たるもの。</param>
/// <param name="UpcomingEvents">これから開催されるイベント(興味トピックのときだけ)。</param>
/// <param name="SelectedTopics">収集対象に選んだトピックの表記(興味トピックのときだけ)。
/// 読者の関心として LLM に伝える。</param>
public record DigestMaterials(
    DigestScope Scope,
    IReadOnlyList<Article> Articles,
    IReadOnlyList<TechEvent> UpcomingEvents,
    IReadOnlyList<string> SelectedTopics)
{
    public bool IsEmpty => Articles.Count == 0 && UpcomingEvents.Count == 0;
}

/// <summary>
/// 材料からダイジェスト(今日のサマリー)を書く。実装は要約と同じ2方式
/// (Claude Code ヘッドレス / Anthropic API)で、選び方も同じ。
/// </summary>
public interface IDigestComposer
{
    /// <summary>実装名(画面の但し書きとログに出す)。</summary>
    string Name { get; }

    /// <summary>ダイジェストを1つ書く。応答が読めなかったときは例外(呼び出し側が再試行を判断する)。</summary>
    Task<Digest> ComposeAsync(DigestMaterials materials, CancellationToken cancellationToken = default);
}

namespace TechAntenna.Core.Models;

/// <summary>
/// 最近出た本(新刊・ムック)1 冊分の観測。
///
/// 書籍(<see cref="Book"/>)とは別物として持つ。あちらは「その分野で読んでおくべき本」で、
/// レビュー・推薦・書影を伴って一覧に並べるもの。こちらは<b>読ませるためではなく数えるため</b>に
/// 集めるので、持つのはタイトル・出版者・刊行日とタグだけ ——
/// 出版側がいまどのテーマに寄せているかをトレンドの一面として出す材料になる。
///
/// 雑誌の号(「日経ソフトウエア 2026年9月号」)には特集の見出しが書誌に載らないので、
/// テーマが読めるのは<b>ムック・入門書のタイトル</b>のほう
/// (「今知っておきたい生成AI厳選100ガイド」)。だから雑誌を除かずに集める ——
/// <see cref="Periodical"/> で落としているのは「読むべき本」の一覧のほうで、ここでは材料になる。
/// </summary>
public class NewRelease
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Title { get; init; }

    /// <summary>書誌の詳細ページ(収集元のもの)。重複判定のキー。</summary>
    public required Uri Url { get; init; }

    public string? Publisher { get; init; }

    /// <summary>刊行日。集計の窓を切るキー(直近 N か月ぶんだけを数える)。</summary>
    public DateOnly? PublishedOn { get; init; }

    /// <summary>収集元の名前(例: NDL サーチ)。</summary>
    public required string SourceName { get; init; }

    public required DateTimeOffset CollectedAt { get; init; }

    /// <summary>
    /// 正規化済みのタグ。タイトルから拾ったトピック(記事と同じ規則)で、
    /// 収集元がタグを持っているわけではない。
    /// </summary>
    public IReadOnlyList<string> Tags { get; set; } = [];

    /// <summary>
    /// 拾ったままのタグ(正規化前)。正規化の規則を変えたときの作り直し用に持つ ——
    /// ただしこの表は毎回同じ窓を引き直して上書きするので、規則を変えても次の収集で揃う
    /// (記事・イベント・書籍のような再正規化のジョブは持たせていない)。
    /// </summary>
    public IReadOnlyList<string> RawTags { get; set; } = [];
}

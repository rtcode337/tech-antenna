namespace TechAntenna.Core.Topics;

/// <summary>ツリーに出すトピックの名前。リンク先のキーと、画面に出す正式表記。</summary>
public record TopicName(string Key, string Display);

/// <summary>親子ツリーの1ノード(配下を含む)。</summary>
public record TopicTreeNode(string Key, string Display, IReadOnlyList<TopicTreeNode> Children);

/// <summary>
/// トピック1件を「語彙」として見たときの姿。同義語(別名)と、親子ツリー上の位置。
///
/// 記事・イベント・書籍の件数は持たない —— 横断の一覧は <see cref="TopicDetail"/> の仕事で、
/// こちらは<b>語彙がどう整理されているか</b>(どの語に寄せているか・どの粒度にいるか)だけを表す。
/// </summary>
/// <param name="Key">突き合わせキー(別名で引かれても正式表記のキーに寄せてある)。</param>
/// <param name="Display">画面に出す正式表記。カタログに無い語はキーと同じ。</param>
/// <param name="InCatalog">カタログ(人手の JSON + LLM 分類)に載っているか。載っていない語は平置き。</param>
/// <param name="Description">用語の一言説明(JSON に書いた記述、または LLM が埋めたもの)。無ければ null。</param>
/// <param name="Aliases">この正式表記へ寄せている別名。</param>
/// <param name="Ancestors">上の粒度へ辿った並び。<b>根 → 直近の親</b>の順。</param>
/// <param name="Children">1つ下の粒度(さらに配下を含む)。</param>
public record TopicStructure(
    string Key,
    string Display,
    bool InCatalog,
    string? Description,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<TopicName> Ancestors,
    IReadOnlyList<TopicTreeNode> Children)
{
    /// <summary>ツリー上で単独(親も子も無い)か。</summary>
    public bool IsIsolated => Ancestors.Count == 0 && Children.Count == 0;
}

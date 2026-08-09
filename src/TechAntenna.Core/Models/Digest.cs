namespace TechAntenna.Core.Models;

/// <summary>
/// 「今日のサマリー」1回分。収集した情報と興味トピックをもとに LLM がまとめた、
/// 押さえておくべき情報のダイジェスト。ホームに最新の1件を出す。
/// </summary>
public class Digest
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>生成した日時(UTC)。最新の1件を選ぶキー。</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>全体の導入(1〜2文)。何が動いている日かをまず一言で言う。</summary>
    public required string Lead { get; init; }

    /// <summary>押さえておく項目。多すぎると読まれないので生成時に数個へ絞らせる。</summary>
    public required IReadOnlyList<DigestItem> Items { get; init; }

    /// <summary>生成した方式(Claude Code / Anthropic API)。画面の但し書きに出す。</summary>
    public required string GeneratorName { get; init; }
}

/// <summary>ダイジェストの1項目。</summary>
/// <param name="Title">見出し(1行)。</param>
/// <param name="Body">本文(2〜3文)。</param>
/// <param name="Url">出典。**材料に含めた URL をそのまま写させたもの**で、無ければ null。</param>
public record DigestItem(string Title, string Body, string? Url);

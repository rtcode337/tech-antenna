namespace TechAntenna.Core.Models;

/// <summary>書籍の書誌情報。</summary>
public class Book
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Title { get; init; }

    /// <summary>ISBN-13(ハイフンなし)。提供されないソースでは null。</summary>
    public string? Isbn13 { get; init; }

    public IReadOnlyList<string> Authors { get; init; } = [];

    public string? Publisher { get; init; }

    public DateOnly? PublishedOn { get; init; }

    /// <summary>書誌詳細ページの URL。</summary>
    public Uri? Url { get; init; }

    /// <summary>書影の URL。画像そのものは保持せずリンクのみを持つ。</summary>
    public Uri? CoverUrl { get; init; }

    /// <summary>収集元の名前(例: openBD、Google Books)。</summary>
    public required string SourceName { get; init; }

    public required DateTimeOffset CollectedAt { get; init; }

    /// <summary>
    /// 正規化済みのタグ(<see cref="TagNormalizer"/> を通した値)。突き合わせに使う。
    /// init ではなく set なのは、正規化の規則を変えたときに <c>RawTags</c> から作り直すため。
    /// </summary>
    public IReadOnlyList<string> Tags { get; set; } = [];

    /// <summary>
    /// 収集元から受け取ったままのタグ。**正規化の規則を変えたら、ここから引き直す**。
    /// 正規化後の値しか持たないと、別名カタログを直しても過去のデータに反映できない
    /// (`claude code` を `claudecode` に寄せた後で分けたくなっても、元の表記が残っていない)。
    /// </summary>
    public IReadOnlyList<string> RawTags { get; init; } = [];
}

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

    /// <summary>正規化済みのタグ(<see cref="TagNormalizer"/> を通した値)。</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}

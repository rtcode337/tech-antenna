namespace TechAntenna.Core.Models;

/// <summary>勉強会・カンファレンス等のイベント。</summary>
public class TechEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Title { get; init; }

    /// <summary>イベントページの URL。重複判定のキーとして使う。</summary>
    public required Uri Url { get; init; }

    /// <summary>収集元の名前(例: connpass、Doorkeeper)。</summary>
    public required string SourceName { get; init; }

    public required DateTimeOffset StartsAt { get; init; }

    public DateTimeOffset? EndsAt { get; init; }

    /// <summary>開催場所。オンライン開催のみ・未定の場合は null。</summary>
    public string? Venue { get; init; }

    public bool IsOnline { get; init; }

    public required DateTimeOffset CollectedAt { get; init; }

    /// <summary>正規化済みのタグ(<see cref="TagNormalizer"/> を通した値)。</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}

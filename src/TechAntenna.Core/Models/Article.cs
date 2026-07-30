namespace TechAntenna.Core.Models;

/// <summary>収集した技術記事。</summary>
public class Article
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required string Title { get; init; }

    /// <summary>記事の URL。重複判定のキーとして使う。</summary>
    public required Uri Url { get; init; }

    /// <summary>収集元の名前(例: Zenn、Qiita)。</summary>
    public required string SourceName { get; init; }

    /// <summary>LLM による要約。未生成の間は null。</summary>
    public string? Summary { get; set; }

    /// <summary>収集元が公開日時を提供しない場合は null。</summary>
    public DateTimeOffset? PublishedAt { get; init; }

    public required DateTimeOffset CollectedAt { get; init; }

    /// <summary>正規化済みのタグ(<see cref="TagNormalizer"/> を通した値)。</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}

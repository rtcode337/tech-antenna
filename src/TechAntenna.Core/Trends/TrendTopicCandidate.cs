namespace TechAntenna.Core.Trends;

/// <summary>外部の技術トレンドから得たトピック候補。</summary>
public record TrendTopicCandidate(string Tag, int Score, string SourceName);

using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Topics;
using TechAntenna.Core.Trends;
using TechAntenna.Infrastructure.Feeds;

namespace TechAntenna.Infrastructure.Trends;

/// <summary>
/// はてなブックマークの人気エントリー(hotentry)RSS を、ブックマーク数で重み付けして集計する。
///
/// その場で 1 リクエスト取得するだけで、収集済みの記事には依存しない ——
/// トピック収集の結果が「最後に記事を収集したのがいつか」に左右されないようにするため
/// (Qiita のいいねと同じく、押した時点の外の反応を映す)。
/// RSS がエントリーごとにブックマーク数(hatena:bookmarkcount)とタグ(dc:subject)を
/// 持っているので、これで完結する。
///
/// タグは RSS のものに加えて、タイトルに出てくるカタログのトピックも足す
/// (記事の収集と同じ理屈 —— 収集元のタグだけでは粒度が粗く、トピックに結びつかない)。
/// </summary>
public class HatenaHotentryTrendSource(
    IHttpClientFactory httpClientFactory,
    TopicCatalog catalog) : ITrendTopicSource
{
    /// <summary>記事の巡回と同じ HttpClient(User-Agent・応答サイズ上限の設定を共有)。</summary>
    public const string HttpClientName = FeedArticleSource.HttpClientName;

    const string Endpoint = "https://b.hatena.ne.jp/hotentry/it.rss";

    public string Name => "はてなブックマーク";

    public async Task<IReadOnlyList<TrendTopicCandidate>> FetchAsync(
        CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);
        var xml = await client.GetStringAsync(Endpoint, cancellationToken);

        var scores = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in FeedParser.Parse(xml))
        {
            var weight = Math.Max(1, entry.BookmarkCount ?? 0);
            var tags = TagNormalizer.Normalize(
                entry.Tags.Concat(catalog.FindIn(entry.Title)));
            foreach (var tag in tags)
            {
                scores[tag] = scores.GetValueOrDefault(tag) + weight;
            }
        }

        return scores.Select(pair => new TrendTopicCandidate(pair.Key, pair.Value, Name)).ToList();
    }
}

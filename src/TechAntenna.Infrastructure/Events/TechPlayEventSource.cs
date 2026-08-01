using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Infrastructure.Feeds;

namespace TechAntenna.Infrastructure.Events;

/// <summary>
/// TECH PLAY のイベント RSS を読むイベントソース。
///
/// connpass / Doorkeeper と違い**キーワード検索を持たず、最新のイベントが流れてくるだけ**なので、
/// 巡回して差分を溜めることで広く拾う。企業主催のウェビナーが多く、ベンダー系のイベントは
/// connpass / Doorkeeper より厚い。
///
/// タグは RSS の <c>&lt;category&gt;</c> から作る。検索キーワードをタグにする他の収集元とは
/// 出どころが違うが、`TagNormalizer` を通せば同じ土俵でトピック横断に乗る。
/// </summary>
public class TechPlayEventSource(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    Uri feedUrl) : IEventSource
{
    /// <summary>記事フィードと同じ HttpClient を使う(User-Agent 等の設定を共有するため)。</summary>
    public const string HttpClientName = FeedArticleSource.HttpClientName;

    /// <summary>
    /// どのイベントにも必ず付いていて、絞り込みにも横断にも使えないカテゴリ。
    /// 落とさないと「テクノロジー」のような巨大で意味の無いタグができてしまう。
    /// </summary>
    static readonly HashSet<string> BoilerplateCategories =
        new(StringComparer.OrdinalIgnoreCase) { "IT", "テクノロジー", "イベント" };

    public string Name => "TECH PLAY";

    public async Task<IReadOnlyList<TechEvent>> FetchAsync(CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);
        var xml = await client.GetStringAsync(feedUrl, cancellationToken);

        var collectedAt = timeProvider.GetUtcNow();
        var today = collectedAt.UtcDateTime.Date;

        return TechPlayFeedParser.Parse(xml)
            // 終わったイベントは拾わない(Doorkeeper に since=今日 を渡しているのと同じ扱い)
            .Where(entry => entry.StartsAt.UtcDateTime.Date >= today)
            .Select(entry => new TechEvent
            {
                Title = entry.Title,
                Url = entry.Url,
                SourceName = Name,
                StartsAt = entry.StartsAt,
                EndsAt = entry.EndsAt,
                Venue = entry.Place,
                IsOnline = VenueClassifier.IsOnline(entry.Place, entry.Address),
                CollectedAt = collectedAt,
                Tags = TagNormalizer.Normalize(
                    entry.Categories.Where(c => !BoilerplateCategories.Contains(c))),
            })
            .ToList();
    }
}

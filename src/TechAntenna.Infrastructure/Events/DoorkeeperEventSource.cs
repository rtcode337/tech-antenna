using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Events;

/// <summary>
/// Doorkeeper API からイベントを取得する。
/// アクセストークン(Authorization: Bearer)が必須で、ホスト側の
/// HttpClient 登録(<see cref="HttpClientName"/>)で設定する。
/// </summary>
public class DoorkeeperEventSource(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    IReadOnlyList<string> keywords,
    TimeSpan? delayBetweenKeywords = null) : IEventSource
{
    public const string HttpClientName = "doorkeeper";

    /// <summary>キーワードを1つ検索してから次に移るまでの待ち時間。</summary>
    readonly TimeSpan _delayBetweenKeywords = delayBetweenKeywords ?? TimeSpan.FromSeconds(2);

    public string Name => "Doorkeeper";

    public async Task<IReadOnlyList<TechEvent>> FetchAsync(CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);

        var collectedAt = timeProvider.GetUtcNow();
        // Doorkeeper の q は1つの検索語しか取らないため、キーワードごとに問い合わせる。
        // 同じイベントが複数のキーワードで見つかることがあるので URL でまとめる
        var byUrl = new Dictionary<Uri, TechEvent>();

        for (var i = 0; i < keywords.Count; i++)
        {
            var keyword = keywords[i];

            // 過ぎたイベントを拾わないよう、今日以降に絞る
            var since = collectedAt.UtcDateTime.ToString("yyyy-MM-dd");
            var requestUri =
                $"https://api.doorkeeper.jp/events?q={Uri.EscapeDataString(keyword)}"
                + $"&since={since}&sort=starts_at";

            var json = await client.GetStringAsync(requestUri, cancellationToken);

            foreach (var entry in DoorkeeperResponseParser.Parse(json))
            {
                if (entry.StartsAt is not { } startsAt)
                {
                    continue;
                }

                if (byUrl.TryGetValue(entry.Url, out var existing))
                {
                    // 別のキーワードでも見つかったイベントは、タグを足す
                    byUrl[entry.Url] = WithTags(existing, [.. existing.Tags, keyword]);
                    continue;
                }

                byUrl[entry.Url] = new TechEvent
                {
                    Title = entry.Title,
                    Url = entry.Url,
                    SourceName = Name,
                    StartsAt = startsAt,
                    EndsAt = entry.EndsAt,
                    Venue = entry.VenueName,
                    // Doorkeeper にオンライン開催のフラグは無いため会場表記から推定する
                    IsOnline = IsOnlineVenue(entry.VenueName) || IsOnlineVenue(entry.Address),
                    CollectedAt = collectedAt,
                    // 検索キーワードをタグにして、記事・書籍と突き合わせられるようにする
                    Tags = TagNormalizer.Normalize([keyword]),
                };
            }

            // 最後のキーワードの後は待たない
            if (i < keywords.Count - 1 && _delayBetweenKeywords > TimeSpan.Zero)
            {
                await Task.Delay(_delayBetweenKeywords, cancellationToken);
            }
        }

        return byUrl.Values.ToList();
    }

    static bool IsOnlineVenue(string? text) =>
        text is not null
        && (text.Contains("オンライン") || text.Contains("online", StringComparison.OrdinalIgnoreCase));

    static TechEvent WithTags(TechEvent source, IEnumerable<string> tags) => new()
    {
        Id = source.Id,
        Title = source.Title,
        Url = source.Url,
        SourceName = source.SourceName,
        StartsAt = source.StartsAt,
        EndsAt = source.EndsAt,
        Venue = source.Venue,
        IsOnline = source.IsOnline,
        CollectedAt = source.CollectedAt,
        Tags = TagNormalizer.Normalize(tags),
    };
}

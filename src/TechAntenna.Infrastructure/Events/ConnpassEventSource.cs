using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Events;

/// <summary>
/// connpass API v2 からイベントを取得する。
/// API キー(X-API-Key)と User-Agent が必須で、これらはホスト側の
/// HttpClient 登録(<see cref="HttpClientName"/>)で設定する。
/// </summary>
public class ConnpassEventSource(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    IReadOnlyList<string> keywords,
    TimeSpan? delayBetweenKeywords = null) : IEventSource
{
    public const string HttpClientName = "connpass";

    /// <summary>キーワードに一致するイベントを開催日の近い順で取得する。</summary>
    const string EndpointFormat = "https://connpass.com/api/v2/events/?keyword={0}&order=2&count=100";

    /// <summary>キーワードを1つ検索してから次に移るまでの待ち時間。</summary>
    readonly TimeSpan _delayBetweenKeywords = delayBetweenKeywords ?? TimeSpan.FromSeconds(2);

    public string Name => "connpass";

    public async Task<IReadOnlyList<TechEvent>> FetchAsync(CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);

        var collectedAt = timeProvider.GetUtcNow();
        // keyword_or でまとめて引くとどのキーワードで見つかったか分からず、
        // hash_tag が無いイベントがタグ無しになってしまう。トピック横断に乗せるため、
        // キーワードごとに問い合わせて検索キーワードをタグにする
        var byUrl = new Dictionary<Uri, TechEvent>();

        for (var i = 0; i < keywords.Count; i++)
        {
            var keyword = keywords[i];
            var requestUri = string.Format(EndpointFormat, Uri.EscapeDataString(keyword));
            var json = await client.GetStringAsync(requestUri, cancellationToken);

            foreach (var entry in ConnpassResponseParser.Parse(json))
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
                    Venue = entry.Place,
                    // connpass にオンライン開催のフラグは無いため会場表記から推定する
                    IsOnline = VenueClassifier.IsOnline(entry.Place, entry.Address),
                    CollectedAt = collectedAt,
                    // 検索キーワードに加え、あればハッシュタグもタグにする
                    Tags = TagNormalizer.Normalize(
                        entry.HashTag is { Length: > 0 } hashTag ? [keyword, hashTag] : [keyword]),
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

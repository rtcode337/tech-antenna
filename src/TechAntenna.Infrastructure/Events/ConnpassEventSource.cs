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
    IReadOnlyList<string> keywords) : IEventSource
{
    public const string HttpClientName = "connpass";

    /// <summary>いずれかのキーワードに一致するイベントを開催日の近い順で取得する。</summary>
    const string EndpointFormat = "https://connpass.com/api/v2/events/?keyword_or={0}&order=2&count=100";

    public string Name => "connpass";

    public async Task<IReadOnlyList<TechEvent>> FetchAsync(CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);
        var requestUri = string.Format(
            EndpointFormat,
            Uri.EscapeDataString(string.Join(",", keywords)));
        var json = await client.GetStringAsync(requestUri, cancellationToken);

        var collectedAt = timeProvider.GetUtcNow();
        return ConnpassResponseParser.Parse(json)
            .Where(entry => entry.StartsAt is not null)
            .Select(entry => new TechEvent
            {
                Title = entry.Title,
                Url = entry.Url,
                SourceName = Name,
                StartsAt = entry.StartsAt!.Value,
                EndsAt = entry.EndsAt,
                Venue = entry.Place,
                // connpass にオンライン開催のフラグは無いため会場表記から推定する
                IsOnline = (entry.Place ?? "").Contains("オンライン")
                    || (entry.Address ?? "").Contains("オンライン"),
                CollectedAt = collectedAt,
                Tags = TagNormalizer.Normalize(
                    entry.HashTag is { Length: > 0 } hashTag ? [hashTag] : []),
            })
            .ToList();
    }
}

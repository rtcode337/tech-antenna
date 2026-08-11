using TechAntenna.Core;
using TechAntenna.Core.Topics;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Events;

/// <summary>
/// connpass API v2 からイベントを取得する。
/// API キー(X-API-Key)と User-Agent が必須で、これらはホスト側の
/// HttpClient 登録(<see cref="HttpClientName"/>)で設定する。
/// </summary>
/// <param name="apiKeyProvider">
/// API キーの実行時解決(画面から設定できるので起動時の値を固定しない)。
/// null なら常に設定済みとみなす(テスト用)。キーが無ければこの収集元だけスキップする。
/// </param>
public class ConnpassEventSource(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    IReadOnlyList<string> keywords,
    TimeSpan? delayBetweenKeywords = null,
    ITopicStore? topicStore = null,
    TopicCatalog? catalog = null,
    Func<string?>? apiKeyProvider = null) : IEventSource
{
    public const string HttpClientName = "connpass";

    /// <summary>キーワードに一致するイベントを開催日の近い順で取得する。</summary>
    const string EndpointFormat = "https://connpass.com/api/v2/events/?keyword={0}&order=2&count=100";

    /// <summary>キーワードを1つ検索してから次に移るまでの待ち時間。</summary>
    readonly TimeSpan _delayBetweenKeywords = delayBetweenKeywords ?? TimeSpan.FromSeconds(2);

    public string Name => "connpass";

    public async Task<IReadOnlyList<TechEvent>> FetchAsync(CancellationToken cancellationToken = default)
    {
        // キー未設定ならこの収集元だけスキップ(他のソースの収集は続く)
        if (apiKeyProvider is not null && string.IsNullOrWhiteSpace(apiKeyProvider()))
        {
            return [];
        }

        using var client = httpClientFactory.CreateClient(HttpClientName);

        var collectedAt = timeProvider.GetUtcNow();
        // keyword_or でまとめて引くとどのキーワードで見つかったか分からず、
        // hash_tag が無いイベントがタグ無しになってしまう。トピック横断に乗せるため、
        // キーワードごとに問い合わせて検索キーワードをタグにする
        var byUrl = new Dictionary<Uri, TechEvent>();

        // 選択されたトピックがあればそれを検索語にする(**正式表記のほう** —— 検索語に
        // 正規化で崩れたキー `生成ai` を投げても当たらない)。未設定なら設定ファイルの keywords
        var activeKeywords = topicStore is null
            ? keywords
            : (await topicStore.GetSelectedAsync(cancellationToken)).Select(topic => topic.Display).ToList();

        for (var i = 0; i < activeKeywords.Count; i++)
        {
            var keyword = activeKeywords[i];
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
                    byUrl[entry.Url] = WithTags(existing, [.. existing.RawTags, keyword]);
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
                    // 公式かどうかの判定材料(名簿との突き合わせは表示のたびに行う)と規模
                    Organizer = entry.Organizer,
                    ParticipantCount = entry.ParticipantCount,
                    CollectedAt = collectedAt,
                    // 検索キーワードに加え、あればハッシュタグもタグにする
                    Tags = (catalog ?? TopicCatalog.Empty).Normalize(
                        entry.HashTag is { Length: > 0 } hashTag ? [keyword, hashTag] : [keyword]),
                    RawTags = entry.HashTag is { Length: > 0 } raw
                        ? new List<string> { keyword, raw }
                        : new List<string> { keyword },
                };
            }

            // 最後のキーワードの後は待たない
            if (i < activeKeywords.Count - 1 && _delayBetweenKeywords > TimeSpan.Zero)
            {
                await Task.Delay(_delayBetweenKeywords, cancellationToken);
            }
        }

        return byUrl.Values.ToList();
    }

    // 受け取るのは**生のタグ**。正規化をここ 1 か所でだけ行い、RawTags と Tags がずれないようにする
    TechEvent WithTags(TechEvent source, IEnumerable<string> rawTags)
    {
        var raw = rawTags.ToList();

        return new TechEvent
        {
            Id = source.Id,
            Title = source.Title,
            Url = source.Url,
            SourceName = source.SourceName,
            StartsAt = source.StartsAt,
            EndsAt = source.EndsAt,
            Venue = source.Venue,
            IsOnline = source.IsOnline,
            Organizer = source.Organizer,
            ParticipantCount = source.ParticipantCount,
            CollectedAt = source.CollectedAt,
            Tags = (catalog ?? TopicCatalog.Empty).Normalize(raw),
            RawTags = raw,
        };
    }
}

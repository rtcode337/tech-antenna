using TechAntenna.Core;
using TechAntenna.Core.Topics;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Events;

/// <summary>
/// Doorkeeper API からイベントを取得する。
/// アクセストークン(Authorization: Bearer)が必須で、ホスト側の
/// HttpClient 登録(<see cref="HttpClientName"/>)で設定する。
/// </summary>
/// <param name="accessTokenProvider">
/// トークンの実行時解決(画面から設定できるので起動時の値を固定しない)。
/// null なら常に設定済みとみなす(テスト用)。トークンが無ければこの収集元だけスキップする。
/// </param>
public class DoorkeeperEventSource(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    IReadOnlyList<string> keywords,
    TimeSpan? delayBetweenKeywords = null,
    ITopicStore? topicStore = null,
    TopicCatalog? catalog = null,
    Func<string?>? accessTokenProvider = null) : IEventSource
{
    public const string HttpClientName = "doorkeeper";

    /// <summary>キーワードを1つ検索してから次に移るまでの待ち時間。</summary>
    readonly TimeSpan _delayBetweenKeywords = delayBetweenKeywords ?? TimeSpan.FromSeconds(2);

    public string Name => "Doorkeeper";

    public async Task<IReadOnlyList<TechEvent>> FetchAsync(CancellationToken cancellationToken = default)
    {
        // トークン未設定ならこの収集元だけスキップ(他のソースの収集は続く)
        if (accessTokenProvider is not null && string.IsNullOrWhiteSpace(accessTokenProvider()))
        {
            return [];
        }

        using var client = httpClientFactory.CreateClient(HttpClientName);

        var collectedAt = timeProvider.GetUtcNow();
        // Doorkeeper の q は1つの検索語しか取らないため、キーワードごとに問い合わせる。
        // 同じイベントが複数のキーワードで見つかることがあるので URL でまとめる
        var byUrl = new Dictionary<Uri, TechEvent>();

        // 選択されたトピックがあればそれを検索語にする(**正式表記のほう** —— 検索語に
        // 正規化で崩れたキー `生成ai` を投げても当たらない)。未設定なら設定ファイルの keywords
        var activeKeywords = topicStore is null
            ? keywords
            : (await topicStore.GetSelectedAsync(cancellationToken)).Select(topic => topic.Display).ToList();

        for (var i = 0; i < activeKeywords.Count; i++)
        {
            var keyword = activeKeywords[i];

            // 過ぎたイベントを拾わないよう、今日以降に絞る(「今日」は開催地の日本時間で数える
            // —— UTC の日付だと日本の朝 9 時までは前日として問い合わせることになる)
            var since = JapanTime.FormatDate(collectedAt);
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

                // Doorkeeper の q は説明文まで検索し、記号を落としてから照合する
                // (「C#」が実質「C」になり、「.NET」が説明文中の URL の .net に当たる)。
                // 検索語がタイトルに実際に含まれるものだけを採って、タグの意味を保つ
                if (!KeywordMatcher.Contains(entry.Title, keyword))
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
                    Venue = entry.VenueName,
                    // Doorkeeper にオンライン開催のフラグは無いため会場表記から推定する
                    IsOnline = VenueClassifier.IsOnline(entry.VenueName, entry.Address),
                    CollectedAt = collectedAt,
                    // 検索キーワードをタグにして、記事・書籍と突き合わせられるようにする
                    Tags = (catalog ?? TopicCatalog.Empty).Normalize([keyword]),
                    RawTags = [keyword],
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
            CollectedAt = source.CollectedAt,
            Tags = (catalog ?? TopicCatalog.Empty).Normalize(raw),
            RawTags = raw,
        };
    }
}

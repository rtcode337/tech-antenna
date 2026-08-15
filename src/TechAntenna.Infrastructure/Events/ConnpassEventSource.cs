using System.Collections.Concurrent;
using TechAntenna.Core;
using TechAntenna.Core.Topics;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Events;

/// <summary>
/// connpass API v2 からイベントを取得する。
/// API キー(X-API-Key)と User-Agent が必須で、これらはホスト側の
/// HttpClient 登録(<see cref="HttpClientName"/>)で設定する。
///
/// <b>引き方は2つある。</b>
/// <list type="number">
/// <item>選んだトピックを<b>検索語</b>にして引く(従来から)。</item>
/// <item>購読しているシリーズを<b>ID で直接</b>引く(<see cref="FollowedGroups"/>)——
/// 検索語に一致するかは問わない。RubyKaigi のような固有名詞のカンファレンスは
/// 「AI」「LLM」といった収集語をタイトルに持たないので、検索だけでは構造的に落ちるため。</item>
/// </list>
/// </summary>
/// <param name="apiKeyProvider">
/// API キーの実行時解決(画面から設定できるので起動時の値を固定しない)。
/// null なら常に設定済みとみなす(テスト用)。キーが無ければこの収集元だけスキップする。
/// </param>
/// <param name="followedProvider">
/// 購読しているグループの名簿の実行時解決(これも画面から直せる)。
/// </param>
public class ConnpassEventSource(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    IReadOnlyList<string> keywords,
    TimeSpan? delayBetweenKeywords = null,
    ITopicStore? topicStore = null,
    TopicCatalog? catalog = null,
    Func<string?>? apiKeyProvider = null,
    Func<FollowedGroups>? followedProvider = null) : IEventSource
{
    public const string HttpClientName = "connpass";

    /// <summary>キーワードに一致するイベントを開催日の近い順で取得する。</summary>
    const string EndpointFormat = "https://connpass.com/api/v2/events/?keyword={0}&order=2&count=100";

    /// <summary>シリーズ(グループ)のイベントを開催日の近い順で取得する。検索語は使わない。</summary>
    const string SeriesEndpointFormat = "https://connpass.com/api/v2/events/?series_id={0}&order=2&count=100";

    /// <summary>サブドメインからシリーズ ID を引く。名簿に数字ではなくサブドメインを書けるようにするため。</summary>
    const string GroupEndpointFormat = "https://connpass.com/api/v2/groups/?subdomain={0}";

    /// <summary>キーワードを1つ検索してから次に移るまでの待ち時間。</summary>
    readonly TimeSpan _delayBetweenKeywords = delayBetweenKeywords ?? TimeSpan.FromSeconds(2);

    /// <summary>
    /// サブドメイン → シリーズ ID。<b>プロセスの間ずっと覚えておく</b> ——
    /// グループの ID は変わらないので、収集のたびに引き直すのは相手を無駄に叩くだけ。
    /// </summary>
    readonly ConcurrentDictionary<string, string> _seriesIdBySubdomain = new(StringComparer.OrdinalIgnoreCase);

    public string Name => "connpass";

    /// <summary>購読しているシリーズがあれば、トピックの選択が空でも集めるものがある。</summary>
    public bool WorksWithoutTopics => Followed().Count > 0;

    IReadOnlyList<FollowedGroup> Followed() =>
        (followedProvider?.Invoke() ?? FollowedGroups.Empty).For(FollowedGroups.Connpass);

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

        var followed = Followed();
        // 待ち時間を入れるかどうかを「最後の1件か」で決めるため、先に総数を数えておく
        var remaining = activeKeywords.Count + followed.Count;

        // --- 1) 検索語で引く ---
        foreach (var keyword in activeKeywords)
        {
            var requestUri = string.Format(EndpointFormat, Uri.EscapeDataString(keyword));
            var json = await client.GetStringAsync(requestUri, cancellationToken);

            foreach (var entry in ConnpassResponseParser.Parse(json))
            {
                Merge(entry, collectedAt, byUrl, [keyword], pickedBy: null);
            }

            await WaitIfMoreAsync(--remaining, cancellationToken);
        }

        // --- 2) 購読しているシリーズを ID で引く ---
        foreach (var group in followed)
        {
            // **解決や取得に失敗したら黙って飛ばす**(名簿の1行の打ち間違いで収集全体を止めない)。
            // 名簿が正しいかは設定画面で見て直せる
            string json;
            try
            {
                var seriesId = await ResolveSeriesIdAsync(client, group.Id, cancellationToken);
                if (seriesId is null)
                {
                    await WaitIfMoreAsync(--remaining, cancellationToken);
                    continue;
                }

                json = await client.GetStringAsync(
                    string.Format(SeriesEndpointFormat, Uri.EscapeDataString(seriesId)), cancellationToken);
            }
            catch (HttpRequestException)
            {
                await WaitIfMoreAsync(--remaining, cancellationToken);
                continue;
            }

            foreach (var entry in ConnpassResponseParser.Parse(json))
            {
                // **検索語が無いので、タグになるのはハッシュタグだけ。**
                // グループの表示名をタグにはしない —— イベント名が語彙に流れ込むと、
                // タグの一覧と LLM の仕分けが「その回限りの固有名詞」で埋まる
                Merge(entry, collectedAt, byUrl, [], pickedBy: group.Label);
            }

            await WaitIfMoreAsync(--remaining, cancellationToken);
        }

        return byUrl.Values.ToList();
    }

    /// <summary>
    /// 名簿の識別子からシリーズ ID を得る。数字ならそのまま、そうでなければ
    /// サブドメインとみなして <c>/groups/</c> で引く —— <b>グループのページを開けば分かる
    /// サブドメイン(<c>https://&lt;ここ&gt;.connpass.com/</c>)で書けるようにするため</b>。
    /// 数字の ID は画面から辿れる場所に出ておらず、名簿を人が作れなくなる。
    /// </summary>
    async Task<string?> ResolveSeriesIdAsync(HttpClient client, string id, CancellationToken cancellationToken)
    {
        if (id.All(char.IsAsciiDigit))
        {
            return id;
        }

        if (_seriesIdBySubdomain.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var json = await client.GetStringAsync(
            string.Format(GroupEndpointFormat, Uri.EscapeDataString(id)), cancellationToken);
        var resolved = ConnpassResponseParser.ParseGroupId(json);
        if (resolved is not null)
        {
            _seriesIdBySubdomain[id] = resolved;
        }

        return resolved;
    }

    /// <summary>最後の1件の後は待たない(手動実行で無駄に待たせないため)。</summary>
    async Task WaitIfMoreAsync(int remaining, CancellationToken cancellationToken)
    {
        if (remaining > 0 && _delayBetweenKeywords > TimeSpan.Zero)
        {
            await Task.Delay(_delayBetweenKeywords, cancellationToken);
        }
    }

    /// <summary>
    /// 1件ぶんを取り込む。同じイベントが別のキーワード・別の経路でも見つかったら、
    /// <b>タグを足してまとめる</b>(URL が同一性のキー)。
    /// </summary>
    void Merge(
        ConnpassEventEntry entry,
        DateTimeOffset collectedAt,
        Dictionary<Uri, TechEvent> byUrl,
        IReadOnlyList<string> keywordTags,
        string? pickedBy)
    {
        if (entry.StartsAt is not { } startsAt)
        {
            return;
        }

        var rawTags = entry.HashTag is { Length: > 0 } hashTag
            ? [.. keywordTags, hashTag]
            : keywordTags;

        if (byUrl.TryGetValue(entry.Url, out var existing))
        {
            // 別のキーワードでも見つかったイベントは、タグを足す。
            // **購読で見つけた印は消さない**(検索でも当たったからといって、
            // 「購読しているから載っている」という説明が嘘になるわけではない)
            byUrl[entry.Url] = WithTags(existing, [.. existing.RawTags, .. rawTags], existing.PickedBy ?? pickedBy);

            return;
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
            PickedBy = pickedBy,
            // 検索キーワードに加え、あればハッシュタグもタグにする
            Tags = (catalog ?? TopicCatalog.Empty).Normalize(rawTags),
            RawTags = rawTags,
        };
    }

    // 受け取るのは**生のタグ**。正規化をここ 1 か所でだけ行い、RawTags と Tags がずれないようにする
    TechEvent WithTags(TechEvent source, IEnumerable<string> rawTags, string? pickedBy)
    {
        var raw = rawTags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

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
            PickedBy = pickedBy,
            Tags = (catalog ?? TopicCatalog.Empty).Normalize(raw),
            RawTags = raw,
        };
    }
}

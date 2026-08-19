using TechAntenna.Core;
using TechAntenna.Core.Topics;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Events;

/// <summary>
/// Doorkeeper API からイベントを取得する。
/// アクセストークン(Authorization: Bearer)が必須で、ホスト側の
/// HttpClient 登録(<see cref="HttpClientName"/>)で設定する。
///
/// <b>引き方は2つある。</b>
/// <list type="number">
/// <item>選んだトピックを<b>検索語</b>にして <c>/events?q=</c> を引く(従来から)。</item>
/// <item>購読しているコミュニティを <c>/groups/&lt;名前&gt;/events</c> で<b>直接</b>引く
/// (<see cref="FollowedGroups"/>)—— 検索語に一致するかは問わない。</item>
/// </list>
/// </summary>
/// <param name="accessTokenProvider">
/// トークンの実行時解決(画面から設定できるので起動時の値を固定しない)。
/// null なら常に設定済みとみなす(テスト用)。トークンが無ければこの収集元だけスキップする。
/// </param>
/// <param name="followedProvider">
/// 購読しているグループの名簿の実行時解決(これも画面から直せる)。
/// </param>
public class DoorkeeperEventSource(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    IReadOnlyList<string> keywords,
    TimeSpan? delayBetweenKeywords = null,
    ITopicStore? topicStore = null,
    TopicCatalog? catalog = null,
    Func<string?>? accessTokenProvider = null,
    Func<FollowedGroups>? followedProvider = null) : IEventSource
{
    public const string HttpClientName = "doorkeeper";

    /// <summary>キーワードを1つ検索してから次に移るまでの待ち時間。</summary>
    readonly TimeSpan _delayBetweenKeywords = delayBetweenKeywords ?? TimeSpan.FromSeconds(2);

    public string Name => "Doorkeeper";

    /// <summary>購読しているコミュニティがあれば、トピックの選択が空でも集めるものがある。</summary>
    public bool WorksWithoutTopics => Followed().Count > 0;

    IReadOnlyList<FollowedGroup> Followed() =>
        (followedProvider?.Invoke() ?? FollowedGroups.Empty).For(FollowedGroups.Doorkeeper);

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

        // 選択されたトピックがあればそれを検索語にする(正式表記のほう —— 検索語に
        // 正規化で崩れたキー `生成ai` を投げても当たらない)。未設定なら設定ファイルの keywords
        var activeKeywords = topicStore is null
            ? keywords
            : (await topicStore.GetSelectedAsync(cancellationToken)).Select(topic => topic.Display).ToList();

        var followed = Followed();
        // 待ち時間を入れるかどうかを「最後の1件か」で決めるため、先に総数を数えておく
        var remaining = activeKeywords.Count + followed.Count;

        // 過ぎたイベントを拾わないよう、今日以降に絞る(「今日」は開催地の日本時間で数える
        // —— UTC の日付だと日本の朝 9 時までは前日として問い合わせることになる)
        var since = JapanTime.FormatDate(collectedAt);

        // --- 1) 検索語で引く ---
        foreach (var keyword in activeKeywords)
        {
            // expand[]=group で主催グループを名前まで展開させる(既定では数値の ID だけが返り、
            // 「公式のイベントか」の判定材料にならない)。展開が効かなくても収集は続く ——
            // API は alpha 扱いなので、名前が取れなければ主催者を null のままにする
            var requestUri =
                $"https://api.doorkeeper.jp/events?q={Uri.EscapeDataString(keyword)}"
                + $"&since={since}&sort=starts_at&expand[]=group";

            var json = await client.GetStringAsync(requestUri, cancellationToken);

            foreach (var entry in DoorkeeperResponseParser.Parse(json))
            {
                // Doorkeeper の q は説明文まで検索し、記号を落としてから照合する
                // (「C#」が実質「C」になり、「.NET」が説明文中の URL の .net に当たる)。
                // 検索語がタイトルに実際に含まれるものだけを採って、タグの意味を保つ
                if (!KeywordMatcher.Contains(entry.Title, keyword))
                {
                    continue;
                }

                Merge(entry, collectedAt, byUrl, [keyword], pickedBy: null);
            }

            await WaitIfMoreAsync(--remaining, cancellationToken);
        }

        // --- 2) 購読しているコミュニティを直接引く ---
        foreach (var group in followed)
        {
            // タイトルの照合はしない。検索語で引いていないので照合する相手が無いし、
            // 「このコミュニティのイベントは全部見たい」が購読の意味そのもの
            var requestUri =
                $"https://api.doorkeeper.jp/groups/{Uri.EscapeDataString(group.Id)}/events"
                + $"?since={since}&sort=starts_at&expand[]=group";

            string json;
            try
            {
                json = await client.GetStringAsync(requestUri, cancellationToken);
            }
            catch (HttpRequestException)
            {
                // 名簿の1行の打ち間違い(404)で収集全体を止めない。
                // 名簿が正しいかは設定画面で見て直せる
                await WaitIfMoreAsync(--remaining, cancellationToken);
                continue;
            }

            foreach (var entry in DoorkeeperResponseParser.Parse(json))
            {
                // 検索語が無いのでタグは付かない。グループの表示名をタグにはしない ——
                // イベント名が語彙に流れ込むと、タグの一覧と LLM の仕分けが固有名詞で埋まる
                Merge(entry, collectedAt, byUrl, [], pickedBy: group.Label);
            }

            await WaitIfMoreAsync(--remaining, cancellationToken);
        }

        return byUrl.Values.ToList();
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
        DoorkeeperEventEntry entry,
        DateTimeOffset collectedAt,
        Dictionary<Uri, TechEvent> byUrl,
        IReadOnlyList<string> keywordTags,
        string? pickedBy)
    {
        if (entry.StartsAt is not { } startsAt)
        {
            return;
        }

        if (byUrl.TryGetValue(entry.Url, out var existing))
        {
            // 購読で見つけた印は消さない(検索でも当たったからといって、
            // 「購読しているから載っている」という説明が嘘になるわけではない)
            byUrl[entry.Url] = WithTags(
                existing, [.. existing.RawTags, .. keywordTags], existing.PickedBy ?? pickedBy);

            return;
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
            // 公式かどうかの判定材料(名簿との突き合わせは表示のたびに行う)と規模
            Organizer = entry.Organizer,
            ParticipantCount = entry.ParticipantCount,
            CollectedAt = collectedAt,
            PickedBy = pickedBy,
            // 検索キーワードをタグにして、記事・書籍と突き合わせられるようにする
            Tags = (catalog ?? TopicCatalog.Empty).Normalize(keywordTags),
            RawTags = keywordTags,
        };
    }

    // 受け取るのは生のタグ。正規化をここ 1 か所でだけ行い、RawTags と Tags がずれないようにする
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

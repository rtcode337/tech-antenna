using TechAntenna.Core;
using TechAntenna.Core.Topics;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Events;

/// <summary>
/// connpass を<b>月ごとに全件なめて、人が集まっているものだけを残す</b>収集元。
///
/// キーワード検索は「こちらが知っている語」しか拾えないので、名前を知らない大型イベントは
/// 永久に出てこない。購読(<see cref="FollowedGroups"/>)も「こちらが知っているグループ」が要る。
/// <b>ここだけは何も知らなくてよい</b> —— <c>ym</c> で月を指定して全件取り、
/// <b>参加者数のしきい値で切る</b>。「注目度が高いイベントを取りたい」に一番素直に答える経路。
///
/// <b>既定では動かさない。</b> 1か月ぶんを取るのに数十リクエストかかり、
/// connpass のレート制限(1 秒 1 リクエスト)と相談する必要があるため ——
/// 使うかどうかは<b>画面(設定 → 外部連携)で切り替える</b>。
/// appsettings(<c>Connpass:Sweep</c>)に残っているのは数の設定だけ。
/// </summary>
/// <param name="minParticipants">
/// これ以上の参加者がいるものだけを残す。<b>この経路の存在意義そのもの</b>なので、
/// 下げすぎると小さな勉強会が大量に入り、検索で集めていたときと同じ状態に戻る。
/// </param>
/// <param name="months">今月から何か月ぶん見るか。先の月ほど登録が少ないので、遠くまで見ても実入りは少ない。</param>
public class ConnpassSweepEventSource(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    int minParticipants,
    int months,
    TimeSpan? delayBetweenRequests = null,
    TopicCatalog? catalog = null,
    Func<string?>? apiKeyProvider = null,
    Func<bool>? enabledProvider = null,
    Func<DateTimeOffset?>? incrementalSinceProvider = null,
    Func<Task>? onSwept = null) : IEventSource
{
    /// <summary>connpass の 1 リクエストあたりの上限。</summary>
    const int PageSize = 100;

    /// <summary>
    /// 1か月ぶんに費やすページ数の上限。<b>ここで打ち切ったら結果に出す</b> ——
    /// 黙って切ると「全部見た」と読めてしまう(<see cref="Truncated"/>)。
    /// </summary>
    const int MaxPagesPerMonth = 10;

    const string EndpointFormat =
        "https://connpass.com/api/v2/events/?ym={0}&order=2&count={1}&start={2}";

    /// <summary>
    /// **差分取得の口。** `publish_ymd`(公開された年月日。複数指定可)で「前回からあとに
    /// 公開されたイベント」だけを引く —— 全掃きは月ごとに最大 10 ページ x 月数かかるが、
    /// こちらは**たいてい 1 リクエスト**で済む(1 日に公開されるイベントは 100 件前後)。
    /// </summary>
    const string IncrementalEndpointFormat =
        "https://connpass.com/api/v2/events/?order=2&count={0}&start={1}{2}";

    /// <summary>
    /// 差分で遡れる日数の上限。**これを超えたら全掃きに戻す** ——
    /// 長く止めていた後に何十日ぶんも `publish_ymd` を並べるより、月ごとに掃くほうが速い。
    /// </summary>
    const int MaxIncrementalDays = 8;

    readonly TimeSpan _delay = delayBetweenRequests ?? TimeSpan.FromSeconds(5);

    public string Name => "connpass(面掃き)";

    /// <summary>
    /// この経路を使う設定になっているか(画面から切り替える。<b>既定は無効</b>)。
    /// <b>実行のたびに読む</b>ので、起動しなおさずに効く。
    /// </summary>
    bool Enabled => enabledProvider?.Invoke() ?? true;

    /// <summary>
    /// 検索語を一切使わないので、トピックの選択が空でも集まる。
    /// <b>無効のときは false</b> —— そうしないと、面掃きを止めているのに
    /// 「トピックが空でも集めるものがある」とランナーが判断し、
    /// 「トピックを選んでいないので集まりません」の案内が出なくなる。
    /// </summary>
    public bool WorksWithoutTopics => Enabled;

    /// <summary>
    /// 上限に当たって最後まで見られなかった月(<c>2026-09</c> の形)。
    /// 収集のたびに作り直す。<b>収集元の側で握りつぶさず、呼び出し側がログに出せるようにする。</b>
    /// </summary>
    public IReadOnlyList<string> Truncated { get; private set; } = [];

    /// <summary>この経路で入ったことの説明。イベントのカードにそのまま出る。</summary>
    public string PickedByLabel => $"参加者 {minParticipants} 人以上";

    public async Task<IReadOnlyList<TechEvent>> FetchAsync(CancellationToken cancellationToken = default)
    {
        // 画面で止めているか、キー未設定ならこの収集元だけスキップ(他のソースの収集は続く)
        if (!Enabled || (apiKeyProvider is not null && string.IsNullOrWhiteSpace(apiKeyProvider())))
        {
            return [];
        }

        using var client = httpClientFactory.CreateClient(ConnpassEventSource.HttpClientName);

        var collectedAt = timeProvider.GetUtcNow();
        var byUrl = new Dictionary<Uri, TechEvent>();
        var truncated = new List<string>();

        // **月の起点は日本時間の今月。** UTC で数えると、月初の朝 9 時までは前の月を掃くことになる
        var start = JapanTime.To(collectedAt);

        // **前回からの差分で済むならそうする。** 全掃きは「期間の全件を数え上げる」ので高い
        // (月ごとに最大 10 ページ x 月数)が、**新しく公開されたぶんだけなら 1 リクエスト**で足りる。
        // ただし**参加者数が伸びてしきい値を越えたイベントは差分に出てこない**(公開日は変わらない)ので、
        // 呼び出し側が週に一度は全掃きへ戻す(`SweepSettings.FullInterval`)
        if (IncrementalDays(incrementalSinceProvider?.Invoke(), start) is { Count: > 0 } days)
        {
            await SweepPublishedAsync(client, days, collectedAt, start, byUrl, cancellationToken);
            Truncated = [];
            if (onSwept is not null)
            {
                await onSwept();
            }

            return byUrl.Values.ToList();
        }

        for (var offset = 0; offset < months; offset++)
        {
            var month = new DateTime(start.Year, start.Month, 1).AddMonths(offset);
            if (!await SweepMonthAsync(client, month, collectedAt, byUrl, cancellationToken))
            {
                truncated.Add($"{month:yyyy-MM}");
            }
        }

        Truncated = truncated;
        // **掃けたときだけ記録する。** 途中で例外なら記録しない(次の収集で掃き直す)
        if (onSwept is not null)
        {
            await onSwept();
        }

        return byUrl.Values.ToList();
    }

    /// <summary>
    /// 差分で引く日付(日本時間)。**null か、遡りすぎ・未来なら空**(= 全掃きに戻す)。
    /// 前回の実行日を含めるのは、その日の途中で走ったときに取りこぼさないため。
    /// </summary>
    static IReadOnlyList<DateTime> IncrementalDays(DateTimeOffset? since, DateTimeOffset nowInJapan)
    {
        if (since is not { } from)
        {
            return [];
        }

        var first = JapanTime.To(from).Date;
        var last = nowInJapan.Date;
        if (first > last || (last - first).TotalDays + 1 > MaxIncrementalDays)
        {
            return [];
        }

        var days = new List<DateTime>();
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            days.Add(day);
        }

        return days;
    }

    /// <summary>
    /// 指定した日に**公開された**イベントだけを取る(`publish_ymd` は複数指定できるので、
    /// 何日ぶんでも 1 リクエストにまとまる)。
    /// **開催が掃く期間の外にあるものは捨てる** —— 全掃きと同じ範囲だけを持つため
    /// (差分で入る/入らないが実行時刻で変わらないようにする)。
    /// </summary>
    async Task SweepPublishedAsync(
        HttpClient client,
        IReadOnlyList<DateTime> days,
        DateTimeOffset collectedAt,
        DateTimeOffset startInJapan,
        Dictionary<Uri, TechEvent> byUrl,
        CancellationToken cancellationToken)
    {
        var query = string.Concat(days.Select(day => $"&publish_ymd={day:yyyyMMdd}"));
        // **全掃きと同じ範囲**にそろえる —— 全掃きは今月の頭から months か月ぶんを
        // `ym` で引くので、差分でもその範囲の開催だけを残す
        // (そろえないと、差分で入るか全掃きで入るかによって持ち物が変わる)
        var windowStart = new DateTime(startInJapan.Year, startInJapan.Month, 1);
        var windowEnd = windowStart.AddMonths(months);

        for (var page = 0; page < MaxPagesPerMonth; page++)
        {
            var requestUri = string.Format(
                IncrementalEndpointFormat, PageSize, page * PageSize + 1, query);
            var json = await client.GetStringAsync(requestUri, cancellationToken);
            var result = ConnpassResponseParser.ParsePage(json);

            foreach (var entry in result.Events)
            {
                if (entry.StartsAt is { } startsAt
                    && JapanTime.To(startsAt).Date is var startDate
                    && startDate >= windowStart && startDate < windowEnd)
                {
                    Take(entry, collectedAt, byUrl);
                }
            }

            if (result.Events.Count < PageSize)
            {
                return;
            }

            if (page < MaxPagesPerMonth - 1 && _delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }
        }
    }

    /// <summary>1か月ぶんを頭から取る。最後まで見られたら true、上限で打ち切ったら false。</summary>
    async Task<bool> SweepMonthAsync(
        HttpClient client,
        DateTime month,
        DateTimeOffset collectedAt,
        Dictionary<Uri, TechEvent> byUrl,
        CancellationToken cancellationToken)
    {
        var ym = $"{month:yyyyMM}";

        for (var page = 0; page < MaxPagesPerMonth; page++)
        {
            // start は 1 始まり
            var requestUri = string.Format(EndpointFormat, ym, PageSize, page * PageSize + 1);
            var json = await client.GetStringAsync(requestUri, cancellationToken);
            var result = ConnpassResponseParser.ParsePage(json);

            foreach (var entry in result.Events)
            {
                Take(entry, collectedAt, byUrl);
            }

            // 返ってきた件数が上限に満たないなら、そこで終わり
            if (result.Events.Count < PageSize)
            {
                return true;
            }

            // 総件数が分かっていて、そこまで読み切ったなら終わり
            if (result.Available is { } available && (page + 1) * PageSize >= available)
            {
                return true;
            }

            await Task.Delay(_delay, cancellationToken);
        }

        return false;
    }

    void Take(ConnpassEventEntry entry, DateTimeOffset collectedAt, Dictionary<Uri, TechEvent> byUrl)
    {
        if (entry.StartsAt is not { } startsAt)
        {
            return;
        }

        // **null(参加者数が取れていない)は残さない。** この経路は「人が集まっている」ことだけを
        // 根拠に拾っているので、根拠が無いものを通すと単なる全件取り込みになる
        if (entry.ParticipantCount is not { } participants || participants < minParticipants)
        {
            return;
        }

        // **検索語が無いので、タグになるのはハッシュタグだけ。**
        IReadOnlyList<string> rawTags = entry.HashTag is { Length: > 0 } hashTag ? [hashTag] : [];

        byUrl[entry.Url] = new TechEvent
        {
            Title = entry.Title,
            Url = entry.Url,
            // **収集元の名前は connpass と分けてある** —— どの経路で入ったのかが
            // 一覧の「収集元」で読めないと、しきい値を動かしたときの効き目が確かめられない
            SourceName = Name,
            StartsAt = startsAt,
            EndsAt = entry.EndsAt,
            Venue = entry.Place,
            IsOnline = VenueClassifier.IsOnline(entry.Place, entry.Address),
            Organizer = entry.Organizer,
            ParticipantCount = participants,
            CollectedAt = collectedAt,
            PickedBy = PickedByLabel,
            Tags = (catalog ?? TopicCatalog.Empty).Normalize(rawTags),
            RawTags = rawTags,
        };
    }
}

using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Events;

/// <summary>
/// Doorkeeper を<b>期間で全件なめて、人が集まっているものだけを残す</b>収集元。
/// connpass の <see cref="ConnpassSweepEventSource"/> と同じ役割で、相手だけが違う。
///
/// <b>検索語(<c>q</c>)を付けないのが要点。</b> <c>/events</c> は <c>q</c> を省いても
/// 期間(<c>since</c>/<c>until</c>)で引けるので、<b>こちらが名前を知らないイベント</b>も
/// 拾える —— 検索は「知っている語」、購読は「知っているグループ」しか拾えず、
/// どちらも「知らない大型カンファレンス」を構造的に落とす。
///
/// <b>既定では動かさない。</b> 1 回の収集で数十リクエストになりうるので、
/// 使うかどうかは<b>画面(設定 → 外部連携)で切り替える</b>。
/// appsettings(<c>Doorkeeper:Sweep</c>)に残っているのは数の設定だけ。
/// </summary>
/// <param name="minParticipants">
/// これ以上の参加者がいるものだけを残す。<b>この経路の存在意義そのもの</b>なので、
/// 下げすぎると小さな勉強会が大量に入り、検索で集めていたときと同じ状態に戻る。
/// </param>
/// <param name="months">今日から何か月先まで見るか。</param>
public class DoorkeeperSweepEventSource(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    int minParticipants,
    int months,
    TimeSpan? delayBetweenRequests = null,
    Func<string?>? accessTokenProvider = null,
    Func<bool>? enabledProvider = null,
    Func<bool>? dueProvider = null,
    Func<Task>? onSwept = null) : IEventSource
{
    /// <summary>
    /// 読むページ数の上限。<b>打ち切ったら <see cref="Truncated"/> に残す</b> ——
    /// 黙って切ると「全部見た」と読めてしまう(connpass の面掃きと同じ扱い)。
    /// </summary>
    const int MaxPages = 10;

    /// <summary>認証済みのレート制限は 300 リクエスト / 300 秒 = 1 秒 1 回。</summary>
    readonly TimeSpan _delay = delayBetweenRequests ?? TimeSpan.FromSeconds(2);

    public string Name => "Doorkeeper(面掃き)";

    /// <summary>
    /// この経路を使う設定になっているか(画面から切り替える。<b>既定は無効</b>)。
    /// <b>実行のたびに読む</b>ので、起動しなおさずに効く。
    /// </summary>
    bool Enabled => enabledProvider?.Invoke() ?? true;

    /// <summary>
    /// 検索語を一切使わないので、トピックの選択が空でも集まる。
    /// <b>無効のときは false</b>(connpass の面掃きと同じ理由)。
    /// </summary>
    public bool WorksWithoutTopics => Enabled;

    /// <summary>上限に当たって最後まで見られなかったか。収集のたびに作り直す。</summary>
    public bool Truncated { get; private set; }

    /// <summary>この経路で入ったことの説明。イベントのカードにそのまま出る。</summary>
    public string PickedByLabel => $"参加者 {minParticipants} 人以上";

    public async Task<IReadOnlyList<TechEvent>> FetchAsync(CancellationToken cancellationToken = default)
    {
        // 画面で止めているか、トークン未設定ならこの収集元だけスキップ(他のソースの収集は続く)
        if (!Enabled
            || (accessTokenProvider is not null && string.IsNullOrWhiteSpace(accessTokenProvider())))
        {
            return [];
        }

        // 前回から間もないなら掃かない(connpass の面掃きと同じ)。期間の全件を
        // 数え上げる経路なので 1 回が高く、中身は 1 日でほとんど変わらない
        if (dueProvider is not null && !dueProvider())
        {
            return [];
        }

        using var client = httpClientFactory.CreateClient(DoorkeeperEventSource.HttpClientName);

        var collectedAt = timeProvider.GetUtcNow();
        // 境界は日本時間で数える。UTC の日付だと、日本の朝 9 時までは前日から引くことになる
        var start = JapanTime.To(collectedAt);
        var since = JapanTime.FormatDate(collectedAt);
        var until = start.AddMonths(months).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var byUrl = new Dictionary<Uri, TechEvent>();
        Truncated = false;

        // ページは 1 始まり。空のページが返ったらそこで終わり —— 総件数を返さない API なので、
        // connpass のように「読み切ったか」を件数から判定できない
        for (var page = 1; page <= MaxPages; page++)
        {
            var requestUri =
                $"https://api.doorkeeper.jp/events?since={since}&until={until}"
                + $"&page={page}&sort=starts_at&expand[]=group";

            var json = await client.GetStringAsync(requestUri, cancellationToken);
            var entries = DoorkeeperResponseParser.Parse(json);
            if (entries.Count == 0)
            {
                // 掃けたときだけ記録する(途中で例外なら記録せず、次の収集で掃き直す)
                if (onSwept is not null)
                {
                    await onSwept();
                }

                return byUrl.Values.ToList();
            }

            foreach (var entry in entries)
            {
                Take(entry, collectedAt, byUrl);
            }

            if (page < MaxPages && _delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }
        }

        // 上限まで読んでも空のページに当たらなかった = まだ先がある
        Truncated = true;
        if (onSwept is not null)
        {
            await onSwept();
        }

        return byUrl.Values.ToList();
    }

    void Take(DoorkeeperEventEntry entry, DateTimeOffset collectedAt, Dictionary<Uri, TechEvent> byUrl)
    {
        if (entry.StartsAt is not { } startsAt)
        {
            return;
        }

        // null(参加者数が取れていない)は残さない。この経路は「人が集まっている」ことだけを
        // 根拠に拾っているので、根拠が無いものを通すと単なる全件取り込みになる
        if (entry.ParticipantCount is not { } participants || participants < minParticipants)
        {
            return;
        }

        byUrl[entry.Url] = new TechEvent
        {
            Title = entry.Title,
            Url = entry.Url,
            // 収集元の名前は Doorkeeper と分けてある —— どの経路で入ったのかが読めないと、
            // しきい値を動かしたときの効き目を確かめられない
            SourceName = Name,
            StartsAt = startsAt,
            EndsAt = entry.EndsAt,
            Venue = entry.VenueName,
            IsOnline = VenueClassifier.IsOnline(entry.VenueName, entry.Address),
            Organizer = entry.Organizer,
            ParticipantCount = participants,
            CollectedAt = collectedAt,
            PickedBy = PickedByLabel,
            // 検索語が無いのでタグは付かない。主催者名をタグにはしない ——
            // 固有名詞が語彙へ流れ込むと、タグの一覧と LLM の仕分けがそれで埋まる
            Tags = [],
            RawTags = [],
        };
    }
}

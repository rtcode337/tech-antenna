using Microsoft.Extensions.Time.Testing;
using TechAntenna.Infrastructure.Http;

namespace TechAntenna.Tests.Infrastructure;

/// <summary>
/// 相手が示した呼び出し頻度を設計として守るための層。
/// connpass は API の利用申請のページで「5 秒に 1 リクエストを超えないよう」としている ——
/// 収集元ごとに `Task.Delay` を書く形だと、経路が増えたときに守られなくなる
/// (検索・購読・サブドメインの引き直し・面掃きが同じ相手を叩いている)。
/// </summary>
public class RequestPacingHandlerTests
{
    sealed class RecordingHandler(List<DateTimeOffset> sentAt, TimeProvider clock) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            sentAt.Add(clock.GetUtcNow());

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
            });
        }
    }

    static (HttpClient Client, List<DateTimeOffset> SentAt, FakeTimeProvider Clock) Build(
        TimeSpan interval)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero));
        var sentAt = new List<DateTimeOffset>();
        var pacing = new RequestPacingHandler(interval, clock)
        {
            InnerHandler = new RecordingHandler(sentAt, clock),
        };

        return (new HttpClient(pacing), sentAt, clock);
    }

    [Fact]
    public async Task 続けて投げても間隔が空く()
    {
        var (client, sentAt, clock) = Build(TimeSpan.FromSeconds(5));

        var first = client.GetStringAsync("https://connpass.com/api/v2/events/?ym=202608");
        await first;

        var second = client.GetStringAsync("https://connpass.com/api/v2/events/?ym=202609");
        // 待っている間は送らない。時計を進めるまで2本目は出ない
        Assert.False(second.IsCompleted);
        Assert.Single(sentAt);

        clock.Advance(TimeSpan.FromSeconds(5));
        await second;

        Assert.Equal(2, sentAt.Count);
        Assert.True(sentAt[1] - sentAt[0] >= TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task 同時に投げても間隔が守られる()
    {
        // 並列に呼ばれても破れないのがこの層を置く理由 ——
        // 収集元の側の `Task.Delay` は、別の経路が同時に叩くことを知らない
        var (client, sentAt, clock) = Build(TimeSpan.FromSeconds(5));

        var tasks = Enumerable.Range(0, 3)
            .Select(i => client.GetStringAsync($"https://connpass.com/api/v2/events/?ym=20260{i + 1}"))
            .ToList();

        Assert.Single(sentAt);

        clock.Advance(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(5));
        await Task.WhenAll(tasks);

        Assert.Equal(3, sentAt.Count);
        Assert.All(
            sentAt.Zip(sentAt.Skip(1), (a, b) => b - a),
            gap => Assert.True(gap >= TimeSpan.FromSeconds(5), $"間隔が {gap} しかない"));
    }

    [Fact]
    public async Task 十分に時間が空いていれば待たない()
    {
        var (client, sentAt, clock) = Build(TimeSpan.FromSeconds(5));

        await client.GetStringAsync("https://connpass.com/api/v2/events/?ym=202608");
        clock.Advance(TimeSpan.FromSeconds(30));
        await client.GetStringAsync("https://connpass.com/api/v2/events/?ym=202609");

        Assert.Equal(2, sentAt.Count);
    }
}

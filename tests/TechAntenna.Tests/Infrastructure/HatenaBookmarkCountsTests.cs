using TechAntenna.Infrastructure.Feeds;

namespace TechAntenna.Tests.Infrastructure;

public class HatenaBookmarkCountsTests
{
    [Fact]
    public void 応答のJSONマップをURLごとの件数として読む()
    {
        var counts = HatenaBookmarkCounts.Parse(
            """{"https://example.com/a": 111, "https://example.com/b": 0}""");

        Assert.Equal(111, counts["https://example.com/a"]);
        // はてブが知らない URL は 0 で返る(「未取得」とは呼び出し側で区別する)
        Assert.Equal(0, counts["https://example.com/b"]);
    }

    [Fact]
    public void 想定外の形の応答は空として扱う()
    {
        Assert.Empty(HatenaBookmarkCounts.Parse("[]"));
        Assert.Empty(HatenaBookmarkCounts.Parse("""{"https://example.com/a": "not-a-number"}"""));
    }

    [Fact]
    public async Task 五十件を超えるURLは複数のリクエストに分ける()
    {
        var factory = new StubHttpClientFactory("{}");
        var counts = new HatenaBookmarkCounts(factory, TimeSpan.Zero);

        var urls = Enumerable.Range(0, 120)
            .Select(i => new Uri($"https://example.com/{i}"))
            .ToList();
        await counts.FetchAsync(urls);

        // 120 URL → 50 + 50 + 20 の3リクエスト
        Assert.Equal(3, factory.RequestedUris.Count);
    }
}

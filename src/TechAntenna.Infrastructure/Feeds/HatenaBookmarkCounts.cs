using System.Text.Json;

namespace TechAntenna.Infrastructure.Feeds;

/// <summary>
/// はてなブックマーク件数取得 API で、記事 URL ごとのブックマーク数を引く。
/// キー不要・1リクエストで 50 URL まで一括。全ソース横断で使える人気の代理指標が
/// これで揃う(Qiita/Zenn のフィードは人気順の選別はあっても数値を持たない)。
///
/// 応答は <c>{"URL": 件数, …}</c> の JSON マップ。はてブが知らない URL は 0 で返るので、
/// 「未取得(null)」と「0 users(0)」は呼び出し側で区別できる。
/// </summary>
public class HatenaBookmarkCounts(
    IHttpClientFactory httpClientFactory,
    TimeSpan? delayBetweenRequests = null)
{
    public const string HttpClientName = "hatena-counts";

    const string Endpoint = "https://bookmark.hatenaapis.com/count/entries";

    /// <summary>API 仕様の上限(1リクエストの URL 数)。</summary>
    public const int BatchSize = 50;

    readonly TimeSpan _delay = delayBetweenRequests ?? TimeSpan.FromSeconds(1);

    /// <summary>URL ごとのブックマーク数を返す。応答に無かった URL はキー自体が入らない。</summary>
    public async Task<IReadOnlyDictionary<string, int>> FetchAsync(
        IReadOnlyList<Uri> urls, CancellationToken cancellationToken = default)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (urls.Count == 0)
        {
            return counts;
        }

        using var client = httpClientFactory.CreateClient(HttpClientName);

        for (var offset = 0; offset < urls.Count; offset += BatchSize)
        {
            // 無料でコミュニティに開かれている API なので、リクエストの間隔を空ける
            if (offset > 0 && _delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }

            var batch = urls.Skip(offset).Take(BatchSize);
            var query = string.Join('&',
                batch.Select(url => $"url={Uri.EscapeDataString(url.ToString())}"));
            var json = await client.GetStringAsync($"{Endpoint}?{query}", cancellationToken);

            foreach (var (url, count) in Parse(json))
            {
                counts[url] = count;
            }
        }

        return counts;
    }

    /// <summary>応答の JSON マップを読む。数値でない値(想定外)は読み飛ばす。</summary>
    public static IReadOnlyDictionary<string, int> Parse(string json)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return counts;
        }

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number
                && property.Value.TryGetInt32(out var count))
            {
                counts[property.Name] = count;
            }
        }

        return counts;
    }
}

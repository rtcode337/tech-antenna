using System.Text.Json;
using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Trends;

namespace TechAntenna.Infrastructure.Trends;

/// <summary>Qiita の直近記事に付いたタグを、いいね数で重み付けして集計する。</summary>
public class QiitaTrendTopicSource(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider) : ITrendTopicSource
{
    public const string HttpClientName = "qiita-trends";
    const string Endpoint = "https://qiita.com/api/v2/items?per_page=100&page=1";

    public string Name => "Qiita";

    public async Task<IReadOnlyList<TrendTopicCandidate>> FetchAsync(CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(Endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var cutoff = timeProvider.GetUtcNow().AddDays(-7);
        var scores = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("created_at", out var createdAtElement)
                || !DateTimeOffset.TryParse(createdAtElement.GetString(), out var createdAt)
                || createdAt < cutoff)
            {
                continue;
            }

            var likes = item.TryGetProperty("likes_count", out var likesElement)
                ? likesElement.GetInt32()
                : 0;
            var weight = Math.Max(1, likes);
            if (!item.TryGetProperty("tags", out var tags))
            {
                continue;
            }

            foreach (var tag in tags.EnumerateArray())
            {
                if (!tag.TryGetProperty("name", out var name))
                {
                    continue;
                }

                // **1つのタグ名が複数の語に割れることがある**(`AI活用,` のようにカンマ入りの
                // タグ名が実在する)。正規化が返した分だけ数える —— 以前は 1 個のときだけ
                // 数えていたので、割れた語がまるごと落ちていた
                foreach (var normalized in TagNormalizer.Normalize([name.GetString() ?? ""]))
                {
                    scores[normalized] = scores.GetValueOrDefault(normalized) + weight;
                }
            }
        }

        return scores.Select(pair => new TrendTopicCandidate(pair.Key, pair.Value, Name)).ToList();
    }
}

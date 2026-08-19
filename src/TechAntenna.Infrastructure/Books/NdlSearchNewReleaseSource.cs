using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;
using TechAntenna.Core.Topics;

namespace TechAntenna.Infrastructure.Books;

/// <summary>
/// 国立国会図書館サーチ(NDL サーチ)から最近出た本を拾う。
///
/// キーも申請も要らず、検索語も要らない —— 分類(NDC)と刊行日で引けるので、
/// トレンドの軸(収集対象の選択に依存しない)に合う。
///
/// 拾うのは既定で NDC 007(情報科学)。雑誌の号は除かない ——
/// テーマが読めるのはムック・入門書のタイトルのほうだが、それらは書誌上ただの図書なので、
/// 種別で分けずに集めてタイトルからトピックを拾う。
///
/// タグは<b>タイトルから拾ったトピック</b>(記事のフィードと同じ規則。収集元はタグを持たない)。
/// </summary>
public class NdlSearchNewReleaseSource(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    TopicCatalog? catalog = null,
    IReadOnlyList<string>? ndcCodes = null,
    int maxItems = 600,
    TimeSpan? delayBetweenPages = null) : INewReleaseSource
{
    public const string HttpClientName = "ndlsearch";

    const string Endpoint = "https://ndlsearch.ndl.go.jp/api/opensearch";

    /// <summary>1 回のリクエストで取る件数(API の上限は 500)。</summary>
    const int PageSize = 200;

    readonly IReadOnlyList<string> _ndcCodes = ndcCodes is { Count: > 0 } ? ndcCodes : ["007"];
    readonly TimeSpan _delay = delayBetweenPages ?? TimeSpan.FromSeconds(1);

    public string Name => "NDL サーチ";

    public async Task<IReadOnlyList<NewRelease>> FetchAsync(
        DateOnly since, CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);
        var collectedAt = timeProvider.GetUtcNow();
        var byUrl = new Dictionary<Uri, NewRelease>();

        foreach (var ndc in _ndcCodes)
        {
            var index = 1;
            while (byUrl.Count < maxItems)
            {
                var xml = await client.GetStringAsync(
                    RequestUri(ndc, since, index), cancellationToken);
                var entries = NdlSearchResponseParser.Parse(xml);
                if (entries.Count == 0)
                {
                    break;
                }

                foreach (var entry in entries)
                {
                    // 同じ本が複数の NDC に入っていることがある(007.6 と 547 など)
                    byUrl[entry.Url] = ToRelease(entry, collectedAt);
                }

                index += entries.Count;
                if (index > NdlSearchResponseParser.TotalResults(xml))
                {
                    break;
                }

                // 無料でコミュニティに開かれた API なので、ページの合間は間隔を空ける
                if (_delay > TimeSpan.Zero)
                {
                    await Task.Delay(_delay, cancellationToken);
                }
            }
        }

        return byUrl.Values
            .Where(release => release.PublishedOn is { } published && published >= since)
            .OrderByDescending(release => release.PublishedOn)
            .Take(maxItems)
            .ToList();
    }

    string RequestUri(string ndc, DateOnly since, int index) =>
        $"{Endpoint}?ndc={Uri.EscapeDataString(ndc)}&cnt={PageSize}&idx={index}"
        + $"&from={since:yyyy-MM-dd}";

    NewRelease ToRelease(NdlSearchEntry entry, DateTimeOffset collectedAt)
    {
        var found = (catalog ?? TopicCatalog.Empty).FindIn(entry.Title);

        return new NewRelease
        {
            Title = entry.Title,
            Url = entry.Url,
            Publisher = entry.Publisher,
            PublishedOn = entry.PublishedOn,
            SourceName = Name,
            CollectedAt = collectedAt,
            Tags = (catalog ?? TopicCatalog.Empty).Normalize(found),
            RawTags = found,
        };
    }
}

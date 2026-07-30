using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Books;

/// <summary>
/// Google Books API でキーワード検索を行う書籍カタログ。
/// API キーは任意(未設定でも検索できるが、1日あたりの上限が低くなる)。
/// </summary>
public class GoogleBooksCatalog(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    string? apiKey,
    int maxResults = 20) : IBookCatalog
{
    public const string HttpClientName = "googlebooks";

    public string Name => "Google Books";

    public async Task<IReadOnlyList<Book>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);

        // 日本語の技術書を拾いたいので言語を絞る
        var requestUri =
            $"https://www.googleapis.com/books/v1/volumes?q={Uri.EscapeDataString(keyword)}"
            + $"&maxResults={maxResults}&langRestrict=ja&orderBy=newest";
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            requestUri += $"&key={Uri.EscapeDataString(apiKey)}";
        }

        var json = await client.GetStringAsync(requestUri, cancellationToken);

        var collectedAt = timeProvider.GetUtcNow();
        return GoogleBooksResponseParser.Parse(json)
            .Where(entry => entry.Title.Length > 0)
            .Select(entry => new Book
            {
                Title = entry.Title,
                Isbn13 = entry.Isbn13,
                Authors = entry.Authors,
                Publisher = entry.Publisher,
                PublishedOn = entry.PublishedOn,
                Url = entry.Url,
                CoverUrl = entry.CoverUrl,
                SourceName = Name,
                CollectedAt = collectedAt,
                // 検索に使ったキーワードを、記事・イベントと突き合わせるためのタグにする
                Tags = TagNormalizer.Normalize([keyword]),
            })
            .ToList();
    }
}

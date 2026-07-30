using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Books;

/// <summary>
/// openBD で日本の書誌情報を補う。openBD はキーワード検索を持たず ISBN 参照専用なので、
/// カタログ検索の結果に対する後段として使う。
/// 既に値がある項目は上書きせず、欠けている項目だけを埋める。
/// </summary>
public class OpenBdEnricher(IHttpClientFactory httpClientFactory) : IBookEnricher
{
    public const string HttpClientName = "openbd";

    /// <summary>1リクエストで問い合わせる ISBN の数。無料のコミュニティ運営サービスなので控えめにする。</summary>
    const int ChunkSize = 50;

    public string Name => "openBD";

    public async Task<IReadOnlyList<Book>> EnrichAsync(
        IReadOnlyList<Book> books,
        CancellationToken cancellationToken = default)
    {
        var isbns = books
            .Select(b => b.Isbn13)
            .OfType<string>()
            .Where(isbn => isbn.Length > 0)
            .Distinct()
            .ToList();

        if (isbns.Count == 0)
        {
            return books;
        }

        using var client = httpClientFactory.CreateClient(HttpClientName);
        var byIsbn = new Dictionary<string, OpenBdEntry>();

        foreach (var chunk in isbns.Chunk(ChunkSize))
        {
            var requestUri = $"https://api.openbd.jp/v1/get?isbn={string.Join(",", chunk)}";
            var json = await client.GetStringAsync(requestUri, cancellationToken);

            foreach (var entry in OpenBdResponseParser.Parse(json))
            {
                byIsbn[entry.Isbn13] = entry;
            }
        }

        return books.Select(book => Merge(book, byIsbn)).ToList();
    }

    static Book Merge(Book book, IReadOnlyDictionary<string, OpenBdEntry> byIsbn)
    {
        if (book.Isbn13 is not { Length: > 0 } isbn
            || !byIsbn.TryGetValue(isbn, out var entry))
        {
            return book;
        }

        return new Book
        {
            Id = book.Id,
            Title = book.Title,
            Isbn13 = book.Isbn13,
            // openBD の author は「著者名／著」のような表示用の1行。
            // 分割すると崩れるので、著者が全く無いときだけそのまま1件として使う
            Authors = book.Authors.Count > 0
                ? book.Authors
                : entry.Author is { Length: > 0 } author ? [author] : [],
            Publisher = book.Publisher ?? entry.Publisher,
            PublishedOn = book.PublishedOn ?? entry.PublishedOn,
            Url = book.Url,
            CoverUrl = book.CoverUrl ?? entry.CoverUrl,
            SourceName = book.SourceName,
            CollectedAt = book.CollectedAt,
            Tags = book.Tags,
        };
    }
}

using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>書籍カタログの検索(openBD / Google Books 等)。</summary>
public interface IBookCatalog
{
    string Name { get; }

    Task<IReadOnlyList<Book>> SearchAsync(string keyword, CancellationToken cancellationToken = default);
}

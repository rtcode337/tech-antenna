using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>記事の収集元(RSS / Atom フィード等)。</summary>
public interface IArticleSource
{
    string Name { get; }

    Task<IReadOnlyList<Article>> FetchAsync(CancellationToken cancellationToken = default);
}

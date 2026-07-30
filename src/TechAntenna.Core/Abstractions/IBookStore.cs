using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>収集した書籍の保存先。</summary>
public interface IBookStore
{
    /// <summary>書籍を追加する。ISBN-13(無ければ URL)が既存と重複するものは無視し、実際に追加した件数を返す。</summary>
    Task<int> AddRangeAsync(IEnumerable<Book> books, CancellationToken cancellationToken = default);

    /// <summary>収集日時の新しい順に最大 <paramref name="count"/> 件返す。</summary>
    Task<IReadOnlyList<Book>> GetRecentAsync(int count, CancellationToken cancellationToken = default);
}

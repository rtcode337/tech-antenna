using TechAntenna.Core.Models;

namespace TechAntenna.Core.Abstractions;

/// <summary>
/// 書籍の書誌情報を別のソースで補う。
/// キーワード検索を持つカタログ(Google Books)と、ISBN 参照専用だが
/// 日本の書誌情報が充実したソース(openBD)を組み合わせるために分けている。
/// </summary>
public interface IBookEnricher
{
    string Name { get; }

    /// <summary>
    /// 渡された書籍の情報を補って返す。補える情報が無い書籍はそのまま返す。
    /// 件数と順序は入力と同じ。
    /// </summary>
    Task<IReadOnlyList<Book>> EnrichAsync(IReadOnlyList<Book> books, CancellationToken cancellationToken = default);
}

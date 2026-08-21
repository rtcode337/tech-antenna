using TechAntenna.Core.Abstractions;

namespace TechAntenna.Web.Services;

/// <summary>
/// 保存済みの本の ISBN → 書影 URL。
///
/// ISBN しか分かっていない本(推薦・引用で見つけた本)は、収集のたびにまっさらな
/// <c>Book</c> として組み立て直すことになる。そのまま補完へ渡すと<b>毎回全冊ぶん</b>
/// Google Books へ書影を問い合わせる —— 1 冊 1 リクエストで無料枠は 1 日 1,000 なので、
/// 冊数が増えるとそれだけで枠が尽きる。一度埋まった本は二度と引かないよう、
/// 保存済みの書影を引き継いでから補完へ渡す。
/// </summary>
public static class KnownCovers
{
    /// <summary>読み込む冊数の上限。ISBN から起こす本は多くても数百冊。</summary>
    public const int BookLimit = 2000;

    public static async Task<IReadOnlyDictionary<string, Uri?>> LoadAsync(
        IBookStore store, CancellationToken cancellationToken = default)
    {
        var stored = await store.GetRecentAsync(BookLimit, cancellationToken);

        return stored
            .Where(book => book.CoverUrl is not null && book.Isbn13 is { Length: > 0 })
            .GroupBy(book => book.Isbn13!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().CoverUrl, StringComparer.Ordinal);
    }
}

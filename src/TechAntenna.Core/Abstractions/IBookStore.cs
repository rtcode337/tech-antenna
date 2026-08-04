using TechAntenna.Core.Models;

using TechAntenna.Core.Topics;

namespace TechAntenna.Core.Abstractions;

/// <summary>収集した書籍の保存先。</summary>
public interface IBookStore
{
    /// <summary>
    /// 書籍を追加し、**実際に追加した件数**を返す。重複判定は <see cref="BookKey"/>。
    ///
    /// 既にある本は書誌情報を上書きしないが、**タグだけは足す**(<see cref="BookMerge"/>)。
    /// 書籍はトピックごとに検索するので 1 冊が複数のトピックで見つかる。捨てるだけだと
    /// 最初のトピックにしか出てこない。
    /// </summary>
    Task<int> AddRangeAsync(IEnumerable<Book> books, CancellationToken cancellationToken = default);

    /// <summary>収集日時の新しい順に最大 <paramref name="count"/> 件返す。</summary>
    Task<IReadOnlyList<Book>> GetRecentAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>タグ <paramref name="tag"/> が付いたものを収集日時の新しい順に最大 <paramref name="count"/> 件返す。</summary>
    Task<IReadOnlyList<Book>> GetByTagAsync(string tag, int count, CancellationToken cancellationToken = default);

    /// <summary>タグごとの件数を返す。</summary>
    Task<IReadOnlyList<TagCount>> GetTagCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存済みの生タグ(<c>RawTags</c>)から <c>Tags</c> を作り直し、更新した件数を返す。
    /// 正規化の規則やストップワードを変えたときに、過去のデータを追従させるために使う。
    /// </summary>
    Task<int> RenormalizeTagsAsync(TopicCatalog catalog, CancellationToken cancellationToken = default);
}

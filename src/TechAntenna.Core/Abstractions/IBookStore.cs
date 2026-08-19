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

    /// <summary>
    /// 「読んだ」の印を裏返す。**返すのは裏返した後の状態**で、本が無ければ null。
    ///
    /// 収集(<see cref="AddRangeAsync"/>)とは別の経路にしてある —— 読んだかどうかは
    /// 外から取れる情報ではなく本人の記録なので、収集の合流に混ぜない。
    ///
    /// **「立てる / 下ろす」ではなく「裏返す」なのは画面の都合。** 静的 SSR のボタンは
    /// 押した値(`toggle:&lt;Id&gt;`)で位置を復元する(<c>keep-focus.js</c>)ので、
    /// 立てる・下ろすで値が変わると POST 後に同じボタンを見つけられず、
    /// 長い一覧の先頭へ戻ってしまう。**今の状態を知っているのは保存先**なので、
    /// 裏返す判断もここに置く。
    ///
    /// <paramref name="now"/> を呼び出し側から渡すのは、時刻の出どころを1つにするため
    /// (タグの仕分けと同じ流儀)。
    /// </summary>
    Task<bool?> ToggleReadAsync(
        Guid id, DateTimeOffset now, CancellationToken cancellationToken = default);

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

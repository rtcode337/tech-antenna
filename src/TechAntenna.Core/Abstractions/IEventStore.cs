using TechAntenna.Core.Models;

using TechAntenna.Core.Topics;

namespace TechAntenna.Core.Abstractions;

/// <summary>収集したイベントの保存先。</summary>
public interface IEventStore
{
    /// <summary>イベントを追加する。URL が既存と重複するものは無視し、実際に追加した件数を返す。</summary>
    Task<int> AddRangeAsync(IEnumerable<TechEvent> events, CancellationToken cancellationToken = default);

    /// <summary><paramref name="from"/> 以降に開催されるイベントを開始日時の早い順に最大 <paramref name="count"/> 件返す。</summary>
    Task<IReadOnlyList<TechEvent>> GetUpcomingAsync(DateTimeOffset from, int count, CancellationToken cancellationToken = default);

    /// <summary>タグ <paramref name="tag"/> が付いたものを開始日時の早い順に最大 <paramref name="count"/> 件返す。</summary>
    Task<IReadOnlyList<TechEvent>> GetByTagAsync(string tag, int count, CancellationToken cancellationToken = default);

    /// <summary>タグごとの件数を返す。</summary>
    Task<IReadOnlyList<TagCount>> GetTagCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存済みの生タグ(<c>RawTags</c>)から <c>Tags</c> を作り直し、更新した件数を返す。
    /// 正規化の規則やストップワードを変えたときに、過去のデータを追従させるために使う。
    /// </summary>
    Task<int> RenormalizeTagsAsync(TopicCatalog catalog, CancellationToken cancellationToken = default);
}

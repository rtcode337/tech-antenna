using TechAntenna.Core.Models;

using TechAntenna.Core.Topics;

namespace TechAntenna.Core.Abstractions;

/// <summary>収集したイベントの保存先。</summary>
public interface IEventStore
{
    /// <summary>
    /// イベントを追加する。URL が既存と重複するものは書誌にあたる情報を上書きせず、
    /// **主催者と参加者数だけ取り込む**(参加者数は開催が近づくほど増えるため)。
    /// 返すのは実際に追加した件数。
    /// </summary>
    Task<int> AddRangeAsync(IEnumerable<TechEvent> events, CancellationToken cancellationToken = default);

    /// <summary><paramref name="from"/> 以降に開催されるイベントを開始日時の早い順に最大 <paramref name="count"/> 件返す。</summary>
    Task<IReadOnlyList<TechEvent>> GetUpcomingAsync(DateTimeOffset from, int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// <paramref name="from"/> 以上 <paramref name="to"/> 未満に開催されるイベントを
    /// 開始日時の早い順に返す。カレンダー(月表示)のように<b>過ぎたものも含めて</b>
    /// 期間で切り出したいときに使う。
    /// </summary>
    Task<IReadOnlyList<TechEvent>> GetInRangeAsync(
        DateTimeOffset from, DateTimeOffset to, int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// 主催者ごとのイベント件数を多い順に返す(主催者が取れていないものは含めない)。
    /// 公式の名簿(設定 → 主催者)を、実際に集まっている主催者名を見ながら直すために使う。
    /// </summary>
    Task<IReadOnlyList<OrganizerCount>> GetOrganizerCountsAsync(CancellationToken cancellationToken = default);

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

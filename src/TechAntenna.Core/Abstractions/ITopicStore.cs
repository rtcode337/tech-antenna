using TechAntenna.Core.Topics;

namespace TechAntenna.Core.Abstractions;

/// <summary>トピック一覧の保存先。</summary>
public interface ITopicStore
{
    /// <summary>
    /// トピックを追加・更新する。**削除はしない** ——
    /// 一覧から一時的に消えたトピックを消すと、選択済み(<see cref="StoredTopic.IsSelected"/>)まで
    /// 失われて収集が止まるため。今回現れなかったトピックは話題度を 0 にするだけにする。
    /// </summary>
    Task UpsertAsync(
        IReadOnlyList<TopicUpdate> topics,
        DateTimeOffset collectedAt,
        CancellationToken cancellationToken = default);

    /// <summary>話題度順のトピックを最大 <paramref name="count"/> 件返す。</summary>
    Task<IReadOnlyList<StoredTopic>> GetTopicsAsync(
        int count,
        CancellationToken cancellationToken = default);

    /// <summary>収集キーワードとして使う選択済みトピックを更新する。</summary>
    Task UpdateSelectionAsync(IReadOnlyList<string> tags, CancellationToken cancellationToken = default);

    /// <summary>
    /// 収集キーワードとして選択されたトピックを返す。
    ///
    /// **キーと正式表記の両方を返す**。用途が 2 つあり、どちらか一方では足りないため:
    /// connpass や Google Books へ投げる<b>検索語</b>には元の表記(`生成AI`)が要るが、
    /// 集めた記事のタグとの<b>突き合わせ</b>には正規化済みのキー(`生成ai`)が要る。
    /// </summary>
    Task<IReadOnlyList<SelectedTopic>> GetSelectedAsync(CancellationToken cancellationToken = default);
}

/// <summary>収集対象に選ばれたトピック。</summary>
/// <param name="Tag">突き合わせキー(記事のタグと比べる用)。</param>
/// <param name="Display">正式表記(外部 API へ投げる検索語)。</param>
public record SelectedTopic(string Tag, string Display);

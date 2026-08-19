using TechAntenna.Core.Topics;

namespace TechAntenna.Core.Abstractions;

/// <summary>収集対象に選ばれたトピック。</summary>
/// <param name="Key">突き合わせキー(記事のタグと比べる用)。</param>
/// <param name="Display">正式表記(外部 API へ投げる検索語)。</param>
/// <param name="English">英語圏の収集元へ投げる検索語。無ければ null(正式表記を使う)。</param>
public record SelectedTopic(string Key, string Display, string? English = null);

/// <summary>
/// 語彙(トピック)の保存先。タグは <see cref="ITagStore"/> の側で、ここには
/// 精査で昇格したものだけが入る。
/// </summary>
public interface ITopicStore
{
    /// <summary>
    /// トピックを追加・更新する。`IsSelected` は触らない(選択は画面の操作だけが変える)。
    /// <paramref name="upsert"/> に含まれないトピックは、件数と話題度を 0 にするだけで消さない
    /// —— 消すと選択ごと失われて収集が止まる。
    /// </summary>
    Task UpsertAsync(
        IReadOnlyList<Topic> topics,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// トピックを全件返す。並びは<b>選択済み → 配下込みの話題度 → キー</b>。
    /// 語彙は数百件なので、画面もここから全部受け取って絞り込む。
    /// </summary>
    Task<IReadOnlyList<Topic>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 1 件だけ追加・更新する。<see cref="UpsertAsync"/> と違って他の行に触らない ——
    /// 画面からの手直し(タグをトピックに昇格させる等)で 1 件だけ入れたいときに使う
    /// (Upsert は渡されなかった行の件数と話題度を 0 にするので、この用途には使えない)。
    /// </summary>
    Task SaveAsync(Topic topic, DateTimeOffset updatedAt, CancellationToken cancellationToken = default);

    /// <summary>キーで 1 件引く。無ければ null。</summary>
    Task<Topic?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// トピックを消す。選択済みは消さない(収集キーワードごと失われるため)。
    /// 実際に消した件数を返す。
    /// </summary>
    Task<int> RemoveAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default);

    /// <summary>収集キーワードとして使う選択済みトピックを更新する(渡された分で置き換える)。</summary>
    Task UpdateSelectionAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// 1 件だけ収集対象の入り切りを切り替える。<see cref="UpdateSelectionAsync"/> と違って
    /// 他の行に触らない —— 画面のチェックボックスをその場で保存する入口
    /// (<c>POST /api/topics/select</c>)で使う。一覧に出ていない行の選択を、
    /// 1 個の操作の巻き添えで落とさないため。
    /// そのトピックがあったか(= 実際に切り替えたか)を返す。
    /// </summary>
    Task<bool> SetSelectedAsync(string key, bool selected, CancellationToken cancellationToken = default);

    /// <summary>
    /// 収集キーワードとして選択されたトピックを返す。
    ///
    /// キーと表記の両方を返す。用途が 2 つあり、どちらか一方では足りないため:
    /// connpass や Google Books へ投げる<b>検索語</b>には表記(`生成AI`。英語圏には
    /// `generative ai`)が要るが、集めた記事のタグとの<b>突き合わせ</b>には
    /// 正規化済みのキー(`生成ai`)が要る。
    /// </summary>
    Task<IReadOnlyList<SelectedTopic>> GetSelectedAsync(CancellationToken cancellationToken = default);
}

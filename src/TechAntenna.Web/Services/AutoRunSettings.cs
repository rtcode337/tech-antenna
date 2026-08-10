namespace TechAntenna.Web.Services;

/// <summary>
/// 定期実行のオン/オフの設定キー(ApiCredentials に渡す名前)。**既定はすべて無効**で、
/// 画面(設定)のチェックボックスで切り替える —— 消し忘れたサーバーが収集先を叩き続けたり、
/// LLM や外部 API の無料枠を使い切ったりしないための既定。
/// 実行時に毎回読むので、切り替えは再起動なしで次の周回から効く。
/// </summary>
public static class AutoRunSettings
{
    /// <summary>トレンドの収集(記事・ニュース・話題の論文)とイベントの収集。</summary>
    public const string CollectionName = "Collection:AutoRun";

    /// <summary>書籍の収集。</summary>
    public const string BooksName = "Books:AutoRun";

    /// <summary>記事の要約。</summary>
    public const string SummaryName = "Summary:AutoRun";

    /// <summary>今日のサマリーの生成。</summary>
    public const string DigestName = "Digest:AutoRun";

    /// <summary>有効か(既定は無効。明示的に "true" にしたときだけ動く)。</summary>
    public static bool IsEnabled(ApiCredentials credentials, string name) =>
        credentials.Get(name) == "true";
}

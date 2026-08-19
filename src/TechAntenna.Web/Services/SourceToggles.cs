namespace TechAntenna.Web.Services;

/// <summary>
/// 収集元1つ1つのオン/オフ。止めたものは収集のときに叩きに行かない。
///
/// 値は定期実行のチェック・名簿・面掃きと同じく DB(<c>Secrets</c>)に持つので、
/// 再起動なしで効き、コンテナを作り直しても残る。環境変数では設定できない
/// (<see cref="ScheduleSettings"/>・<see cref="SweepSettings"/> と同じ扱い)。
///
/// <b>既定は有効。</b> 面掃き(<see cref="SweepSettings"/>)と逆に、止めたものだけを保存する
/// —— 収集元は普通に使うものなので、既定を無効にすると新しい収集元を足すたびに
/// 「入れたのに集まらない」が起きる。行が無い = 有効。
///
/// <b>鍵は「役割 + 名前」。</b> 名前だけでは足りない —— `Qiita` は推薦本(定番の書籍)と
/// 話題度(トピック)の両方にあり、同じ名前で別の収集元になる。役割で名前空間を分ける。
/// </summary>
public class SourceToggles(ApiCredentials credentials)
{
    /// <summary>設定パスの接頭辞。止めたものだけがこの名前で保存される。</summary>
    public const string Prefix = "Collection:Disabled:";

    // 役割。ランナーが回す抽象1つにつき1つで、画面(IntegrationCatalog)と同じ値を使う
    public const string Article = "article";
    public const string Paper = "paper";
    public const string Event = "event";
    public const string Book = "book";
    public const string Enricher = "enricher";
    public const string Recommendation = "recommendation";
    public const string NewRelease = "newrelease";
    public const string Trend = "trend";
    public const string Bookmark = "bookmark";

    /// <summary>設定パス。画面の切り替えボタンにもこの値が乗る。</summary>
    public static string KeyOf(string role, string name) => $"{Prefix}{role}:{name}";

    /// <summary>この設定パスが収集元のオン/オフか(面掃きの設定と見分ける)。</summary>
    public static bool IsSourceKey(string name) => name.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>いま有効か。行が無ければ有効(止めたものだけを保存しているため)。</summary>
    public bool IsEnabled(string role, string name) => !credentials.Has(KeyOf(role, name));

    /// <summary>設定パスから直接引く(画面の一覧用)。</summary>
    public bool IsEnabledByKey(string key) => !credentials.Has(key);

    /// <summary>画面の切り替え。止めるときだけ値を書き、動かすときは<b>行ごと消す</b>。</summary>
    public Task SetAsync(string key, bool enabled, CancellationToken cancellationToken = default) =>
        enabled
            ? credentials.RemoveAsync(key, cancellationToken)
            : credentials.SetAsync(key, "true", cancellationToken);

    /// <summary>
    /// 止めていない収集元だけを返す。実行のたびに読む ——
    /// 起動時に絞ると、画面で切り替えても再起動するまで効かない。
    /// </summary>
    public IReadOnlyList<T> Enabled<T>(IEnumerable<T> sources, string role, Func<T, string> nameOf) =>
        sources.Where(source => IsEnabled(role, nameOf(source))).ToList();
}

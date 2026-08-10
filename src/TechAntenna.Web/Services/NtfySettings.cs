namespace TechAntenna.Web.Services;

/// <summary>
/// ntfy 通知の設定キー(ApiCredentials に渡す名前)。接続先(BaseUrl / Topic / トークン)は
/// 外部連携の画面から設定し、**通知のオン/オフはそれとは独立に**設定画面で切り替える ——
/// 接続先を消さなくても一時的に通知を止められるようにするため。
/// </summary>
public static class NtfySettings
{
    public const string BaseUrlName = "Ntfy:BaseUrl";

    public const string TopicName = "Ntfy:Topic";

    public const string AccessTokenName = "Ntfy:AccessToken";

    /// <summary>通知のオン/オフ("false" で無効)。未設定は有効(接続先があれば通知する)。</summary>
    public const string EnabledName = "Ntfy:Enabled";

    /// <summary>BaseUrl 未設定のときの既定(公式のホスティング)。</summary>
    public const string DefaultBaseUrl = "https://ntfy.sh";

    /// <summary>通知が有効か(既定は有効。明示的に "false" にしたときだけ止まる)。</summary>
    public static bool IsEnabled(ApiCredentials credentials) =>
        credentials.Get(EnabledName) != "false";

    /// <summary>通知先が設定されているか(トピックがあれば足りる。BaseUrl は既定がある)。</summary>
    public static bool IsConfigured(ApiCredentials credentials) =>
        credentials.Has(TopicName);
}

using System.Security.Cryptography;

namespace TechAntenna.Web.Services;

/// <summary>
/// ntfy 通知の設定キー(ApiCredentials に渡す名前)。接続先(BaseUrl / Topic / トークン)は
/// 外部連携の画面から設定し、通知のオン/オフはそれとは独立に設定画面で切り替える ——
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

    /// <summary>生成するトピック名の頭に付ける印。ntfy のアプリに複数の購読が並んだとき、
    /// どれがこのアプリのものか分かるようにするため(推測されにくさは後ろの乱数で持つ)。</summary>
    const string TopicPrefix = "tech-antenna-";

    /// <summary>生成する乱数部分の文字数。33 種から 16 文字 = 約 80 bit。</summary>
    const int TopicRandomLength = 16;

    /// <summary>
    /// 乱数部分に使う文字。ntfy のトピック名に使えるのは <c>[-_A-Za-z0-9]</c> だが、
    /// 見間違えやすい文字(l・1・I・0・O)を外した小文字と数字だけにしてある ——
    /// 購読する端末へ手で打ち写すことがあるため。
    /// </summary>
    const string TopicAlphabet = "abcdefghijkmnopqrstuvwxyz23456789";

    /// <summary>
    /// ランダムなトピック名を作る。ntfy.sh のトピック名は知っている人が誰でも購読・投稿
    /// できるので、推測されない名前が要る。暗号論的乱数(<see cref="RandomNumberGenerator"/>)
    /// で作り、<c>Random</c> は使わない —— 種が推測できると名前も推測できるため。
    /// </summary>
    public static string GenerateTopic() =>
        TopicPrefix + RandomNumberGenerator.GetString(TopicAlphabet, TopicRandomLength);
}

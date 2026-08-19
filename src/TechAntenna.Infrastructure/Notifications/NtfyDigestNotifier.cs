using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TechAntenna.Core;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Notifications;

/// <summary>ntfy の通知先(送信のたびに解決される)。</summary>
/// <param name="BaseUrl">ntfy サーバーのベース URL。</param>
/// <param name="Topic">通知を送るトピック名。</param>
/// <param name="AccessToken">Bearer 認証のトークン(認証なしのサーバーでは null)。</param>
/// <param name="ClickUrl">通知をタップしたときに開く URL(未設定なら null)。</param>
public record NtfyTarget(string BaseUrl, string Topic, string? AccessToken, string? ClickUrl);

/// <summary>
/// ダイジェストを ntfy へ送る。JSON publish(ベース URL への POST に topic を含める)を
/// 使うのは、タイトルをヘッダ(X-Title)で渡すと非 ASCII に RFC 2047 エンコードが要るため ——
/// JSON ボディなら日本語のタイトルをそのまま書ける。
///
/// 認証はアクセストークンがあるときだけ Bearer で送る(セルフホストの ntfy は
/// 認証なしのことも多い)。
///
/// 通知先は送信のたびに <paramref name="targetProvider"/> から解決する —— 接続先は
/// 画面から設定でき、通知のオン/オフも独立に切り替えられるため、起動時の値を固定しない。
/// null が返ったら(未設定・無効)送らずに false を返す。
/// </summary>
public class NtfyDigestNotifier(
    IHttpClientFactory httpClientFactory,
    Func<NtfyTarget?> targetProvider) : IDigestNotifier
{
    public const string HttpClientName = "ntfy";

    /// <summary>
    /// 本文の上限。ntfy の既定のメッセージ上限(4096 バイト)に UTF-8 の日本語
    /// (1文字3バイト)で収まる長さ。超えた分は切って「…」を付ける。
    /// </summary>
    public const int MaxMessageChars = 1200;

    public string Name => "ntfy";

    public async Task<bool> NotifyAsync(Digest digest, CancellationToken cancellationToken = default)
    {
        var target = targetProvider();
        if (target is null)
        {
            return false;
        }

        using var client = httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, target.BaseUrl)
        {
            Content = JsonContent.Create(new
            {
                topic = target.Topic,
                // 守備範囲をタイトルに入れる。1回の生成で2通届くので、
                // 通知面(本文が畳まれた状態)でどちらのサマリーか見分けられないと読み分けられない
                title = $"今日のサマリー・{digest.Scope.Label()}"
                    + $"({JapanTime.FormatShort(digest.GeneratedAt)} 生成)",
                message = BuildMessage(digest),
                // 絵文字も範囲で変える(ntfy は tag を絵文字にして通知の頭に出す)
                tags = new[] { digest.Scope == DigestScope.Interests ? "dart" : "newspaper" },
                // click はホームの URL(設定されているときだけ)。アプリは自分の公開 URL を
                // 知らないので、設定から渡してもらう
                click = string.IsNullOrWhiteSpace(target.ClickUrl) ? null : target.ClickUrl,
            }, options: new JsonSerializerOptions
            {
                // 未設定の click を "click": null で送らない(値のあるキーだけにする)
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            }),
        };
        if (!string.IsNullOrWhiteSpace(target.AccessToken))
        {
            request.Headers.Authorization = new("Bearer", target.AccessToken);
        }

        var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>
    /// 通知の本文を組む。導入 → 項目(見出し+本文+出典)→ 署名の順で、スマホの通知でも
    /// 読める素のテキストにする(ntfy に Markdown 表示はあるが、既定の Android/iOS の
    /// 通知面ではただの文字列なので装飾に頼らない)。
    /// </summary>
    public static string BuildMessage(Digest digest)
    {
        var builder = new StringBuilder();
        if (digest.Lead is { Length: > 0 })
        {
            builder.AppendLine(digest.Lead);
        }

        foreach (var item in digest.Items)
        {
            builder.AppendLine();
            builder.AppendLine($"● {item.Title}");
            builder.AppendLine(item.Body);
            if (item.Url is { Length: > 0 })
            {
                builder.AppendLine(item.Url);
            }
        }

        var message = builder.ToString().Trim();
        if (message.Length > MaxMessageChars)
        {
            message = message[..MaxMessageChars] + "…";
        }

        // 署名は切り詰めの後に足す。複数の AI に書かせていると「どれが書いたものか」が
        // 分からないと読み比べられず、長い日にだけ署名が消えるのでは意味が無い
        return $"{message}\n\n— {digest.GeneratorName}";
    }
}

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TechAntenna.Core.Abstractions;
using TechAntenna.Core.Models;

namespace TechAntenna.Infrastructure.Notifications;

/// <summary>
/// ダイジェストを ntfy へ送る。**JSON publish**(ベース URL への POST に topic を含める)を
/// 使うのは、タイトルをヘッダ(X-Title)で渡すと非 ASCII に RFC 2047 エンコードが要るため ——
/// JSON ボディなら日本語のタイトルをそのまま書ける。
///
/// 認証はアクセストークンがあるときだけ Bearer で送る(セルフホストの ntfy は
/// 認証なしのことも多い)。トークンの実値はコミットせず環境変数で渡す。
/// </summary>
public class NtfyDigestNotifier(
    IHttpClientFactory httpClientFactory,
    string baseUrl,
    string topic,
    string? accessToken,
    string? clickUrl) : IDigestNotifier
{
    public const string HttpClientName = "ntfy";

    /// <summary>
    /// 本文の上限。ntfy の既定のメッセージ上限(4096 バイト)に UTF-8 の日本語
    /// (1文字3バイト)で収まる長さ。超えた分は切って「…」を付ける。
    /// </summary>
    public const int MaxMessageChars = 1200;

    public string Name => "ntfy";

    public async Task NotifyAsync(Digest digest, CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl)
        {
            Content = JsonContent.Create(new
            {
                topic,
                title = $"今日のサマリー({digest.GeneratedAt:M/d HH:mm} 生成)",
                message = BuildMessage(digest),
                tags = new[] { "newspaper" },
                // click はホームの URL(設定されているときだけ)。アプリは自分の公開 URL を
                // 知らないので、設定から渡してもらう
                click = string.IsNullOrWhiteSpace(clickUrl) ? null : clickUrl,
            }, options: new JsonSerializerOptions
            {
                // 未設定の click を "click": null で送らない(値のあるキーだけにする)
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            }),
        };
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new("Bearer", accessToken);
        }

        var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// 通知の本文を組む。導入 → 項目(見出し+本文+出典)の順で、スマホの通知でも
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
        return message.Length <= MaxMessageChars ? message : message[..MaxMessageChars] + "…";
    }
}

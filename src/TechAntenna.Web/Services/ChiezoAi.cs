using Microsoft.Extensions.Options;
using TechAntenna.Infrastructure.Chiezo;

namespace TechAntenna.Web.Services;

/// <summary>
/// Chiezo(LAN 内の知識サーバー)への入口。**設定されているかの判定と、
/// クライアントの組み立てを1か所にまとめる** —— 設定画面と LLM ゲートウェイの
/// 両方が要るので、URL の読み方が2か所に散らないようにする。
/// </summary>
public class ChiezoAi(IHttpClientFactory httpClientFactory, IOptions<ChiezoOptions> options)
{
    /// <summary>URL が設定されているか(環境変数 <c>Chiezo__BaseUrl</c>)。</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.BaseUrl);

    public string BaseUrl => options.Value.BaseUrl;

    /// <summary>未設定なら null(画面は設定の仕方を出す)。</summary>
    public ChiezoAiClient? Client() => IsConfigured
        ? new ChiezoAiClient(
            httpClientFactory, options.Value.BaseUrl, TimeSpan.FromSeconds(options.Value.TimeoutSeconds))
        : null;
}

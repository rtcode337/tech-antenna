using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TechAntenna.Core.Abstractions;

namespace TechAntenna.Infrastructure.Bridge;

/// <summary>
/// chiezo-bridge(Claude Code の CLI を OpenAI 互換の口に見せるサイドカー)へ HTTP で頼む。
///
/// **CLI を同居させない**ためのクラス。以前はこのアプリのイメージに claude を入れて
/// プロセス起動していたが、CLI の実体だけで 100MB 超あり、版を上げるたびにイメージを
/// 焼き直すことになる。ブリッジは chiezo リポジトリの公開イメージなので、
/// CLI の更新はそちらのコンテナを入れ替えるだけで済む。
///
/// **構造化出力(<c>--json-schema</c>)は通らない。** ブリッジが CLI に渡すのは
/// <c>--output-format text</c> で、OpenAI 互換の口にスキーマを載せる項目が無いため ——
/// JSON が欲しい呼び出しはプロンプトで指示して読み取る(<see cref="Summarization.ClaudeCodeBatch"/>)。
/// </summary>
public class CliBridgeClient(
    IHttpClientFactory httpClientFactory,
    string baseUrl,
    string? model,
    TimeSpan timeout) : ICliBridge
{
    public const string HttpClientName = "cli-bridge";

    /// <summary>
    /// 道具を引く往復の上限。**道具は使わせない**(ブリッジ側で組み込みの道具を塞ぎ、
    /// MCP も繋がない設定で動かす)ので 1 回で答えが返るが、CLI が内部で 1 往復使う
    /// 場合に備えて 2 にしてある。使わなければ増えない。
    /// </summary>
    const int MaxTurns = 2;

    // モデル名まで画面に出す —— どのモデルがサブスク枠を使っているか見えるようにする。
    // model が null(CLI の既定に任せる)ときは、既定が何かこちらから分からないので付けない
    public string Name => model is null ? "Claude Code" : $"Claude Code / {model}";

    record Message(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    record ChatRequest(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<Message> Messages,
        [property: JsonPropertyName("chiezo_max_turns")] int MaxTurns,
        [property: JsonPropertyName("chiezo_timeout")] double TimeoutSeconds);

    public async Task<string> RunAsync(
        string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var request = new ChatRequest(
            string.IsNullOrWhiteSpace(model) ? null : model,
            [new Message("system", systemPrompt), new Message("user", userPrompt)],
            MaxTurns,
            timeout.TotalSeconds);

        using var client = httpClientFactory.CreateClient(HttpClientName);
        // **こちらの待ちはブリッジより長くする。** 先に切れると「ブリッジが何秒で諦めたか」が
        // 分からなくなる(向こうは 504 と経過秒数を返してくれる)
        client.Timeout = timeout + TimeSpan.FromSeconds(30);

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(Endpoint(baseUrl), request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient の打ち切りは TaskCanceledException で来る(呼び出し側の中断とは別)
            throw new TimeoutException(
                $"CLI ブリッジが {client.Timeout.TotalSeconds:0} 秒で応答しなかった({baseUrl})。");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"CLI ブリッジに繋がらない({baseUrl})。コンテナが動いているか確認する。", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.GatewayTimeout)
            {
                throw new TimeoutException(
                    $"CLI ブリッジが {timeout.TotalSeconds:0} 秒で打ち切った: {Excerpt(body)}");
            }

            if (!response.IsSuccessStatusCode)
            {
                // 401 は「ブリッジがトークンを読めていない」。共有ディレクトリの設定 DB を
                // 書けていないか、ブリッジのマウント先が違う(理由はブリッジの本文に入る)
                throw new InvalidOperationException(
                    $"CLI ブリッジがエラーを返した(HTTP {(int)response.StatusCode}): {Excerpt(body)}");
            }

            return ReadContent(body);
        }
    }

    /// <summary>末尾のスラッシュを気にせず <c>/chat/completions</c> に繋ぐ。</summary>
    static string Endpoint(string baseUrl) => baseUrl.TrimEnd('/') + "/chat/completions";

    static string ReadContent(string body)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new FormatException("CLI ブリッジの応答が JSON として読めない。", ex);
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0
                || !choices[0].TryGetProperty("message", out var message)
                || !message.TryGetProperty("content", out var content)
                || content.GetString()?.Trim() is not { Length: > 0 } text)
            {
                throw new FormatException("CLI ブリッジが空の応答を返した。");
            }

            return text;
        }
    }

    /// <summary>例外メッセージにそのまま載せられる長さに切る。</summary>
    static string Excerpt(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500] + "…";
    }
}

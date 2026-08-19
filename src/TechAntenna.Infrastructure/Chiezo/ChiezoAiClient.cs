using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TechAntenna.Infrastructure.Chiezo;

/// <summary>
/// Chiezo に登録してある AI(相手)の1つ。画面の選択肢を組むのに必要なぶんだけを持つ。
/// </summary>
/// <param name="Id">相手の識別子(`gemini`・`claude` など)。</param>
/// <param name="Label">画面に出す名前。</param>
/// <param name="Models">選べるモデル(相手に聞けたときはその答え)。</param>
/// <param name="Efforts">選べる考える量。空なら画面に出さない(その相手には無い)。</param>
/// <param name="ModelRequired">モデルの指定が必須か。false なら「既定に任せる」を選べる。</param>
public record ChiezoBackend(
    string Id,
    string Label,
    IReadOnlyList<string> Models,
    IReadOnlyList<string> Efforts,
    bool ModelRequired);

/// <summary>Chiezo が1回の問い合わせで返したもの。</summary>
/// <param name="Content">応答の本文。</param>
/// <param name="Model">実際に使われたモデル。「相手の既定に任せる」で頼んだときに、
/// 何が書いたのかを知る唯一の手がかり(こちらは名前を指定していないため)。</param>
public record ChiezoCompletion(string Content, string? Model);

/// <summary>
/// Chiezo(LAN 内の知識サーバー)の「素の問い合わせ」の口を叩く。
///
/// 鍵を持たずに複数の AI を使えるようにするための経路。Gemini・Claude Code・
/// 推論サーバ…といった相手の認証情報は Chiezo が握っていて、こちらは
/// 「どの相手に投げるか」を指定するだけでよい(同梱の CLI は
/// Claude Code 1 つしか包めない)。
///
/// `/v1/chat` ではなく `/v1/ai/complete` を使う。あちらは知識ベースを引いて答える口で、
/// 必ず抽出が混ざる —— こちらは材料もプロンプトも自前で持っているので邪魔になる。
/// </summary>
public class ChiezoAiClient(IHttpClientFactory httpClientFactory, string baseUrl, TimeSpan timeout)
{
    public const string HttpClientName = "chiezo-ai";

    /// <summary>設定画面が一覧を引くときの待ち(生成より短くてよい)。</summary>
    static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(15);

    public string BaseUrl { get; } = baseUrl.TrimEnd('/');

    record BackendsResponse(
        [property: JsonPropertyName("backends")] IReadOnlyList<BackendEntry>? Backends);

    record BackendEntry(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("label")] string? Label,
        [property: JsonPropertyName("models")] IReadOnlyList<string>? Models,
        [property: JsonPropertyName("efforts")] IReadOnlyList<string>? Efforts,
        [property: JsonPropertyName("model_required")] bool ModelRequired);

    record CompleteRequest(
        [property: JsonPropertyName("backend")] string Backend,
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("effort")] string? Effort,
        [property: JsonPropertyName("messages")] IReadOnlyList<Message> Messages);

    record Message(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    record CompleteResponse(
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("model")] string? Model);

    /// <summary>いま話せる相手の一覧。Chiezo に繋がらなければ例外(画面が理由を出す)。</summary>
    public async Task<IReadOnlyList<ChiezoBackend>> GetBackendsAsync(
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(ListTimeout);
        using var response = await SendAsync(
            () => client.GetAsync($"{BaseUrl}/v1/ai/backends", cancellationToken), cancellationToken);

        var body = await ReadAsync<BackendsResponse>(response, cancellationToken);

        return (body?.Backends ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
            .Select(entry => new ChiezoBackend(
                entry.Id!,
                string.IsNullOrWhiteSpace(entry.Label) ? entry.Id! : entry.Label!,
                entry.Models ?? [],
                entry.Efforts ?? [],
                entry.ModelRequired))
            .ToList();
    }

    /// <summary>1 往復投げて本文を受け取る。</summary>
    public async Task<ChiezoCompletion> CompleteAsync(
        string backend,
        string? model,
        string? effort,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var request = new CompleteRequest(
            backend,
            string.IsNullOrWhiteSpace(model) ? null : model,
            string.IsNullOrWhiteSpace(effort) ? null : effort,
            [new Message("system", systemPrompt), new Message("user", userPrompt)]);

        using var client = CreateClient(timeout);
        using var response = await SendAsync(
            () => client.PostAsJsonAsync($"{BaseUrl}/v1/ai/complete", request, cancellationToken),
            cancellationToken);

        var body = await ReadAsync<CompleteResponse>(response, cancellationToken);

        return body?.Content?.Trim() is { Length: > 0 } text
            ? new ChiezoCompletion(text, body.Model)
            : throw new FormatException("Chiezo が空の応答を返した。");
    }

    HttpClient CreateClient(TimeSpan wait)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        // こちらの待ちは相手より長くする。Chiezo の向こうにいるのは AI なので、
        // 生成そのものが数分かかることがある
        client.Timeout = wait + TimeSpan.FromSeconds(30);
        return client;
    }

    async Task<HttpResponseMessage> SendAsync(
        Func<Task<HttpResponseMessage>> send, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await send();
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Chiezo({BaseUrl})が時間内に応答しなかった。");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Chiezo({BaseUrl})に繋がらない。URL と、Chiezo が動いているかを確認する。", ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        // Chiezo は理由を JSON の本文に入れて返す(未設定の相手なら 404、
        // 「答える」層が無効なら 503)。そのまま画面へ出せるように載せる
        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        response.Dispose();
        throw new InvalidOperationException(
            $"Chiezo がエラーを返した(HTTP {(int)response.StatusCode}): {Excerpt(detail)}");
    }

    static async Task<T?> ReadAsync<T>(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Chiezo の応答が JSON として読めない。", ex);
        }
    }

    static string Excerpt(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300] + "…";
    }
}

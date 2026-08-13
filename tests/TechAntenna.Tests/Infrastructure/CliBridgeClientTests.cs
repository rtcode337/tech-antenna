using System.Net;
using System.Text.Json;
using TechAntenna.Infrastructure.Bridge;

namespace TechAntenna.Tests.Infrastructure;

public class CliBridgeClientTests
{
    const string Response = """
        {"choices":[{"message":{"role":"assistant","content":"  応答本文  "}}]}
        """;

    static CliBridgeClient Client(
        RecordingHttpClientFactory factory, string? model = "claude-sonnet-5") =>
        new(factory, "http://bridge:7013/v1/", model, TimeSpan.FromSeconds(30));

    [Fact]
    public async Task 応答本文を返す()
    {
        var factory = new RecordingHttpClientFactory(Response);

        var text = await Client(factory).RunAsync("システム", "本文");

        Assert.Equal("応答本文", text);
        // 末尾のスラッシュがあってもエンドポイントは二重にならない
        Assert.Equal("http://bridge:7013/v1/chat/completions", factory.RequestedUris.Single().ToString());
    }

    [Fact]
    public async Task システムと本文を役割つきで渡す()
    {
        var factory = new RecordingHttpClientFactory(Response);

        await Client(factory).RunAsync("システム", "本文");

        using var request = JsonDocument.Parse(factory.RequestBody);
        var messages = request.RootElement.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("システム", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("本文", messages[1].GetProperty("content").GetString());
        // 上限秒数は要求ごとに渡す(ブリッジ起動時の既定より短くも長くもできる)
        Assert.Equal(30, request.RootElement.GetProperty("chiezo_timeout").GetDouble());
        Assert.Equal("claude-sonnet-5", request.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task モデル未指定ならブリッジの既定に任せる()
    {
        var factory = new RecordingHttpClientFactory(Response);

        await Client(factory, model: null).RunAsync("システム", "本文");

        using var request = JsonDocument.Parse(factory.RequestBody);
        Assert.Equal(JsonValueKind.Null, request.RootElement.GetProperty("model").ValueKind);
    }

    [Fact]
    public async Task ブリッジの打ち切りは時間切れとして投げる()
    {
        // ブリッジは上限を過ぎると 504 と経過秒数を返す
        var factory = new RecordingHttpClientFactory(
            """{"error":"claude timed out after 300s"}""", HttpStatusCode.GatewayTimeout);

        var ex = await Assert.ThrowsAsync<TimeoutException>(
            () => Client(factory).RunAsync("システム", "本文"));
        Assert.Contains("timed out", ex.Message);
    }

    [Fact]
    public async Task 認証情報が未登録なら理由つきで投げる()
    {
        // 共有ディレクトリの設定 DB を読めていないときにブリッジが返す 401
        var factory = new RecordingHttpClientFactory(
            """{"error":"claude の認証情報が未登録です"}""", HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Client(factory).RunAsync("システム", "本文"));
        Assert.Contains("401", ex.Message);
        Assert.Contains("認証情報が未登録", ex.Message);
    }

    [Fact]
    public async Task 空の応答は例外にする()
    {
        var factory = new RecordingHttpClientFactory("""{"choices":[]}""");

        await Assert.ThrowsAsync<FormatException>(() => Client(factory).RunAsync("システム", "本文"));
    }

    /// <summary>要求の本文まで記録する IHttpClientFactory。</summary>
    sealed class RecordingHttpClientFactory(
        string responseBody, HttpStatusCode statusCode = HttpStatusCode.OK) : IHttpClientFactory
    {
        public List<Uri> RequestedUris { get; } = [];

        public string RequestBody { get; private set; } = "";

        public HttpClient CreateClient(string name) => new(new Handler(this, responseBody, statusCode));

        sealed class Handler(
            RecordingHttpClientFactory owner, string body, HttpStatusCode statusCode) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri is { } uri)
                {
                    owner.RequestedUris.Add(uri);
                }
                if (request.Content is { } content)
                {
                    owner.RequestBody = await content.ReadAsStringAsync(cancellationToken);
                }

                return new HttpResponseMessage(statusCode) { Content = new StringContent(body) };
            }
        }
    }
}

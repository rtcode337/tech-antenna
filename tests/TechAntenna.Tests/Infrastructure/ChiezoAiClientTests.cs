using System.Net;
using System.Text.Json;
using TechAntenna.Infrastructure.Chiezo;

namespace TechAntenna.Tests.Infrastructure;

public class ChiezoAiClientTests
{
    const string Backends = """
        {"backends":[
          {"id":"gemini","label":"Gemini","models":["gemini-2.5-flash"],"efforts":[],"model_required":true},
          {"id":"claude","label":"Claude Code","models":["sonnet"],"efforts":["low","high"],"model_required":false}
        ]}
        """;

    static ChiezoAiClient Client(RecordingFactory factory) =>
        new(factory, "http://chiezo:7010/", TimeSpan.FromSeconds(30));

    [Fact]
    public async Task 話せる相手を取り出す()
    {
        var factory = new RecordingFactory(Backends);

        var backends = await Client(factory).GetBackendsAsync();

        Assert.Equal(["gemini", "claude"], backends.Select(b => b.Id));
        Assert.Equal("Claude Code", backends[1].Label);
        // エフォートを持たない相手は空(画面に出さない)
        Assert.Empty(backends[0].Efforts);
        Assert.Equal(["low", "high"], backends[1].Efforts);
        // モデル指定が要らない相手は「既定に任せる」を選ばせる
        Assert.False(backends[1].ModelRequired);
        // 末尾のスラッシュがあってもパスは二重にならない
        Assert.Equal("http://chiezo:7010/v1/ai/backends", factory.RequestedUris.Single().ToString());
    }

    [Fact]
    public async Task システムと本文を役割つきで送る()
    {
        // **知識ベースは引かせない**(/v1/chat ではなく /v1/ai/complete)
        var factory = new RecordingFactory("""{"content":"  まとめ  ","model":"gemini-2.5-flash"}""");

        var completion = await Client(factory).CompleteAsync(
            "gemini", "gemini-2.5-flash", "high", "システム", "本文");

        Assert.Equal("まとめ", completion.Content);
        Assert.Equal("http://chiezo:7010/v1/ai/complete", factory.RequestedUris.Single().ToString());

        using var request = JsonDocument.Parse(factory.RequestBody);
        Assert.Equal("gemini", request.RootElement.GetProperty("backend").GetString());
        Assert.Equal("high", request.RootElement.GetProperty("effort").GetString());
        var messages = request.RootElement.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("本文", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task モデルとエフォートは空なら送らない()
    {
        // 相手の既定に任せる(推論サーバや CLI は自分で決められる)
        var factory = new RecordingFactory("""{"content":"はい"}""");

        await Client(factory).CompleteAsync("local", null, "", "システム", "本文");

        using var request = JsonDocument.Parse(factory.RequestBody);
        Assert.Equal(JsonValueKind.Null, request.RootElement.GetProperty("model").ValueKind);
        Assert.Equal(JsonValueKind.Null, request.RootElement.GetProperty("effort").ValueKind);
    }

    [Fact]
    public async Task 実際に使われたモデルを名乗る()
    {
        // 「相手の既定に任せる」で頼んだときに、何が書いたのかを知る唯一の手がかり
        var factory = new RecordingFactory("""{"content":"はい","model":"claude-sonnet-5"}""");

        var completion = await Client(factory).CompleteAsync(
            "claude", null, null, "システム", "本文");

        Assert.Equal("claude-sonnet-5", completion.Model);
    }

    [Fact]
    public async Task Chiezoのエラーは理由つきで投げる()
    {
        // 相手を知らない(404)・「答える」層が無効(503)の理由は本文に入っている
        var factory = new RecordingFactory(
            """{"error":"unknown backend: gpt5","backends":["gemini"]}""", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Client(factory).CompleteAsync("gpt5", null, null, "システム", "本文"));

        Assert.Contains("404", ex.Message);
        Assert.Contains("unknown backend", ex.Message);
    }

    [Fact]
    public async Task 空の応答は例外にする()
    {
        var factory = new RecordingFactory("""{"content":"   "}""");

        await Assert.ThrowsAsync<FormatException>(
            () => Client(factory).CompleteAsync("gemini", null, null, "システム", "本文"));
    }

    /// <summary>要求の URI と本文まで記録する IHttpClientFactory。</summary>
    sealed class RecordingFactory(string body, HttpStatusCode status = HttpStatusCode.OK)
        : IHttpClientFactory
    {
        public List<Uri> RequestedUris { get; } = [];

        public string RequestBody { get; private set; } = "";

        public HttpClient CreateClient(string name) => new(new Handler(this, body, status));

        sealed class Handler(RecordingFactory owner, string body, HttpStatusCode status)
            : HttpMessageHandler
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

                return new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
                };
            }
        }
    }
}

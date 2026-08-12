using System.Net;
using System.Text.Json;
using TechAntenna.Core.Models;
using TechAntenna.Infrastructure.Notifications;

namespace TechAntenna.Tests.Infrastructure;

public class NtfyDigestNotifierTests
{
    static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    static Digest Digest(params DigestItem[] items) => Digest(DigestScope.Overall, items);

    static Digest Digest(DigestScope scope, params DigestItem[] items) => new()
    {
        Scope = scope,
        GeneratedAt = Now,
        Lead = "今日は生成AIの話題が中心。",
        Items = items,
        GeneratorName = "テスト",
    };

    /// <summary>送ったリクエストを記録して 200 を返すハンドラ。</summary>
    class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    [Fact]
    public void 本文は導入と項目と出典で組む()
    {
        var message = NtfyDigestNotifier.BuildMessage(Digest(
            new DigestItem("見出し", "本文。", "https://example.com/a"),
            new DigestItem("URL無し", "本文2。", null)));

        Assert.Contains("今日は生成AIの話題が中心。", message);
        Assert.Contains("● 見出し", message);
        Assert.Contains("https://example.com/a", message);
        Assert.Contains("● URL無し", message);
    }

    [Fact]
    public void 長すぎる本文は上限で切る()
    {
        var message = NtfyDigestNotifier.BuildMessage(Digest(
            Enumerable.Range(1, 50)
                .Select(i => new DigestItem($"見出し{i}", new string('あ', 100), null))
                .ToArray()));

        Assert.True(message.Length <= NtfyDigestNotifier.MaxMessageChars + 1);
        Assert.EndsWith("…", message);
    }

    [Fact]
    public async Task JSONで題名とトピックとクリック先を送る()
    {
        var handler = new RecordingHandler();
        var notifier = new NtfyDigestNotifier(
            new SingleClientFactory(handler),
            () => new NtfyTarget(
                "https://ntfy.example.com",
                "tech-antenna",
                AccessToken: "token-123",
                ClickUrl: "https://home.example.com/"));

        await notifier.NotifyAsync(Digest(new DigestItem("見出し", "本文。", null)));

        Assert.Equal("https://ntfy.example.com/", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);

        using var doc = JsonDocument.Parse(handler.Body!);
        Assert.Equal("tech-antenna", doc.RootElement.GetProperty("topic").GetString());
        // 日本語のタイトルをヘッダではなく JSON で送る(RFC 2047 エンコード不要)のが要点
        Assert.Contains("今日のサマリー", doc.RootElement.GetProperty("title").GetString());
        Assert.Equal("https://home.example.com/", doc.RootElement.GetProperty("click").GetString());
    }

    // 1回の生成で2通届くので、通知面(本文が畳まれた状態)で見分けられること
    [Fact]
    public async Task 守備範囲はタイトルとタグに出す()
    {
        var handler = new RecordingHandler();
        var notifier = new NtfyDigestNotifier(
            new SingleClientFactory(handler),
            () => new NtfyTarget("https://ntfy.example.com", "t", null, null));

        await notifier.NotifyAsync(
            Digest(DigestScope.Interests, new DigestItem("見出し", "本文。", null)));

        using var interests = JsonDocument.Parse(handler.Body!);
        Assert.Contains("興味トピック", interests.RootElement.GetProperty("title").GetString());
        Assert.Equal("dart", interests.RootElement.GetProperty("tags")[0].GetString());

        await notifier.NotifyAsync(
            Digest(DigestScope.Overall, new DigestItem("見出し", "本文。", null)));

        using var overall = JsonDocument.Parse(handler.Body!);
        Assert.Contains("技術界隈全体", overall.RootElement.GetProperty("title").GetString());
        Assert.Equal("newspaper", overall.RootElement.GetProperty("tags")[0].GetString());
    }

    [Fact]
    public async Task トークン未設定ならAuthorizationを付けない()
    {
        var handler = new RecordingHandler();
        var notifier = new NtfyDigestNotifier(
            new SingleClientFactory(handler),
            () => new NtfyTarget("https://ntfy.example.com", "t", null, null));

        await notifier.NotifyAsync(Digest(new DigestItem("見出し", "本文。", null)));

        Assert.Null(handler.Request!.Headers.Authorization);
    }

    [Fact]
    public async Task 通知先が無ければ送らずにfalseを返す()
    {
        // 接続先未設定・通知オフのとき provider は null を返す。リクエストを出さないこと
        var handler = new RecordingHandler();
        var notifier = new NtfyDigestNotifier(new SingleClientFactory(handler), () => null);

        Assert.False(await notifier.NotifyAsync(Digest(new DigestItem("見出し", "本文。", null))));
        Assert.Null(handler.Request);
    }
}

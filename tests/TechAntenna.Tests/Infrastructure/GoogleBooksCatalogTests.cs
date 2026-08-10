using System.Net;
using Microsoft.Extensions.Time.Testing;
using TechAntenna.Infrastructure.Books;

namespace TechAntenna.Tests.Infrastructure;

public class GoogleBooksCatalogTests
{
    const string TooManyRequests = """
        {"error":{"code":429,"message":"Quota exceeded","status":"RESOURCE_EXHAUSTED"}}
        """;

    static FakeTimeProvider Clock() =>
        new(new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero));

    static GoogleBooksCatalog Catalog(string? apiKey) => new(
        new StubHttpClientFactory(TooManyRequests, HttpStatusCode.TooManyRequests),
        Clock(),
        () => apiKey);

    [Fact]
    public async Task キー未設定で429ならキーが要ると分かるメッセージになる()
    {
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => Catalog(apiKey: "").SearchAsync("C#"));

        Assert.Contains("API キーが未設定", ex.Message);
        Assert.Contains("外部連携", ex.Message);
    }

    [Fact]
    public async Task キー設定済みで429なら使いすぎだと分かるメッセージになる()
    {
        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => Catalog(apiKey: "dummy-key").SearchAsync("C#"));

        Assert.DoesNotContain("API キーが未設定", ex.Message);
        Assert.Contains("上限に達した", ex.Message);
    }
}

using System.Net;

namespace TechAntenna.Tests.Infrastructure;

/// <summary>常に決まったレスポンスを返す IHttpClientFactory。外部 API を叩かずに動作を確かめるために使う。</summary>
public class StubHttpClientFactory(string responseBody) : IHttpClientFactory
{
    /// <summary>実際に要求された URI。リクエストの組み立てを確認するために記録する。</summary>
    public List<Uri> RequestedUris { get; } = [];

    public HttpClient CreateClient(string name) => new(new StubHandler(responseBody, RequestedUris));

    sealed class StubHandler(string body, List<Uri> requestedUris) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is { } uri)
            {
                requestedUris.Add(uri);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
        }
    }
}

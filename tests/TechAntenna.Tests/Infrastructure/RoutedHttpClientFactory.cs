using System.Net;

namespace TechAntenna.Tests.Infrastructure;

/// <summary>
/// <b>要求された URI に応じて返す内容を変える</b> IHttpClientFactory。
///
/// <see cref="StubHttpClientFactory"/> は常に同じ本文を返すので、
/// 「1回目と2回目で違う応答が返る」経路(ページ送り、サブドメイン → ID の解決、
/// 存在しないグループの 404)を再現できない。そこだけこちらを使う。
/// </summary>
public class RoutedHttpClientFactory(Func<Uri, (HttpStatusCode Status, string Body)> respond)
    : IHttpClientFactory
{
    /// <summary>実際に要求された URI。リクエストの組み立てと回数を確認するために記録する。</summary>
    public List<Uri> RequestedUris { get; } = [];

    /// <summary>URI の一部に一致したらその本文を返す、という素朴な組み立て方。</summary>
    public static RoutedHttpClientFactory Matching(params (string Contains, string Body)[] routes) =>
        new(uri =>
        {
            foreach (var (contains, body) in routes)
            {
                if (uri.ToString().Contains(contains, StringComparison.Ordinal))
                {
                    return (HttpStatusCode.OK, body);
                }
            }

            // 名簿の打ち間違いを再現できるよう、当たらなければ 404
            return (HttpStatusCode.NotFound, "");
        });

    public HttpClient CreateClient(string name) => new(new RoutedHandler(respond, RequestedUris));

    sealed class RoutedHandler(
        Func<Uri, (HttpStatusCode Status, string Body)> respond, List<Uri> requestedUris)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? new Uri("https://example.invalid/");
            requestedUris.Add(uri);
            var (status, body) = respond(uri);

            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}

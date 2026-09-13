using System;
using System.Net.Http;
using System.Threading;

namespace CoreWebForms.Services
{
    internal static class ServiceHttp
    {
        internal static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(2);

        private static readonly HttpClient _client = new HttpClient(
            new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) }, disposeHandler: true)
        {
            Timeout = CallTimeout
        };

        internal static HttpResponseMessage Send(Func<HttpRequestMessage> requestFactory, bool retryOnFailure)
        {
            const int maxAttempts = 3;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return _client.Send(requestFactory());
                }
                catch (HttpRequestException) when (retryOnFailure && attempt < maxAttempts)
                {
                    Thread.Sleep(200);
                }
                catch (TaskCanceledException) when (retryOnFailure && attempt < maxAttempts)
                {
                    Thread.Sleep(200);
                }
            }
        }
    }
}

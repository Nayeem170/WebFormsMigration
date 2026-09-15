using System;
using System.Net.Http;
using System.Threading;
using Inventory.Contracts;

namespace CoreWebForms.Services
{
    internal static class ServiceHttp
    {
        internal static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(2);

        internal static readonly TimeSpan PooledConnectionLifetime = ReadPoolLifetime();
        private static readonly HttpClient _client = new HttpClient(
            new SocketsHttpHandler { PooledConnectionLifetime = PooledConnectionLifetime }, disposeHandler: true)
        {
            Timeout = CallTimeout
        };

        private static TimeSpan ReadPoolLifetime()
        {
            var raw = Environment.GetEnvironmentVariable("Http__PooledConnectionLifetimeSeconds");
            return double.TryParse(raw, out var seconds) && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.FromMinutes(2);
        }

        internal static HttpResponseMessage Send(Func<string, HttpRequestMessage> requestFactory, bool retryOnFailure, ServiceEndpointPool pool)
        {
            const int maxAttempts = 3;
            for (var attempt = 1; ; attempt++)
            {
                var endpoint = pool.Next();
                try
                {
                    var request = requestFactory(endpoint);
                    request.Headers.Add(CorrelationHeader.Name, Correlation.Current());
                    var response = _client.Send(request);
                    pool.ReportSuccess(endpoint);
                    return response;
                }
                catch (HttpRequestException)
                {
                    if (!ReportAndContinue(pool, endpoint, retryOnFailure, attempt, maxAttempts)) throw;
                }
                catch (TaskCanceledException)
                {
                    if (!ReportAndContinue(pool, endpoint, retryOnFailure, attempt, maxAttempts)) throw;
                }
            }
        }

        private static bool ReportAndContinue(ServiceEndpointPool pool, string endpoint, bool retryOnFailure, int attempt, int maxAttempts)
        {
            pool.ReportFailure(endpoint);
            if (!retryOnFailure || attempt >= maxAttempts) return false;
            Thread.Sleep(200);
            return true;
        }
    }
}

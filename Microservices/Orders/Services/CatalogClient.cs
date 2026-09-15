using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Inventory.Contracts;

namespace Orders{
    public class CatalogRuleException : Exception
    {
        public string ErrorCode { get; }

        public CatalogRuleException(string errorCode, string message) : base(message)
        {
            ErrorCode = errorCode;
        }
    }

    public static class CatalogClient
    {
        private const int MaxAttempts = 3;
        private static readonly TimeSpan PooledConnectionLifetime = ReadPoolLifetime();
        private static readonly HttpClient _client = new HttpClient(
            new SocketsHttpHandler { PooledConnectionLifetime = PooledConnectionLifetime }, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(3)
        };
        private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

        private static TimeSpan ReadPoolLifetime()
        {
            var raw = Environment.GetEnvironmentVariable("Http__PooledConnectionLifetimeSeconds");
            return double.TryParse(raw, out var seconds) && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.FromMinutes(2);
        }

        public static void Reserve(ServiceEndpointPool pool, ReserveStockRequest request, string? correlationId = null)
            => SendStockCall(pool, "/api/products/reserve", request, correlationId);

        public static void Release(ServiceEndpointPool pool, ReleaseStockRequest request, string? correlationId = null)
            => SendStockCall(pool, "/api/products/release", request, correlationId);

        private static void SendStockCall(ServiceEndpointPool pool, string path, object request, string? correlationId)
        {
            for (var attempt = 1; ; attempt++)
            {
                var endpoint = pool.Next();
                HttpResponseMessage response;
                try
                {
                    using var message = new HttpRequestMessage(HttpMethod.Post, endpoint + path)
                    {
                        Content = new StringContent(
                            JsonSerializer.Serialize(request, _jsonOptions), Encoding.UTF8, "application/json")
                    };
                    if (!string.IsNullOrEmpty(correlationId))
                        message.Headers.Add(CorrelationHeader.Name, correlationId);
                    response = _client.Send(message);
                    pool.ReportSuccess(endpoint);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    pool.ReportFailure(endpoint);
                    if (attempt >= MaxAttempts)
                        throw;
                    Thread.Sleep(200);
                    continue;
                }
                using (response)
                {
                    if (response.IsSuccessStatusCode)
                        return;
                    var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                    {
                        var error = TryReadError(body);
                        throw new CatalogRuleException(
                            error?.ErrorCode ?? "StockRule",
                            error?.Message ?? "Catalog rejected the stock call.");
                    }
                    if (attempt < MaxAttempts)
                    {
                        Thread.Sleep(200);
                        continue;
                    }
                    throw new HttpRequestException(
                        string.Format("Catalog stock call failed: {0} {1}", (int)response.StatusCode, body));
                }
            }
        }

        private static ApiErrorResponse? TryReadError(string body)
        {
            try
            {
                return JsonSerializer.Deserialize<ApiErrorResponse>(body, _jsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}

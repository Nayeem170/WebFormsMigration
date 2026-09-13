using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Inventory.Contracts;

namespace Orders
{
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
        private static readonly HttpClient _client = new HttpClient(
            new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) }, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(3)
        };
        private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

        public static void Reserve(string baseUrl, ReserveStockRequest request)
            => SendStockCall(baseUrl.TrimEnd('/') + "/api/products/reserve", request);

        public static void Release(string baseUrl, ReleaseStockRequest request)
            => SendStockCall(baseUrl.TrimEnd('/') + "/api/products/release", request);

        private static void SendStockCall(string url, object request)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    using var content = new StringContent(
                        JsonSerializer.Serialize(request, _jsonOptions), Encoding.UTF8, "application/json");
                    using var response = _client.PostAsync(url, content).GetAwaiter().GetResult();
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
                    throw new HttpRequestException(
                        string.Format("Catalog stock call failed: {0} {1}", (int)response.StatusCode, body));
                }
                catch (HttpRequestException) when (attempt < MaxAttempts)
                {
                    Thread.Sleep(200);
                }
                catch (TaskCanceledException) when (attempt < MaxAttempts)
                {
                    Thread.Sleep(200);
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

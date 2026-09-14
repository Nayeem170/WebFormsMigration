using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using CoreWebForms.Core;
using Inventory.Contracts;

namespace CoreWebForms.Services
{
    public class HttpOrderService : IOrderService
    {
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        private readonly ServiceEndpointPool _pool;
        private readonly ILogger _log;

        public HttpOrderService(string baseUrlList, ILogger log)
        {
            _pool = new ServiceEndpointPool(baseUrlList);
            _log = log;
            _log.Info(string.Format("Orders endpoint pool: {0} endpoint(s): {1}",
                _pool.Endpoints.Count, string.Join(", ", _pool.Endpoints)));
        }

        public List<Order> GetAll(bool includeDeleted = false)
        {
            return GetAllCore(includeDeleted, null);
        }

        public List<Order> GetAll(bool includeDeleted, string? status)
        {
            return GetAllCore(includeDeleted, status);
        }

        public Order? GetById(int id)
        {
            var response = Send(HttpMethod.Get, "/api/orders/" + id);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                response.Dispose();
                return null;
            }
            return ToModel(ReadJson<OrderDto>(response));
        }

        public int Count(bool includeDeleted = false)
        {
            return CountCore(includeDeleted, null);
        }

        public int Count(bool includeDeleted, string? status)
        {
            return CountCore(includeDeleted, status);
        }

        public int CountByStatus(string status)
        {
            return CountCore(false, status);
        }

        public List<Order> GetRecent(int count)
        {
            var payload = ReadJson<List<OrderDto>>(
                Send(HttpMethod.Get, "/api/orders/recent/" + count));
            return payload.Select(ToModel).ToList();
        }

        public List<OrderItem> GetItems(int orderId)
        {
            var payload = ReadJson<List<OrderItemDto>>(
                Send(HttpMethod.Get, "/api/orders/" + orderId + "/items"));
            return payload.Select(ToItemModel).ToList();
        }

        public List<Order> GetPaged(int skip, int take, bool includeDeleted, string? status)
        {
            var payload = ReadJson<PagedResult<OrderDto>>(
                Send(HttpMethod.Get, Query("/api/orders", includeDeleted, status, skip, take)));
            return payload.Items.Select(ToModel).ToList();
        }

        public int PlaceOrder(Order order)
        {
            if (order == null) throw new ArgumentNullException(nameof(order));
            if (order.Items == null || !order.Items.Any())
                throw new ArgumentException("Order must contain at least one item.", nameof(order));

            var response = Send(HttpMethod.Post, "/api/orders", ToDto(order));
            return ReadJson<int>(response);
        }

        public void UpdateStatus(int orderId, string status, string priority)
        {
            var body = new UpdateStatusRequest { Status = status, Priority = priority };
            var response = Send(HttpMethod.Put, "/api/orders/" + orderId + "/status", body);
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return;
            }
        }

        public void DeleteOrder(int id)
        {
            var response = Send(HttpMethod.Delete, "/api/orders/" + id);
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return;
            }
        }

        private List<Order> GetAllCore(bool includeDeleted, string? status)
        {
            var payload = ReadJson<PagedResult<OrderDto>>(
                Send(HttpMethod.Get, Query("/api/orders", includeDeleted, status, 0, 0)));
            return payload.Items.Select(ToModel).ToList();
        }

        private int CountCore(bool includeDeleted, string? status)
        {
            var payload = ReadJson<PagedResult<OrderDto>>(
                Send(HttpMethod.Get, Query("/api/orders", includeDeleted, status, 0, 0)));
            return payload.TotalCount;
        }

        private static string Query(string path, bool includeDeleted, string? status, int skip, int take)
        {
            var query = "?includeDeleted=" + (includeDeleted ? "true" : "false")
                + "&skip=" + skip + "&take=" + take;
            if (!string.IsNullOrEmpty(status))
                query += "&status=" + Uri.EscapeDataString(status);
            return path + query;
        }

        private HttpResponseMessage Send(HttpMethod method, string path, object? body = null)
        {
            var response = ServiceHttp.Send(endpoint => BuildRequest(method, endpoint, path, body), method == HttpMethod.Get, _pool);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
                return response;

            var detail = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var error = TryReadError(detail);
            var code = (int)response.StatusCode;
            response.Dispose();
            var summary = string.Format("Orders request failed: {0} {1} -> {2} {3}",
                method.Method, path, code, detail);
            _log.Error(summary);

            if (code == 400 && error != null)
                throw new ValidationException(error.Message);
            if (code == 409 && error != null)
                throw new InvalidOperationException(error.Message);
            throw new HttpRequestException(summary);
        }

        private HttpRequestMessage BuildRequest(HttpMethod method, string endpoint, string path, object? body)
        {
            var request = new HttpRequestMessage(method, endpoint + path);
            if (body != null)
                request.Content = new StringContent(
                    JsonSerializer.Serialize(body, _jsonOptions), Encoding.UTF8, "application/json");
            return request;
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

        private static T ReadJson<T>(HttpResponseMessage response)
        {
            using (response)
            {
                var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonSerializer.Deserialize<T>(json, _jsonOptions)
                    ?? throw new HttpRequestException("Orders returned an empty response body.");
            }
        }

        private static OrderDto ToDto(Order o) => new()
        {
            Id = o.Id,
            CustomerName = o.CustomerName,
            CustomerEmail = o.CustomerEmail,
            OrderDate = o.OrderDate,
            DeliveryDate = o.DeliveryDate,
            Status = o.Status,
            Priority = o.Priority,
            Extras = o.Extras,
            Total = o.Total,
            IsDeleted = o.IsDeleted,
            Items = o.Items.Select(ToItemDto).ToList()
        };

        private static OrderItemDto ToItemDto(OrderItem i) => new()
        {
            ProductId = i.ProductId,
            ProductName = i.ProductName,
            Quantity = i.Quantity,
            UnitPrice = i.UnitPrice
        };

        private static Order ToModel(OrderDto o) => new()
        {
            Id = o.Id,
            CustomerName = o.CustomerName,
            CustomerEmail = o.CustomerEmail,
            OrderDate = o.OrderDate,
            DeliveryDate = o.DeliveryDate,
            Status = o.Status,
            Priority = o.Priority,
            Extras = o.Extras,
            Total = o.Total,
            IsDeleted = o.IsDeleted,
            Items = o.Items.Select(ToItemModel).ToList()
        };

        private static OrderItem ToItemModel(OrderItemDto i) => new()
        {
            ProductId = i.ProductId,
            ProductName = i.ProductName,
            Quantity = i.Quantity,
            UnitPrice = i.UnitPrice
        };
    }
}

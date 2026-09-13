using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using CoreWebForms.Core;
using Inventory.Contracts;

namespace CoreWebForms.Services
{
    public class HttpProductService : IProductService
    {
        private static readonly HttpClient _client = new HttpClient();
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        private readonly string _baseUrl;
        private readonly ILogger _log;

        public HttpProductService(string baseUrl, ILogger log)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _log = log;
        }

        public List<Product> GetAll(bool includeDeleted = false)
        {
            var payload = ReadJson<List<ProductDto>>(
                Send(HttpMethod.Get, "/api/products?includeDeleted=" + (includeDeleted ? "true" : "false")));
            return payload.Select(ToModel).ToList();
        }

        public Product? GetById(int id)
        {
            var response = Send(HttpMethod.Get, "/api/products/" + id);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                response.Dispose();
                return null;
            }
            return ToModel(ReadJson<ProductDto>(response));
        }

        public int Add(Product product)
        {
            return ReadJson<int>(Send(HttpMethod.Post, "/api/products", ToDto(product)));
        }

        public void Update(Product product)
        {
            Send(HttpMethod.Put, "/api/products/" + product.Id, ToDto(product)).Dispose();
        }

        // DELETE /api/products/{id} is a soft delete: Catalog keeps the row for order history.
        public void Delete(int id)
        {
            Send(HttpMethod.Delete, "/api/products/" + id).Dispose();
        }

        private HttpResponseMessage Send(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, _baseUrl + path);
            if (body != null)
                request.Content = new StringContent(
                    JsonSerializer.Serialize(body, _jsonOptions), Encoding.UTF8, "application/json");
            var response = _client.Send(request);
            if (!response.IsSuccessStatusCode)
            {
                var detail = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                response.Dispose();
                var error = string.Format("Catalog request failed: {0} {1} -> {2} {3}",
                    method.Method, path, (int)response.StatusCode, detail);
                _log.Error(error);
                throw new HttpRequestException(error);
            }
            return response;
        }

        private static T ReadJson<T>(HttpResponseMessage response)
        {
            using (response)
            {
                var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return JsonSerializer.Deserialize<T>(json, _jsonOptions)
                    ?? throw new HttpRequestException("Catalog returned an empty response body.");
            }
        }

        private static ProductDto ToDto(Product p) => new()
        {
            Id = p.Id,
            Name = p.Name,
            Category = p.Category,
            Price = p.Price,
            Stock = p.Stock,
            IsActive = p.IsActive,
            IsDeleted = p.IsDeleted,
            AddedDate = p.AddedDate
        };

        private static Product ToModel(ProductDto dto) => new()
        {
            Id = dto.Id,
            Name = dto.Name,
            Category = dto.Category,
            Price = dto.Price,
            Stock = dto.Stock,
            IsActive = dto.IsActive,
            IsDeleted = dto.IsDeleted,
            AddedDate = dto.AddedDate
        };
    }
}

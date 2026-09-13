using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Sdk;

namespace CoreWebForms.CharacterizationTests
{
    public class HttpCharacterizationTests : IDisposable
    {
        private static readonly HttpClient Http = CreateClient();

        private readonly string _run = Guid.NewGuid().ToString("N")[..8];
        private readonly List<int> _createdProducts = new();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var catalog = Environment.GetEnvironmentVariable("CATALOG_BASE") ?? "http://localhost:8094";
            var orders = Environment.GetEnvironmentVariable("ORDERS_BASE") ?? "http://localhost:8095";
            try
            {
                var c = client.GetAsync(catalog + "/health").Result;
                var o = client.GetAsync(orders + "/health").Result;
                if (!c.IsSuccessStatusCode || !o.IsSuccessStatusCode)
                    throw new XunitException(
                        $"Catalog/Orders answered /health with {(int)c.StatusCode}/{(int)o.StatusCode}; expected 200.");
            }
            catch (XunitException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new XunitException(
                    "Catalog (8094) and Orders (8095) must be running for these tests; start them with scripts/test-failures.ps1 or dotnet run.", ex);
            }
            return client;
        }

        private string CatalogBase => Environment.GetEnvironmentVariable("CATALOG_BASE") ?? "http://localhost:8094";
        private string OrdersBase => Environment.GetEnvironmentVariable("ORDERS_BASE") ?? "http://localhost:8095";

        private async Task<JsonNode> CreateProductAsync(int stock = 10, bool isActive = true)
        {
            var body = new JsonObject
            {
                ["name"] = $"Http Char {_run} {Guid.NewGuid().ToString("N")[..6]}",
                ["category"] = "HttpChar",
                ["price"] = 5m,
                ["stock"] = stock,
                ["isActive"] = isActive
            };
            var response = await Http.PostAsJsonAsync(CatalogBase + "/api/products", body);
            Assert.True(response.IsSuccessStatusCode, $"create product failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            var id = (int)(await response.Content.ReadFromJsonAsync<int>())!;
            _createdProducts.Add(id);
            return (await GetProductAsync(id))!;
        }

        private async Task<JsonNode?> GetProductAsync(int id)
        {
            var response = await Http.GetAsync($"{CatalogBase}/api/products/{id}");
            if ((int)response.StatusCode == 404) return null;
            Assert.True(response.IsSuccessStatusCode, $"get product failed: {(int)response.StatusCode}");
            return JsonNode.Parse(await response.Content.ReadAsStringAsync());
        }

        private async Task<HttpResponseMessage> PutProductAsync(JsonNode product, int stock, bool isActive)
        {
            var body = new JsonObject
            {
                ["name"] = (string)product["name"]!,
                ["category"] = (string)product["category"]!,
                ["price"] = (decimal)product["price"]!,
                ["stock"] = stock,
                ["isActive"] = isActive
            };
            return await Http.PutAsJsonAsync($"{CatalogBase}/api/products/{(int)product["id"]!}", body);
        }

        private async Task<HttpResponseMessage> PlaceOrderAsync(params (int productId, string productName, int quantity, decimal unitPrice)[] items)
        {
            var body = new JsonObject
            {
                ["customerName"] = $"Http Char Customer {_run}",
                ["customerEmail"] = $"char-{_run}@example.com",
                ["orderDate"] = DateTime.UtcNow.ToString("O"),
                ["deliveryDate"] = DateTime.UtcNow.AddDays(3).ToString("O"),
                ["status"] = "Pending",
                ["priority"] = "Normal",
                ["extras"] = new JsonArray(),
                ["items"] = new JsonArray(items.Select(i => (JsonNode)new JsonObject
                {
                    ["productId"] = i.productId,
                    ["productName"] = i.productName,
                    ["quantity"] = i.quantity,
                    ["unitPrice"] = i.unitPrice
                }).ToArray())
            };
            return await Http.PostAsJsonAsync(OrdersBase + "/api/orders", body);
        }

        private async Task<JsonNode?> GetOrderAsync(int id)
        {
            var response = await Http.GetAsync($"{OrdersBase}/api/orders/{id}");
            if ((int)response.StatusCode == 404) return null;
            Assert.True(response.IsSuccessStatusCode, $"get order failed: {(int)response.StatusCode}");
            return JsonNode.Parse(await response.Content.ReadAsStringAsync());
        }

        private async Task<JsonNode> ListOrdersAsync(bool includeDeleted)
        {
            var response = await Http.GetAsync($"{OrdersBase}/api/orders?includeDeleted={includeDeleted.ToString().ToLower()}&skip=0&take=0");
            Assert.True(response.IsSuccessStatusCode, $"list orders failed: {(int)response.StatusCode}");
            return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        }

        private static async Task<JsonNode> ErrorOf(HttpResponseMessage response)
        {
            return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        }

        public void Dispose()
        {
            foreach (var id in _createdProducts)
            {
                try { Http.DeleteAsync($"{CatalogBase}/api/products/{id}").Wait(); } catch { }
            }
        }

        [Fact]
        public async Task ProductUpdate_WithZeroStock_Deactivates()
        {
            var product = await CreateProductAsync(stock: 5, isActive: true);

            var response = await PutProductAsync(product, stock: 0, isActive: true);
            Assert.Equal(204, (int)response.StatusCode);

            var reloaded = await GetProductAsync((int)product["id"]!);
            Assert.Equal(0, (int)reloaded!["stock"]!);
            Assert.False((bool)reloaded["isActive"]!);
        }

        [Fact]
        public async Task ProductUpdate_WithPositiveStock_PreservesActiveAndInactiveFlags()
        {
            var active = await CreateProductAsync(stock: 5, isActive: true);
            var inactive = await CreateProductAsync(stock: 5, isActive: false);

            Assert.Equal(204, (int)(await PutProductAsync(active, stock: 7, isActive: true)).StatusCode);
            Assert.Equal(204, (int)(await PutProductAsync(inactive, stock: 7, isActive: false)).StatusCode);

            var reloadedActive = await GetProductAsync((int)active["id"]!);
            Assert.Equal(7, (int)reloadedActive!["stock"]!);
            Assert.True((bool)reloadedActive["isActive"]!);

            var reloadedInactive = await GetProductAsync((int)inactive["id"]!);
            Assert.Equal(7, (int)reloadedInactive!["stock"]!);
            Assert.False((bool)reloadedInactive["isActive"]!);
        }

        [Fact]
        public async Task ProductUpdate_WithNegativeStock_Returns400()
        {
            var product = await CreateProductAsync(stock: 5, isActive: true);

            var response = await PutProductAsync(product, stock: -1, isActive: true);

            Assert.Equal(400, (int)response.StatusCode);
            var error = await ErrorOf(response);
            Assert.Equal("Validation", (string)error["errorCode"]!);
        }

        [Fact]
        public async Task PlaceOrder_DecrementsStock_KeepsProductActive_DrainingToZeroDeactivates()
        {
            var product = await CreateProductAsync(stock: 5, isActive: true);
            var id = (int)product["id"]!;

            var partial = await PlaceOrderAsync((id, (string)product["name"]!, 3, 5m));
            Assert.Equal(201, (int)partial.StatusCode);
            var afterPartial = await GetProductAsync(id);
            Assert.Equal(2, (int)afterPartial!["stock"]!);
            Assert.True((bool)afterPartial["isActive"]!);

            var drain = await PlaceOrderAsync((id, (string)product["name"]!, 2, 5m));
            Assert.Equal(201, (int)drain.StatusCode);
            var afterDrain = await GetProductAsync(id);
            Assert.Equal(0, (int)afterDrain!["stock"]!);
            Assert.False((bool)afterDrain["isActive"]!);
        }

        [Fact]
        public async Task PlaceOrder_ComputesTotal_AndPersistsDenormalizedItems()
        {
            var first = await CreateProductAsync(stock: 10);
            var second = await CreateProductAsync(stock: 10);

            var response = await PlaceOrderAsync(
                ((int)first["id"]!, "First Product", 2, 3m),
                ((int)second["id"]!, "Second Product", 1, 5m));
            Assert.Equal(201, (int)response.StatusCode);
            var orderId = await response.Content.ReadFromJsonAsync<int>();

            var order = await GetOrderAsync(orderId);
            Assert.Equal(11m, (decimal)order!["total"]!);
            var items = (JsonArray)order["items"]!;
            Assert.Equal(2, items.Count);
            Assert.Contains(items, i =>
                (string)i!["productName"]! == "First Product" && (decimal)i["unitPrice"]! == 3m && (int)i["quantity"]! == 2);
            Assert.Contains(items, i =>
                (string)i!["productName"]! == "Second Product" && (decimal)i["unitPrice"]! == 5m && (int)i["quantity"]! == 1);
        }

        [Fact]
        public async Task PlaceOrder_UnknownProduct_Returns409WithPinnedMessage_AndCreatesNoOrder()
        {
            var before = (int)(await ListOrdersAsync(true))["totalCount"]!;

            var response = await PlaceOrderAsync((999999, "Ghost", 1, 5m));

            Assert.Equal(409, (int)response.StatusCode);
            var error = await ErrorOf(response);
            Assert.Equal("ProductNotFound", (string)error["errorCode"]!);
            Assert.Equal("Product ID 999999 not found.", (string)error["message"]!);
            var after = (int)(await ListOrdersAsync(true))["totalCount"]!;
            Assert.Equal(before, after);
        }

        [Fact]
        public async Task PlaceOrder_OnSoftDeletedProduct_Succeeds_WithoutReactivating()
        {
            var product = await CreateProductAsync(stock: 5, isActive: true);
            var id = (int)product["id"]!;
            Assert.Equal(204, (int)(await Http.DeleteAsync($"{CatalogBase}/api/products/{id}")).StatusCode);

            var response = await PlaceOrderAsync((id, (string)product["name"]!, 1, 5m));

            Assert.Equal(201, (int)response.StatusCode);
            var reloaded = await GetProductAsync(id);
            Assert.Equal(4, (int)reloaded!["stock"]!);
            Assert.True((bool)reloaded["isDeleted"]!);
            Assert.False((bool)reloaded["isActive"]!);
        }

        [Fact]
        public async Task DeleteOrder_RestoresStock_AndReactivatesDrainedProduct_SecondDeleteIs404()
        {
            var product = await CreateProductAsync(stock: 5, isActive: true);
            var id = (int)product["id"]!;
            var placed = await PlaceOrderAsync((id, (string)product["name"]!, 5, 5m));
            var orderId = await placed.Content.ReadFromJsonAsync<int>();
            Assert.False((bool)(await GetProductAsync(id))!["isActive"]!);

            Assert.Equal(204, (int)(await Http.DeleteAsync($"{OrdersBase}/api/orders/{orderId}")).StatusCode);

            var restored = await GetProductAsync(id);
            Assert.Equal(5, (int)restored!["stock"]!);
            Assert.True((bool)restored["isActive"]!);

            Assert.Equal(404, (int)(await Http.DeleteAsync($"{OrdersBase}/api/orders/{orderId}")).StatusCode);
            Assert.Equal(5, (int)(await GetProductAsync(id))!["stock"]!);
        }

        [Fact]
        public async Task DeleteOrder_OnSoftDeletedProduct_RestoresStock_WithoutReactivating()
        {
            var product = await CreateProductAsync(stock: 5, isActive: true);
            var id = (int)product["id"]!;
            var placed = await PlaceOrderAsync((id, (string)product["name"]!, 5, 5m));
            var orderId = await placed.Content.ReadFromJsonAsync<int>();
            Assert.Equal(204, (int)(await Http.DeleteAsync($"{CatalogBase}/api/products/{id}")).StatusCode);

            Assert.Equal(204, (int)(await Http.DeleteAsync($"{OrdersBase}/api/orders/{orderId}")).StatusCode);

            var restored = await GetProductAsync(id);
            Assert.Equal(5, (int)restored!["stock"]!);
            Assert.True((bool)restored["isDeleted"]!);
            Assert.False((bool)restored["isActive"]!);
        }

        [Fact]
        public async Task UpdateStatus_ValidOnLiveOrder_InvalidRejected_DeletedOrder404WithValidationFirst()
        {
            var product = await CreateProductAsync(stock: 10, isActive: true);
            var id = (int)product["id"]!;
            var placed = await PlaceOrderAsync((id, (string)product["name"]!, 3, 5m));
            var orderId = await placed.Content.ReadFromJsonAsync<int>();

            var valid = await Http.PutAsJsonAsync($"{OrdersBase}/api/orders/{orderId}/status",
                new { status = "Shipped", priority = "High" });
            Assert.Equal(204, (int)valid.StatusCode);
            var order = await GetOrderAsync(orderId);
            Assert.Equal("Shipped", (string)order!["status"]!);
            Assert.Equal("High", (string)order["priority"]!);
            Assert.Equal(7, (int)(await GetProductAsync(id))!["stock"]!);
            Assert.True((bool)(await GetProductAsync(id))!["isActive"]!);

            var badStatus = await Http.PutAsJsonAsync($"{OrdersBase}/api/orders/{orderId}/status",
                new { status = "Bogus", priority = "Normal" });
            Assert.Equal(400, (int)badStatus.StatusCode);
            var badPriority = await Http.PutAsJsonAsync($"{OrdersBase}/api/orders/{orderId}/status",
                new { status = "Shipped", priority = "Urgent" });
            Assert.Equal(400, (int)badPriority.StatusCode);
            order = await GetOrderAsync(orderId);
            Assert.Equal("Shipped", (string)order!["status"]!);
            Assert.Equal("High", (string)order["priority"]!);

            Assert.Equal(204, (int)(await Http.DeleteAsync($"{OrdersBase}/api/orders/{orderId}")).StatusCode);
            var listed = (JsonArray)(await ListOrdersAsync(false))["items"]!;
            Assert.DoesNotContain(listed, o => (int)o!["id"]! == orderId);
            var listedWithDeleted = (JsonArray)(await ListOrdersAsync(true))["items"]!;
            var deleted = Assert.Single(listedWithDeleted, o => (int)o!["id"]! == orderId);
            Assert.True((bool)deleted!["isDeleted"]!);

            var invalidOnDeleted = await Http.PutAsJsonAsync($"{OrdersBase}/api/orders/{orderId}/status",
                new { status = "Bogus", priority = "Normal" });
            Assert.Equal(400, (int)invalidOnDeleted.StatusCode);

            var validOnDeleted = await Http.PutAsJsonAsync($"{OrdersBase}/api/orders/{orderId}/status",
                new { status = "Shipped", priority = "High" });
            Assert.Equal(404, (int)validOnDeleted.StatusCode);
            deleted = Assert.Single((JsonArray)(await ListOrdersAsync(true))["items"]!, o => (int)o!["id"]! == orderId);
            Assert.Equal("Shipped", (string)deleted!["status"]!);
        }
    }
}

using System.ComponentModel.DataAnnotations;
using System.Linq;
using Xunit;

namespace CoreWebForms.CharacterizationTests
{
    public class OrderServiceDeleteOrderTests : DatabaseTest
    {
        [Fact]
        public void DeleteOrder_RestoresStock_AndReactivatesDrainedProduct()
        {
            var id = AppData.Services.Products.Add(NewProduct(stock: 5, isActive: true));
            var orderId = AppData.Services.Orders.PlaceOrder(NewOrder(NewItem(id, 5, 5m)));
            Assert.False(AppData.Services.Products.GetById(id)!.IsActive);

            AppData.Services.Orders.DeleteOrder(orderId);

            var product = AppData.Services.Products.GetById(id)!;
            Assert.Equal(5, product.Stock);
            Assert.True(product.IsActive);
        }

        [Fact]
        public void DeleteOrder_SoftDeletedProduct_RestoresStock_WithoutReactivating()
        {
            var id = AppData.Services.Products.Add(NewProduct(stock: 5, isActive: true));
            var orderId = AppData.Services.Orders.PlaceOrder(NewOrder(NewItem(id, 5, 5m)));
            AppData.Services.Products.Delete(id);

            AppData.Services.Orders.DeleteOrder(orderId);

            var product = AppData.Services.Products.GetById(id)!;
            Assert.Equal(5, product.Stock);
            Assert.True(product.IsDeleted);
            Assert.False(product.IsActive);
        }

        [Fact]
        public void DeleteOrder_ReactivatesManuallyDeactivatedProduct()
        {
            var id = AppData.Services.Products.Add(NewProduct(stock: 10, isActive: true));

            var product = AppData.Services.Products.GetById(id)!;
            product.IsActive = false;
            AppData.Services.Products.Update(product);

            var orderId = AppData.Services.Orders.PlaceOrder(NewOrder(NewItem(id, 2, 5m)));
            Assert.False(AppData.Services.Products.GetById(id)!.IsActive);

            AppData.Services.Orders.DeleteOrder(orderId);

            var restored = AppData.Services.Products.GetById(id)!;
            Assert.Equal(10, restored.Stock);
            Assert.True(restored.IsActive);
        }

        [Fact]
        public void DeleteOrder_MarksOrderDeleted_AndHidesFromDefaultListing()
        {
            var id = AppData.Services.Products.Add(NewProduct(stock: 10, isActive: true));
            var orderId = AppData.Services.Orders.PlaceOrder(NewOrder(NewItem(id, 1, 5m)));

            AppData.Services.Orders.DeleteOrder(orderId);

            Assert.DoesNotContain(AppData.Services.Orders.GetAll(false), o => o.Id == orderId);
            var deleted = AppData.Services.Orders.GetAll(true).Single(o => o.Id == orderId);
            Assert.True(deleted.IsDeleted);
        }

        [Fact]
        public void DeleteOrder_Twice_IsNoop()
        {
            var id = AppData.Services.Products.Add(NewProduct(stock: 10, isActive: true));
            var orderId = AppData.Services.Orders.PlaceOrder(NewOrder(NewItem(id, 3, 5m)));

            AppData.Services.Orders.DeleteOrder(orderId);
            AppData.Services.Orders.DeleteOrder(orderId);

            Assert.Equal(10, AppData.Services.Products.GetById(id)!.Stock);
        }

        [Fact]
        public void UpdateStatus_DoesNotTouchStockOrActiveFlag()
        {
            var id = AppData.Services.Products.Add(NewProduct(stock: 10, isActive: true));
            var orderId = AppData.Services.Orders.PlaceOrder(NewOrder(NewItem(id, 3, 5m)));

            AppData.Services.Orders.UpdateStatus(orderId, "Shipped", "High");

            var order = AppData.Services.Orders.GetById(orderId)!;
            Assert.Equal("Shipped", order.Status);
            Assert.Equal("High", order.Priority);

            var product = AppData.Services.Products.GetById(id)!;
            Assert.Equal(7, product.Stock);
            Assert.True(product.IsActive);
        }

        [Fact]
        public void UpdateStatus_OnDeletedOrder_IsSilentlyRejected()
        {
            var id = AppData.Services.Products.Add(NewProduct(stock: 10, isActive: true));
            var orderId = AppData.Services.Orders.PlaceOrder(NewOrder(NewItem(id, 3, 5m)));
            AppData.Services.Orders.DeleteOrder(orderId);

            AppData.Services.Orders.UpdateStatus(orderId, "Shipped", "High");

            var order = AppData.Services.Orders.GetAll(true).Single(o => o.Id == orderId);
            Assert.Equal("Pending", order.Status);
        }

        [Fact]
        public void UpdateStatus_WithInvalidStatusOrPriority_ThrowsValidationException()
        {
            var id = AppData.Services.Products.Add(NewProduct(stock: 10, isActive: true));
            var orderId = AppData.Services.Orders.PlaceOrder(NewOrder(NewItem(id, 1, 5m)));

            Assert.Throws<ValidationException>(() =>
                AppData.Services.Orders.UpdateStatus(orderId, "Bogus", "Normal"));
            Assert.Throws<ValidationException>(() =>
                AppData.Services.Orders.UpdateStatus(orderId, "Shipped", "Urgent"));

            var order = AppData.Services.Orders.GetById(orderId)!;
            Assert.Equal("Pending", order.Status);
            Assert.Equal("Normal", order.Priority);
        }

        [Fact]
        public void UpdateStatus_WithInvalidStatus_OnDeletedOrder_ThrowsValidationException()
        {
            var id = AppData.Services.Products.Add(NewProduct(stock: 10, isActive: true));
            var orderId = AppData.Services.Orders.PlaceOrder(NewOrder(NewItem(id, 1, 5m)));
            AppData.Services.Orders.DeleteOrder(orderId);

            Assert.Throws<ValidationException>(() =>
                AppData.Services.Orders.UpdateStatus(orderId, "Bogus", "Normal"));
        }
    }
}

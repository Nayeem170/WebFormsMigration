using System;
using System.Linq;
using Xunit;

namespace CoreWebForms.CharacterizationTests
{
    public class OrderServicePlaceOrderTests : DatabaseTest
    {
        [Fact]
        public void PlaceOrder_DecrementsStock_AndKeepsProductActive()
        {
            var id = AppData.Services.Products.Add(NewProduct(stock: 10, isActive: true));

            var orderId = AppData.Services.Orders.PlaceOrder(NewOrder(NewItem(id, 3, 5m)));

            var product = AppData.Services.Products.GetById(id)!;
            Assert.Equal(7, product.Stock);
            Assert.True(product.IsActive);
            Assert.True(orderId > 0);
        }

        [Fact]
        public void PlaceOrder_DrainingToZeroStock_DeactivatesProduct()
        {
            var id = AppData.Services.Products.Add(NewProduct(stock: 5, isActive: true));

            AppData.Services.Orders.PlaceOrder(NewOrder(NewItem(id, 5, 5m)));

            var product = AppData.Services.Products.GetById(id)!;
            Assert.Equal(0, product.Stock);
            Assert.False(product.IsActive);
        }

        [Fact]
        public void PlaceOrder_ComputesTotal_AndPersistsDenormalizedItems()
        {
            var id1 = AppData.Services.Products.Add(NewProduct(stock: 10, isActive: true));
            var id2 = AppData.Services.Products.Add(NewProduct(stock: 10, isActive: true));

            var orderId = AppData.Services.Orders.PlaceOrder(NewOrder(
                NewItem(id1, 2, 3m, productName: "First Product"),
                NewItem(id2, 1, 5m, productName: "Second Product")));

            var order = AppData.Services.Orders.GetById(orderId)!;
            Assert.Equal(11m, order.Total);

            var items = AppData.Services.Orders.GetItems(orderId);
            Assert.Equal(2, items.Count);
            Assert.Contains(items, i => i.ProductName == "First Product" && i.UnitPrice == 3m && i.Quantity == 2);
            Assert.Contains(items, i => i.ProductName == "Second Product" && i.UnitPrice == 5m && i.Quantity == 1);
        }

        [Fact]
        public void PlaceOrder_InsufficientStock_Throws_AndRollsBackOrder()
        {
            var id = AppData.Services.Products.Add(NewProduct(stock: 2, isActive: true));
            var ordersBefore = AppData.Services.Orders.Count();

            Assert.Throws<InvalidOperationException>(() =>
                AppData.Services.Orders.PlaceOrder(NewOrder(NewItem(id, 3, 5m))));

            Assert.Equal(ordersBefore, AppData.Services.Orders.Count());
            Assert.Equal(2, AppData.Services.Products.GetById(id)!.Stock);
        }

        [Fact]
        public void PlaceOrder_UnknownProduct_Throws_AndRollsBackOrder()
        {
            var ordersBefore = AppData.Services.Orders.Count();

            Assert.Throws<InvalidOperationException>(() =>
                AppData.Services.Orders.PlaceOrder(NewOrder(NewItem(999999, 1, 5m))));

            Assert.Equal(ordersBefore, AppData.Services.Orders.Count());
        }

        [Fact]
        public void PlaceOrder_OnSoftDeletedProduct_Succeeds()
        {
            var id = AppData.Services.Products.Add(NewProduct(stock: 5, isActive: true));
            AppData.Services.Products.Delete(id);

            AppData.Services.Orders.PlaceOrder(NewOrder(NewItem(id, 1, 5m)));

            var product = AppData.Services.Products.GetById(id)!;
            Assert.Equal(4, product.Stock);
            Assert.True(product.IsDeleted);
            Assert.False(product.IsActive);
        }
    }
}

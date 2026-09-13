using System.Linq;
using Xunit;

namespace CoreWebForms.CharacterizationTests
{
    public class DashboardSmokeTests : DatabaseTest
    {
        [Fact]
        public void FreshDatabase_SeedsProductsAndOrders()
        {
            Assert.NotEmpty(AppData.Services.Products.GetAll());
            Assert.True(AppData.Services.Orders.Count() > 0);
        }

        [Fact]
        public void Dashboard_CompositionCalls_SucceedTogether()
        {
            var products = AppData.Services.Products.GetAll();
            var recent = AppData.Services.Orders.GetRecent(6);
            var totalOrders = AppData.Services.Orders.Count();
            var pendingOrders = AppData.Services.Orders.CountByStatus("Pending");

            Assert.NotEmpty(products);
            Assert.True(recent.Count <= 6);
            Assert.True(totalOrders >= recent.Count);
            Assert.True(pendingOrders >= 0);
        }
    }
}

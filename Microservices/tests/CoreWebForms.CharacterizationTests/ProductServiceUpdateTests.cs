using System.ComponentModel.DataAnnotations;
using Xunit;

namespace CoreWebForms.CharacterizationTests
{
    public class ProductServiceUpdateTests : DatabaseTest
    {
        [Fact]
        public void Update_WithZeroStock_DeactivatesProduct()
        {
            var id = AppData.Services.Products.Add(NewProduct(stock: 5, isActive: true));

            var product = AppData.Services.Products.GetById(id)!;
            product.Stock = 0;
            AppData.Services.Products.Update(product);

            Assert.False(AppData.Services.Products.GetById(id)!.IsActive);
        }

        [Fact]
        public void Update_WithPositiveStock_PreservesActiveFlag()
        {
            var id = AppData.Services.Products.Add(NewProduct(stock: 5, isActive: true));

            var product = AppData.Services.Products.GetById(id)!;
            product.Stock = 7;
            AppData.Services.Products.Update(product);

            var reloaded = AppData.Services.Products.GetById(id)!;
            Assert.True(reloaded.IsActive);
            Assert.Equal(7, reloaded.Stock);
        }

        [Fact]
        public void Update_WithPositiveStock_PreservesInactiveFlag()
        {
            var id = AppData.Services.Products.Add(NewProduct(stock: 5, isActive: false));

            var product = AppData.Services.Products.GetById(id)!;
            product.Stock = 7;
            AppData.Services.Products.Update(product);

            Assert.False(AppData.Services.Products.GetById(id)!.IsActive);
        }

        [Fact]
        public void Update_WithNegativeStock_ThrowsValidationException()
        {
            var id = AppData.Services.Products.Add(NewProduct(stock: 5, isActive: true));

            var product = AppData.Services.Products.GetById(id)!;
            product.Stock = -1;

            Assert.Throws<ValidationException>(() => AppData.Services.Products.Update(product));
        }
    }
}

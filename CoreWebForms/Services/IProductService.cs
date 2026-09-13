using System.Collections.Generic;

namespace CoreWebForms.Services
{
    public interface IProductService
    {
        List<Product> GetAll(bool includeDeleted = false);
        Product? GetById(int id);
        int Add(Product product);
        void Update(Product product);
        void Delete(int id);
    }
}

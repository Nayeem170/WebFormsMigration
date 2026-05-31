using System.Collections.Generic;

namespace LegacyWebForms.Core
{
    public interface IProductRepository
    {
        List<Product> GetAll(bool includeDeleted = false);
        Product GetById(int id);
        int Add(Product product);
        void Update(Product product);
        void Delete(int id);
    }
}

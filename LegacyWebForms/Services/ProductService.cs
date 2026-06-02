using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using LegacyWebForms.Core;
using LegacyWebForms.Data;

namespace LegacyWebForms.Services
{
    public class ProductService
    {
        private readonly IProductRepository _repo;
        private readonly ILogger _log;

        public ProductService(IProductRepository repo, ILogger log)
        {
            _repo = repo;
            _log = log;
        }

        public List<Product> GetAll(bool includeDeleted = false)
        {
            return _repo.GetAll(includeDeleted);
        }

        public Product GetById(int id)
        {
            return _repo.GetById(id);
        }

        public int Add(Product product)
        {
            if (product == null) throw new ArgumentNullException(nameof(product));
            Validate(product);
            _log.Info(string.Format("Adding product: {0}", product.Name));
            return _repo.Add(product);
        }

        public void Update(Product product)
        {
            if (product == null) throw new ArgumentNullException(nameof(product));
            Validate(product);
            if (product.Stock == 0)
                product.IsActive = false;
            _log.Info(string.Format("Updating product #{0}", product.Id));
            _repo.Update(product);
        }

        private static void Validate(object model)
        {
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(model, new ValidationContext(model), results, true))
                throw new ValidationException(string.Join("; ", results.Select(r => r.ErrorMessage)));
        }

        public void Delete(int id)
        {
            _log.Info(string.Format("Soft-deleting product #{0}", id));
            _repo.Delete(id);
        }
    }
}

using System.Collections.Generic;

namespace CoreWebForms.Services
{
    public interface IOrderService
    {
        List<Order> GetAll(bool includeDeleted = false);
        List<Order> GetAll(bool includeDeleted, string? status);
        Order? GetById(int id);
        int Count(bool includeDeleted = false);
        int Count(bool includeDeleted, string? status);
        int CountByStatus(string status);
        List<Order> GetRecent(int count);
        List<OrderItem> GetItems(int orderId);
        List<Order> GetPaged(int skip, int take, bool includeDeleted, string? status);
        int PlaceOrder(Order order);
        void UpdateStatus(int orderId, string status, string priority);
        void DeleteOrder(int id);
    }
}

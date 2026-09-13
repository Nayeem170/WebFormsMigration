namespace Inventory.Contracts
{
    public class OrderDto
    {
        public int Id { get; set; }
        public string CustomerName { get; set; } = null!;
        public string CustomerEmail { get; set; } = null!;
        public DateTime OrderDate { get; set; }
        public DateTime DeliveryDate { get; set; }
        public string Status { get; set; } = null!;
        public string Priority { get; set; } = null!;
        public List<string> Extras { get; set; } = new();
        public decimal Total { get; set; }
        public bool IsDeleted { get; set; }
        public List<OrderItemDto> Items { get; set; } = new();
    }
}

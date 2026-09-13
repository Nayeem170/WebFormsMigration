using System.ComponentModel.DataAnnotations;

namespace Inventory.Contracts
{
    public class StockItemDto
    {
        [Range(1, 999999)]
        public int ProductId { get; set; }

        [Range(1, 999999)]
        public int Quantity { get; set; }
    }
}

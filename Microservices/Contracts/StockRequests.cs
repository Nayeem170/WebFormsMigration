using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Inventory.Contracts
{
    public class ReserveStockRequest
    {
        [Required]
        [StringLength(64, MinimumLength = 1)]
        public string ReservationKey { get; set; } = null!;

        [ValidateItems]
        public List<StockItemDto> Items { get; set; } = new();
    }

    public class ReleaseStockRequest
    {
        [Required]
        [StringLength(64, MinimumLength = 1)]
        public string ReleaseKey { get; set; } = null!;

        [ValidateItems]
        public List<StockItemDto> Items { get; set; } = new();
    }

    public class ValidateItemsAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value)
        {
            return value is List<StockItemDto> items && items.Count > 0;
        }

        public override string FormatErrorMessage(string name)
        {
            return "At least one stock item is required.";
        }
    }
}

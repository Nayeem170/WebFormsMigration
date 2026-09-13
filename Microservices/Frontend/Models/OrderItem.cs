using System;
using System.ComponentModel.DataAnnotations;

namespace CoreWebForms
{
    [Serializable]
    public class OrderItem
    {
        public int Id { get; set; }

        public int OrderId { get; set; }

        public int ProductId { get; set; }

        [Required]
        [StringLength(200)]
        public string ProductName { get; set; } = null!;

        [Range(1, 999999)]
        public int Quantity { get; set; }

        [Range(0, 99999)]
        public decimal UnitPrice { get; set; }

        public decimal LineTotal => Quantity * UnitPrice;
    }
}

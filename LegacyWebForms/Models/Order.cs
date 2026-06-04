using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace LegacyWebForms
{
    public class Order
    {
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        public string CustomerName { get; set; } = null!;

        [Required]
        [EmailAddress]
        [StringLength(200)]
        public string CustomerEmail { get; set; } = null!;

        public DateTime OrderDate { get; set; }
        public DateTime DeliveryDate { get; set; }

        [Required]
        [RegularExpression("\\A(Pending|Processing|Shipped|Delivered)\\z")]
        public string Status { get; set; } = null!;

        [Required]
        [RegularExpression("\\A(Low|Normal|High)\\z")]
        public string Priority { get; set; } = null!;

        public List<string> Extras { get; set; } = new List<string>();

        [Range(0, 999999)]
        public decimal Total { get; set; }

        public bool IsDeleted { get; set; }

        public List<OrderItem> Items { get; set; } = new List<OrderItem>();
    }
}

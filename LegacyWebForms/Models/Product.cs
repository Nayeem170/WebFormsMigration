using System;
using System.ComponentModel.DataAnnotations;
namespace LegacyWebForms
{
    public class Product
    {
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = null!;

        [Required]
        [StringLength(100)]
        public string Category { get; set; } = null!;

        [Range(0, 99999)]
        public decimal Price { get; set; }

        [Range(0, 999999)]
        public int Stock { get; set; }

        public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }

        public DateTime AddedDate { get; set; }

    }
}

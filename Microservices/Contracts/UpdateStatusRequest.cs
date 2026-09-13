using System.ComponentModel.DataAnnotations;

namespace Inventory.Contracts
{
    public class UpdateStatusRequest
    {
        [Required]
        [RegularExpression("\\A(Pending|Processing|Shipped|Delivered)\\z")]
        public string Status { get; set; } = null!;

        [Required]
        [RegularExpression("\\A(Low|Normal|High)\\z")]
        public string Priority { get; set; } = null!;
    }
}

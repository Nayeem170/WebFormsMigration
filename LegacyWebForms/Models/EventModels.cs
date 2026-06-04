using System;

namespace LegacyWebForms
{
    public class ProductEventArgs : EventArgs
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = null!;
    }

    public class OrderEventArgs : EventArgs
    {
        public int OrderId { get; set; }
    }
}

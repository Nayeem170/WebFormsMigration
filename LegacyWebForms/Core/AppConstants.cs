using System.Collections.Generic;

namespace LegacyWebForms
{
    public static class AppConstants
    {
        public static IReadOnlyList<string> Categories { get; } = new List<string>
        {
            "Electronics", "Clothing", "Food", "Books", "Sports"
        }.AsReadOnly();

        public static class OrderStatus
        {
            public const string Pending    = "Pending";
            public const string Processing = "Processing";
            public const string Shipped    = "Shipped";
            public const string Delivered  = "Delivered";
        }

        public static class UiLabels
        {
            public const string Deleted = "Deleted";
        }

        public static class OrderPriority
        {
            public const string Low = "Low";
            public const string Normal = "Normal";
            public const string High = "High";
        }
    }
}

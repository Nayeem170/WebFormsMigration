namespace Inventory.Contracts
{
    public class ApiErrorResponse
    {
        public string ErrorCode { get; set; } = null!;
        public string Message { get; set; } = null!;
    }
}

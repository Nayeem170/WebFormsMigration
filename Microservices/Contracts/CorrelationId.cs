namespace Inventory.Contracts
{
    public static class CorrelationId
    {
        // The gateway mints 32-hex (Guid "N"). A client-settable logged
        // field must match the mint format exactly or be replaced: newline
        // injection and forged log/trace attributes start with trusting
        // this header.
        public static bool IsValid(string? value) =>
            value is not null && value.Length == 32 && value.All(Uri.IsHexDigit);
    }
}

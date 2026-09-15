using Inventory.Contracts;
using Xunit;

namespace CoreWebForms.UnitTests
{
    public class CorrelationIdTests
    {
        [Fact]
        public void Valid_32LowercaseHex()
        {
            Assert.True(CorrelationId.IsValid("0123456789abcdef0123456789abcdef"));
        }

        [Fact]
        public void Valid_32UppercaseHex()
        {
            Assert.True(CorrelationId.IsValid("0123456789ABCDEF0123456789ABCDEF"));
        }

        [Fact]
        public void Invalid_31Chars()
        {
            Assert.False(CorrelationId.IsValid("0123456789abcdef0123456789abcde"));
        }

        [Fact]
        public void Invalid_33Chars()
        {
            Assert.False(CorrelationId.IsValid("0123456789abcdef0123456789abcdeff"));
        }

        [Fact]
        public void Invalid_NonHexCharacter()
        {
            Assert.False(CorrelationId.IsValid("0123456789abcdef0123456789abcdeg"));
        }

        [Fact]
        public void Invalid_NewlineInjection()
        {
            Assert.False(CorrelationId.IsValid("0123456789abcdef0123456789abcd\n"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Invalid_MissingOrWhitespace(string? value)
        {
            Assert.False(CorrelationId.IsValid(value));
        }
    }
}

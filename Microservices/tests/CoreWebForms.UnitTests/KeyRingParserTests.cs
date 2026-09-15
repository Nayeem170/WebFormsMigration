using Inventory.Contracts;
using Xunit;

namespace CoreWebForms.UnitTests
{
    public class KeyRingParserTests
    {
        private static string KeyXml(string id, string created) =>
            $"<key id=\"{id}\" version=\"1\"><creationDate>{created}</creationDate><activationDate>{created}</activationDate></key>";

        [Fact]
        public void Parse_CountsKeys_AndPicksNewest()
        {
            var summary = KeyRingParser.Parse(new[]
            {
                KeyXml("older", "2026-09-01T10:00:00Z"),
                KeyXml("newer", "2026-09-02T10:00:00Z")
            });
            Assert.Equal(2, summary.KeyCount);
            Assert.Equal("newer", summary.ActiveKeyId);
            Assert.Equal(0, summary.Malformed);
        }

        [Fact]
        public void Parse_ToleratesMalformedEntries()
        {
            var summary = KeyRingParser.Parse(new[]
            {
                "not xml at all <",
                "",
                KeyXml("only-good", "2026-09-03T10:00:00Z")
            });
            Assert.Equal(1, summary.KeyCount);
            Assert.Equal(2, summary.Malformed);
            Assert.Equal("only-good", summary.ActiveKeyId);
        }

        [Fact]
        public void Parse_EmptyRing()
        {
            var summary = KeyRingParser.Parse(System.Array.Empty<string>());
            Assert.Equal(0, summary.KeyCount);
            Assert.Null(summary.ActiveKeyId);
            Assert.Equal(0, summary.ActiveFingerprint);
        }

        [Fact]
        public void Fingerprint_StableAndPositive()
        {
            var a = KeyRingParser.Fingerprint("d1e2f3a4-b5c6-7890-abcd-ef1234567890");
            var b = KeyRingParser.Fingerprint("d1e2f3a4-b5c6-7890-abcd-ef1234567890");
            var c = KeyRingParser.Fingerprint("00000000-0000-0000-0000-000000000000");
            Assert.Equal(a, b);
            Assert.NotEqual(a, c);
            Assert.True(a > 0);
        }
    }
}

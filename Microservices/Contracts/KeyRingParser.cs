using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Inventory.Contracts
{
    /// <summary>
    /// Parses the raw XML values of the shared DataProtection key ring
    /// (as stored by PersistKeysToStackExchangeRedis). Pure so the skew
    /// signal - key count and newest-key identity - is unit-testable.
    /// </summary>
    public static class KeyRingParser
    {
        public static KeyRingSummary Parse(IEnumerable<string> keyXmlValues)
        {
            var keys = new List<(string Id, DateTimeOffset Created)>();
            var malformed = 0;
            foreach (var value in keyXmlValues ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(value)) { malformed++; continue; }
                try
                {
                    var xml = XDocument.Parse(value).Root ?? throw new InvalidOperationException("empty document");
                    var id = (string?)xml.Attribute("id");
                    var created = (DateTimeOffset?)xml.Element("creationDate");
                    if (id == null || created == null) { malformed++; continue; }
                    keys.Add((id, created.Value));
                }
                catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException or FormatException)
                {
                    malformed++;
                }
            }
            // The active key is the newest by creation date (ties: last id).
            var active = keys.OrderByDescending(k => k.Created).ThenBy(k => k.Id).FirstOrDefault();
            return new KeyRingSummary(
                keys.Count,
                active.Id is null ? 0 : Fingerprint(active.Id),
                active.Id,
                malformed);
        }

        /// <summary>
        /// Stable 31-bit hash of a key id so the active-key signal can ride
        /// a numeric gauge; the full id goes to logs.
        /// </summary>
        public static int Fingerprint(string id)
        {
            unchecked
            {
                var hash = (int)2166136261;
                foreach (var c in id) hash = (hash ^ c) * 16777619;
                return hash & 0x7FFFFFFF;
            }
        }
    }

    public readonly record struct KeyRingSummary(int KeyCount, int ActiveFingerprint, string? ActiveKeyId, int Malformed);
}

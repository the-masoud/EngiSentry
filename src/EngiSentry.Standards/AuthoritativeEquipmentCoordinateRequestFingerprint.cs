using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>
    /// The canonical <c>RequestFingerprint</c> of an engineer-facing coordinate workbook.
    ///
    /// This is an integrity and binding fingerprint only. It is NOT a digital signature, it does NOT
    /// prove the engineer's identity, and it is NOT cryptographic authorization. It exists so that a
    /// workbook can be bound to the exact drawing revision and the exact set of equipment occurrences
    /// it was generated for, so that a workbook generated for one drawing can never be imported into
    /// another, and so that a row cannot be added, removed or re-identified without detection.
    ///
    /// Canonical serialization (documented and unambiguous):
    /// <code>
    /// line 1 : &lt;SchemaVersion&gt;
    /// line 2 : &lt;DrawingSha256&gt;
    /// line 3..n : &lt;OccurrenceId&gt; U+001F &lt;EquipmentTag&gt;   (one line per pair)
    /// </code>
    /// Each line is terminated by a single LF (U+000A). The whole payload is UTF-8 encoded with no byte
    /// order mark, and the fingerprint is the uppercase hexadecimal SHA-256 of those bytes.
    ///
    /// The pairs are sorted ordinally by <c>OccurrenceId</c>, so the fingerprint does not depend on row
    /// order or Excel row number. It deliberately excludes Easting, Northing, Elevation, every
    /// source-document metadata field, Notes, and GeneratedUtc — the engineer is expected to change the
    /// coordinates, and that must never invalidate the request.
    /// </summary>
    public static class AuthoritativeEquipmentCoordinateRequestFingerprint
    {
        /// <summary>The unit separator that divides the two identity fields inside one canonical line.</summary>
        internal const char FieldSeparator = '\u001F';

        /// <summary>
        /// Computes the fingerprint from the workbook schema version, the drawing SHA-256, and the
        /// expected occurrence/tag pairs. Blank occurrence ids are ignored, exactly as the request
        /// generator ignores them.
        /// </summary>
        public static string Compute(string? schemaVersion, string? drawingSha256, IEnumerable<KeyValuePair<string, string>>? pairs)
        {
            var builder = new StringBuilder();
            builder.Append((schemaVersion ?? string.Empty).Trim()).Append('\n');
            builder.Append((drawingSha256 ?? string.Empty).Trim()).Append('\n');

            if (pairs != null)
            {
                foreach (KeyValuePair<string, string> pair in pairs
                    .Where(p => p.Key != null && p.Key.Trim().Length > 0)
                    .Select(p => new KeyValuePair<string, string>(p.Key.Trim(), (p.Value ?? string.Empty).Trim()))
                    .OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    builder.Append(pair.Key).Append(FieldSeparator).Append(pair.Value).Append('\n');
                }
            }

            byte[] payload = new UTF8Encoding(false).GetBytes(builder.ToString());
            return AuthoritativeEquipmentCoordinateHashing.ComputeSha256(payload);
        }

        /// <summary>Convenience overload for a workbook model's rows.</summary>
        public static string Compute(string? schemaVersion, string? drawingSha256, IEnumerable<AuthoritativeEquipmentCoordinateWorkbookRow>? rows)
            => Compute(schemaVersion, drawingSha256,
                rows?.Select(r => new KeyValuePair<string, string>(r.OccurrenceId ?? string.Empty, r.EquipmentTag ?? string.Empty)));

        /// <summary>Convenience overload for a drawing inventory.</summary>
        public static string Compute(string? schemaVersion, string? drawingSha256, IEnumerable<AuthoritativeEquipmentCoordinateInventoryEntry>? inventory)
            => Compute(schemaVersion, drawingSha256,
                inventory?.Select(e => new KeyValuePair<string, string>(e.OccurrenceId ?? string.Empty, e.EquipmentTag ?? string.Empty)));
    }
}

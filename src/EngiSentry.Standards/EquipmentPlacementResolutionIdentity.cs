using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>
    /// Deterministic identity for an engineer placement resolution.
    ///
    /// ResolutionId is the canonical SHA-256 over exactly the drawing hash, the equipment identity,
    /// the exact stored Easting and Northing, and the insertion units — nothing else. A changed
    /// drawing hash, equipment identity, coordinate, or unit therefore yields a different
    /// ResolutionId, so a stale confirmation can never be mistaken for a current one.
    ///
    /// RecordId is the canonical SHA-256 over the ResolutionId, the request evidence hash, the
    /// review response identity, the reviewer, and the UTC response timestamp. Every distinct
    /// confirmation therefore has its own RecordId, and prior confirmations are never overwritten.
    /// </summary>
    public static class EquipmentPlacementResolutionIdentity
    {
        public static string ComputeResolutionId(EquipmentPlacementResolutionRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var parts = new[]
            {
                record.DrawingSha256 ?? string.Empty,
                record.EquipmentEntityId ?? string.Empty,
                CanonicalDecimal(record.Easting),
                CanonicalDecimal(record.Northing),
                record.InsertionUnits ?? string.Empty
            }.ToList();
            if (string.Equals(record.Source, EquipmentPlacementResolutionSources.EngineerSelectedDwgPoint, StringComparison.Ordinal))
                parts.Add(record.OccurrenceId ?? string.Empty);
            return HexSha256(string.Join("\n", parts));
        }

        public static string ComputeRecordId(EquipmentPlacementResolutionRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            return HexSha256(string.Join("\n", new[]
            {
                record.ResolutionId ?? string.Empty,
                record.RequestSha256 ?? string.Empty,
                record.ResponseId ?? string.Empty,
                record.ReviewerDisplayName ?? string.Empty,
                NormalizeTimestamp(record.ResponseTimestampUtc).ToString("O", CultureInfo.InvariantCulture),
                record.ProposalId ?? string.Empty,
                record.ProposalFingerprint ?? string.Empty,
                record.DispositionRecordId ?? string.Empty,
                record.SelectedPlacementFingerprint ?? string.Empty
            }));
        }

        /// <summary>
        /// The persisted timestamp precision. Records are round-tripped through JSON, which stores
        /// <see cref="DateTime"/> at millisecond precision. The confirmation timestamp is therefore
        /// normalized to that precision before it is stored and before it is hashed into the
        /// <see cref="EquipmentPlacementResolutionRecord.RecordId"/>: a record built from a
        /// sub-millisecond clock reading (for example <c>DateTime.UtcNow</c>) would otherwise fail
        /// to re-verify after it is read back from disk.
        /// </summary>
        public static DateTime NormalizeTimestamp(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
        }

        /// <summary>
        /// Exact, exponent-free, trailing-zero-free fixed-point rendering of a decimal, so the same
        /// stored value always hashes to the same identity regardless of its decimal scale.
        /// </summary>
        public static string CanonicalDecimal(decimal value)        {
            string text = value.ToString("0." + new string('#', 28), CultureInfo.InvariantCulture);
            return text.Length == 0 ? "0" : text;
        }

        private static string HexSha256(string canonical)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", string.Empty);
        }
    }
}

using System;
using System.Text.RegularExpressions;

namespace EngiSentry.Contracts
{
    public enum EquipmentTagGroupKind
    {
        None,
        SlashList,
        AlphabeticRange
    }

    public sealed class EquipmentTagParseResult
    {
        internal EquipmentTagParseResult(string prefix, string? unitNumber, string sequenceNumber, string baseNormalizedTag, string? suffixExpression, string normalizedTag, EquipmentTagGroupKind groupKind)
        {
            Prefix = prefix;
            UnitNumber = unitNumber;
            SequenceNumber = sequenceNumber;
            BaseNormalizedTag = baseNormalizedTag;
            SuffixExpression = suffixExpression;
            NormalizedTag = normalizedTag;
            GroupKind = groupKind;
        }

        public string Prefix { get; }
        public string? UnitNumber { get; }
        public string SequenceNumber { get; }
        public string BaseNormalizedTag { get; }
        public string? SuffixExpression { get; }
        public string NormalizedTag { get; }
        public EquipmentTagGroupKind GroupKind { get; }
        public bool IsGrouped => GroupKind != EquipmentTagGroupKind.None;
    }

    public static class EquipmentTagGrammar
    {
        private static readonly Regex ThreePart = new Regex(@"^\s*([A-Za-z]{1,6})\s*-\s*([0-9]+)\s*-\s*([0-9]+)(?:\s*([A-Za-z][A-Za-z0-9]*(?:\s*/\s*[A-Za-z][A-Za-z0-9]*)*|[A-Za-z]\s*~\s*[A-Za-z]))?\s*$", RegexOptions.Compiled);
        private static readonly Regex Compact = new Regex(@"^\s*([A-Za-z]{1,6})(?:\s*-\s*|)([0-9]{3,6})(?:\s*([A-Za-z][A-Za-z0-9]*(?:\s*/\s*[A-Za-z][A-Za-z0-9]*)*|[A-Za-z]\s*~\s*[A-Za-z]))?\s*$", RegexOptions.Compiled);

        public static bool TryParse(string? value, out EquipmentTagParseResult result)
        {
            result = null!;
            Match match = ThreePart.Match(value ?? string.Empty);
            string prefix;
            string? unitNumber;
            string sequenceNumber;
            string? suffix;
            if (match.Success)
            {
                prefix = match.Groups[1].Value.ToUpperInvariant();
                unitNumber = match.Groups[2].Value;
                sequenceNumber = match.Groups[3].Value;
                suffix = match.Groups[4].Success ? NormalizeSuffix(match.Groups[4].Value) : null;
            }
            else
            {
                match = Compact.Match(value ?? string.Empty);
                if (!match.Success) return false;
                prefix = match.Groups[1].Value.ToUpperInvariant();
                unitNumber = null;
                sequenceNumber = match.Groups[2].Value;
                suffix = match.Groups[3].Success ? NormalizeSuffix(match.Groups[3].Value) : null;
            }

            EquipmentTagGroupKind groupKind = suffix == null ? EquipmentTagGroupKind.None : suffix.IndexOf('/') >= 0 ? EquipmentTagGroupKind.SlashList : suffix.IndexOf('~') >= 0 ? EquipmentTagGroupKind.AlphabeticRange : EquipmentTagGroupKind.None;
            string baseNormalizedTag = unitNumber == null ? prefix + "-" + sequenceNumber : prefix + "-" + unitNumber + "-" + sequenceNumber;
            result = new EquipmentTagParseResult(prefix, unitNumber, sequenceNumber, baseNormalizedTag, suffix, baseNormalizedTag + (suffix ?? string.Empty), groupKind);
            return true;
        }

        public static EquipmentTagParseResult Parse(string? value)
        {
            EquipmentTagParseResult result;
            if (!TryParse(value, out result)) throw new FormatException("invalid equipment tag: " + (value ?? string.Empty));
            return result;
        }

        private static string NormalizeSuffix(string value) => Regex.Replace(value, @"\s+", string.Empty).ToUpperInvariant();
    }
}

using System;
using System.Collections.Generic;

namespace EngiSentry.Contracts
{
    public sealed class EquipmentTagExpansionMember
    {
        internal EquipmentTagExpansionMember(string sourceNormalizedTag, string baseNormalizedTag, string memberSuffix, string normalizedMemberTag, EquipmentTagGroupKind groupKind, string expansionMethod, int memberOrdinal)
        {
            SourceNormalizedTag = sourceNormalizedTag;
            BaseNormalizedTag = baseNormalizedTag;
            MemberSuffix = memberSuffix;
            NormalizedMemberTag = normalizedMemberTag;
            GroupKind = groupKind;
            ExpansionMethod = expansionMethod;
            MemberOrdinal = memberOrdinal;
        }

        public string SourceNormalizedTag { get; }
        public string BaseNormalizedTag { get; }
        public string MemberSuffix { get; }
        public string NormalizedMemberTag { get; }
        public EquipmentTagGroupKind GroupKind { get; }
        public string ExpansionMethod { get; }
        public int MemberOrdinal { get; }
    }

    public sealed class EquipmentTagExpansionResult
    {
        internal EquipmentTagExpansionResult(IReadOnlyList<EquipmentTagExpansionMember> members, string? failureReason)
        {
            Members = members;
            FailureReason = failureReason;
        }

        public IReadOnlyList<EquipmentTagExpansionMember> Members { get; }
        public string? FailureReason { get; }
        public bool Success { get { return FailureReason == null; } }
    }

    public static class EquipmentTagExpander
    {
        public const string DirectMethod = "Direct";
        public const string GroupedSuffixExpansionMethod = "GroupedSuffixExpansion";
        public const string AlphabeticRangeExpansionMethod = "AlphabeticRangeExpansion";

        public static EquipmentTagExpansionResult Expand(EquipmentTagParseResult parsed)
        {
            if (parsed == null) throw new ArgumentNullException(nameof(parsed));
            if (parsed.GroupKind == EquipmentTagGroupKind.None)
            {
                return Success(new[] { new EquipmentTagExpansionMember(parsed.NormalizedTag, parsed.BaseNormalizedTag, parsed.SuffixExpression ?? string.Empty, parsed.NormalizedTag, parsed.GroupKind, DirectMethod, 0) });
            }
            if (parsed.GroupKind == EquipmentTagGroupKind.SlashList) return ExpandSlashList(parsed);
            if (parsed.GroupKind == EquipmentTagGroupKind.AlphabeticRange) return ExpandAlphabeticRange(parsed);
            return Failure("InvalidGroupedTag");
        }

        private static EquipmentTagExpansionResult ExpandSlashList(EquipmentTagParseResult parsed)
        {
            string[] suffixes = (parsed.SuffixExpression ?? string.Empty).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var members = new List<EquipmentTagExpansionMember>(suffixes.Length);
            for (int i = 0; i < suffixes.Length; i++)
            {
                string suffix = suffixes[i];
                string normalized = parsed.BaseNormalizedTag + suffix;
                if (!seen.Add(normalized)) return Failure("DuplicateGroupedMember");
                members.Add(new EquipmentTagExpansionMember(parsed.NormalizedTag, parsed.BaseNormalizedTag, suffix, normalized, parsed.GroupKind, GroupedSuffixExpansionMethod, i));
            }
            return members.Count == 0 ? Failure("InvalidGroupedTag") : Success(members);
        }

        private static EquipmentTagExpansionResult ExpandAlphabeticRange(EquipmentTagParseResult parsed)
        {
            string expression = parsed.SuffixExpression ?? string.Empty;
            if (expression.Length != 3 || expression[1] != '~') return Failure("InvalidAlphabeticRange");
            char start = expression[0];
            char end = expression[2];
            if (start < 'A' || start > 'Z' || end < 'A' || end > 'Z') return Failure("InvalidAlphabeticRange");
            if (start == end) return Failure("DegenerateAlphabeticRange");
            if (start > end) return Failure("DescendingAlphabeticRange");

            var members = new List<EquipmentTagExpansionMember>(end - start + 1);
            int ordinal = 0;
            for (char suffix = start; suffix <= end; suffix++)
            {
                string memberSuffix = suffix.ToString();
                members.Add(new EquipmentTagExpansionMember(parsed.NormalizedTag, parsed.BaseNormalizedTag, memberSuffix, parsed.BaseNormalizedTag + memberSuffix, parsed.GroupKind, AlphabeticRangeExpansionMethod, ordinal++));
            }
            return Success(members);
        }

        private static EquipmentTagExpansionResult Success(IReadOnlyList<EquipmentTagExpansionMember> members) => new EquipmentTagExpansionResult(members, null);
        private static EquipmentTagExpansionResult Failure(string reason) => new EquipmentTagExpansionResult(new EquipmentTagExpansionMember[0], reason);
    }
}

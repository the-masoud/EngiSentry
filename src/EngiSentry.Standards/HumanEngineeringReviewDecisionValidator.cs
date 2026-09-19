using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class HumanEngineeringReviewDecisionValidationException : Exception
    {
        public HumanEngineeringReviewDecisionValidationException(string message) : base(message) { }
    }

    public static class HumanEngineeringReviewDecisionValidator
    {
        public const string SchemaVersion = "1.0";

        public static void Validate(HumanEngineeringReviewDecisionRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (record.SchemaVersion != SchemaVersion) throw new HumanEngineeringReviewDecisionValidationException("unsupported human engineering review decision SchemaVersion: " + record.SchemaVersion);
            if (string.IsNullOrWhiteSpace(record.DecisionRecordId)) throw new HumanEngineeringReviewDecisionValidationException("human engineering review decision DecisionRecordId is required");
            if (string.IsNullOrWhiteSpace(record.DrawingName)) throw new HumanEngineeringReviewDecisionValidationException("human engineering review decision DrawingName is required");
            if (!IsSha256(record.ReviewPackageSha256)) throw new HumanEngineeringReviewDecisionValidationException("human engineering review decision ReviewPackageSha256 must be a 64-character hexadecimal SHA256");
            if (string.IsNullOrWhiteSpace(record.ReviewPackageSchemaVersion)) throw new HumanEngineeringReviewDecisionValidationException("human engineering review decision ReviewPackageSchemaVersion is required");
            if (string.IsNullOrWhiteSpace(record.RuleId)) throw new HumanEngineeringReviewDecisionValidationException("human engineering review decision RuleId is required");
            if (string.IsNullOrWhiteSpace(record.AuthorityDecisionStateAtReview)) throw new HumanEngineeringReviewDecisionValidationException("human engineering review decision AuthorityDecisionStateAtReview is required");
            if (string.IsNullOrWhiteSpace(record.EffectiveEnforcementModeAtReview)) throw new HumanEngineeringReviewDecisionValidationException("human engineering review decision EffectiveEnforcementModeAtReview is required");
            if (!IsSupportedState(record.HumanReviewState)) throw new HumanEngineeringReviewDecisionValidationException("unsupported human engineering review decision HumanReviewState: " + record.HumanReviewState);
            if (string.Equals(record.HumanReviewState, "APPROVED", StringComparison.Ordinal)) throw new HumanEngineeringReviewDecisionValidationException("APPROVED is not a supported human engineering review decision state");
            if (string.IsNullOrWhiteSpace(record.ReviewerDisplayName)) throw new HumanEngineeringReviewDecisionValidationException("human engineering review decision ReviewerDisplayName is required");
            if (record.DecisionTimestampUtc.Kind != DateTimeKind.Utc) throw new HumanEngineeringReviewDecisionValidationException("human engineering review decision DecisionTimestampUtc must be UTC");
            if (record.EvidenceBindings == null || record.EvidenceBindings.Count == 0) throw new HumanEngineeringReviewDecisionValidationException("human engineering review decision EvidenceBindings are required");
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (HumanEngineeringReviewEvidenceBinding binding in record.EvidenceBindings)
            {
                if (binding == null) throw new HumanEngineeringReviewDecisionValidationException("human engineering review decision contains a null evidence binding");
                if (string.IsNullOrWhiteSpace(binding.ClauseId)) throw new HumanEngineeringReviewDecisionValidationException("human engineering review decision evidence ClauseId is required");
                if (string.IsNullOrWhiteSpace(binding.SourceId)) throw new HumanEngineeringReviewDecisionValidationException("human engineering review decision evidence SourceId is required");
                if (string.IsNullOrWhiteSpace(binding.SourceSha256)) throw new HumanEngineeringReviewDecisionValidationException("human engineering review decision evidence SourceSha256 is required");
                if (!identities.Add(binding.ClauseId + "\u001f" + binding.SourceId + "\u001f" + binding.SourceSha256)) throw new HumanEngineeringReviewDecisionValidationException("duplicate human engineering review decision evidence binding");
            }
            if (string.Equals(record.SupersedesDecisionRecordId, record.DecisionRecordId, StringComparison.Ordinal)) throw new HumanEngineeringReviewDecisionValidationException("human engineering review decision cannot supersede itself");
        }

        private static bool IsSupportedState(string state)
        {
            return state == HumanEngineeringReviewDecisionStates.ReviewAccepted || state == HumanEngineeringReviewDecisionStates.ReviewRejected || state == HumanEngineeringReviewDecisionStates.NeedsClarification;
        }

        private static bool IsSha256(string? value)
        {
            if (value == null || value.Length != 64) return false;
            return value.All(character => (character >= '0' && character <= '9') || (character >= 'A' && character <= 'F') || (character >= 'a' && character <= 'f'));
        }
    }
}

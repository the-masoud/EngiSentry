using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class HumanEngineeringReviewDecisionStalenessValidationException : Exception
    {
        public HumanEngineeringReviewDecisionStalenessValidationException(string message) : base(message) { }
    }

    public static class HumanEngineeringReviewDecisionStalenessValidator
    {
        public static HumanEngineeringReviewDecisionEvaluation Evaluate(
            HumanEngineeringReviewDecisionRecord decision,
            StandardAuthorityReviewPackage currentPackage,
            string currentReviewPackageSha256)
        {
            if (decision == null) throw new ArgumentNullException(nameof(decision));
            if (currentPackage == null) throw new ArgumentNullException(nameof(currentPackage));
            HumanEngineeringReviewDecisionValidator.Validate(decision);
            if (!IsSha256(currentReviewPackageSha256)) throw new HumanEngineeringReviewDecisionStalenessValidationException("current review package SHA256 must be a 64-character hexadecimal SHA256");
            if (currentPackage.ReviewProjection == null || currentPackage.ReviewProjection.ReviewItems == null) throw new HumanEngineeringReviewDecisionStalenessValidationException("current review package ReviewProjection and ReviewItems are required");

            List<StandardAuthorityReviewItem> matches = currentPackage.ReviewProjection.ReviewItems.Where(item => item != null && item.RuleId == decision.RuleId).ToList();
            if (matches.Count > 1) throw new HumanEngineeringReviewDecisionStalenessValidationException("current review package contains duplicate ReviewItems for RuleId: " + decision.RuleId);

            var reasons = new List<string>();
            if (!string.Equals(decision.ReviewPackageSha256, currentReviewPackageSha256, StringComparison.OrdinalIgnoreCase)) reasons.Add(HumanEngineeringReviewDecisionEvaluationReasons.ReviewPackageShaChanged);
            if (!string.Equals(decision.ReviewPackageSchemaVersion, currentPackage.SchemaVersion, StringComparison.Ordinal)) reasons.Add(HumanEngineeringReviewDecisionEvaluationReasons.ReviewPackageSchemaChanged);
            if (!string.Equals(decision.DrawingName, currentPackage.DrawingName, StringComparison.Ordinal)) reasons.Add(HumanEngineeringReviewDecisionEvaluationReasons.DrawingChanged);
            if (decision.ScanTimestampUtc != currentPackage.ScanTimestampUtc) reasons.Add(HumanEngineeringReviewDecisionEvaluationReasons.ScanTimestampChanged);

            if (matches.Count == 0)
            {
                reasons.Add(HumanEngineeringReviewDecisionEvaluationReasons.RuleMissing);
            }
            else
            {
                StandardAuthorityReviewItem item = matches[0];
                if (item.Clauses == null || item.Clauses.Any(clause => clause == null || string.IsNullOrWhiteSpace(clause.ClauseId) || string.IsNullOrWhiteSpace(clause.SourceId) || !IsSha256(clause.SourceSha256))) throw new HumanEngineeringReviewDecisionStalenessValidationException("current review package contains incomplete clause/source evidence: " + decision.RuleId);
                if (!string.Equals(item.DecisionState, decision.AuthorityDecisionStateAtReview, StringComparison.Ordinal)) reasons.Add(HumanEngineeringReviewDecisionEvaluationReasons.AuthorityDecisionStateChanged);
                if (!string.Equals(item.EffectiveEnforcementMode, decision.EffectiveEnforcementModeAtReview, StringComparison.Ordinal)) reasons.Add(HumanEngineeringReviewDecisionEvaluationReasons.EnforcementModeChanged);
                string[] currentEvidence = item.Clauses.OrderBy(clause => clause.ClauseId, StringComparer.Ordinal).ThenBy(clause => clause.SourceId, StringComparer.Ordinal).ThenBy(clause => clause.SourceSha256, StringComparer.Ordinal).Select(clause => clause.ClauseId + "\u001f" + clause.SourceId + "\u001f" + clause.SourceSha256).ToArray();
                string[] decisionEvidence = decision.EvidenceBindings.OrderBy(binding => binding.ClauseId, StringComparer.Ordinal).ThenBy(binding => binding.SourceId, StringComparer.Ordinal).ThenBy(binding => binding.SourceSha256, StringComparer.Ordinal).Select(binding => binding.ClauseId + "\u001f" + binding.SourceId + "\u001f" + binding.SourceSha256).ToArray();
                if (!currentEvidence.SequenceEqual(decisionEvidence, StringComparer.Ordinal)) reasons.Add(HumanEngineeringReviewDecisionEvaluationReasons.EvidenceBindingsChanged);
            }

            return new HumanEngineeringReviewDecisionEvaluation
            {
                DecisionRecordId = decision.DecisionRecordId,
                RuleId = decision.RuleId,
                Status = reasons.Count == 0 ? HumanEngineeringReviewDecisionEvaluationStatuses.Current : HumanEngineeringReviewDecisionEvaluationStatuses.Stale,
                Reasons = reasons
            };
        }

        private static bool IsSha256(string? value)
        {
            if (value == null || value.Length != 64) return false;
            return value.All(character => (character >= '0' && character <= '9') || (character >= 'A' && character <= 'F') || (character >= 'a' && character <= 'f'));
        }
    }
}

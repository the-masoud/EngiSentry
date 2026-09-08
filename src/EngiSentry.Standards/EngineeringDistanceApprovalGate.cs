using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class EngineeringDistanceApprovalGateException : Exception
    {
        public EngineeringDistanceApprovalGateException(string message) : base(message) { }
    }

    public static class EngineeringDistanceApprovalGate
    {
        public static bool TryCreateExecutableRule(EngineeringDistanceCandidateDefinition candidate, EngineeringDistanceApprovalDecision decision, StandardRulePackResolution standardsResolution, out EngineeringDistanceRuleDefinition? rule)
        {
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (decision == null) throw new ArgumentNullException(nameof(decision));
            if (standardsResolution == null) throw new ArgumentNullException(nameof(standardsResolution));
            if (!string.Equals(decision.CandidateId, candidate.CandidateId, StringComparison.Ordinal)) throw new EngineeringDistanceApprovalGateException("engineering distance approval CandidateId does not match candidate: " + candidate.CandidateId);
            if (candidate.ApprovalStatus != "PENDING_ENGINEERING_APPROVAL") throw new EngineeringDistanceApprovalGateException("engineering distance candidate is not pending engineering approval: " + candidate.CandidateId);

            if (decision.DecisionStatus == "PENDING" || decision.DecisionStatus == "REJECTED" || decision.DecisionStatus == "REFERENCE_ONLY")
            {
                rule = null;
                return false;
            }
            if (decision.DecisionStatus != "APPROVED") throw new EngineeringDistanceApprovalGateException("unsupported engineering distance approval DecisionStatus: " + decision.DecisionStatus);
            if (string.IsNullOrWhiteSpace(decision.ApprovalReference)) throw new EngineeringDistanceApprovalGateException("engineering distance approval reference is required: " + candidate.CandidateId);
            if (standardsResolution.EffectiveRules == null) throw new EngineeringDistanceApprovalGateException("engineering distance approval standards EffectiveRules are required");

            List<EffectiveStandardRuleDefinition> matches = standardsResolution.EffectiveRules.Where(x => x != null && string.Equals(x.RuleId, candidate.RuleId, StringComparison.Ordinal)).ToList();
            if (matches.Count != 1) throw new EngineeringDistanceApprovalGateException("engineering distance approval requires exactly one effective RuleId: " + candidate.RuleId);
            List<string> candidateClauseIds = candidate.ClauseIds == null ? new List<string>() : candidate.ClauseIds.OrderBy(x => x, StringComparer.Ordinal).ToList();
            List<string> effectiveClauseIds = matches[0].ClauseIds == null ? new List<string>() : matches[0].ClauseIds.OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (!candidateClauseIds.SequenceEqual(effectiveClauseIds, StringComparer.Ordinal)) throw new EngineeringDistanceApprovalGateException("engineering distance approval ClauseIds do not match effective rule: " + candidate.RuleId);

            rule = new EngineeringDistanceRuleDefinition
            {
                RuleId = candidate.RuleId,
                MeasurementKind = candidate.MeasurementKind,
                MinimumDistanceMeters = candidate.ProposedMinimumDistanceMeters,
                RequiredMeasurementBasisKey = candidate.RequiredMeasurementBasisKey,
                RequiredApplicabilityKey = candidate.RequiredApplicabilityKey
            };
            return true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class StandardAuthorityExecutionGateException : Exception
    {
        public StandardAuthorityExecutionGateException(string message) : base(message) { }
    }

    public static class StandardAuthorityExecutionGate
    {
        public static StandardExecutionGateResolution Evaluate(StandardAuthorityResolution authorityResolution)
        {
            if (authorityResolution == null) throw new ArgumentNullException(nameof(authorityResolution));
            if (authorityResolution.Rules == null) throw new StandardAuthorityExecutionGateException("standard authority execution gate Rules are required");

            var ruleIds = new HashSet<string>(StringComparer.Ordinal);
            var decisions = new List<StandardRuleExecutionGateDecision>();
            foreach (StandardRuleAuthorityResolution rule in authorityResolution.Rules)
            {
                if (rule == null) throw new StandardAuthorityExecutionGateException("standard authority execution gate contains a null rule");
                if (string.IsNullOrWhiteSpace(rule.RuleId)) throw new StandardAuthorityExecutionGateException("standard authority execution gate RuleId is required");
                if (!ruleIds.Add(rule.RuleId)) throw new StandardAuthorityExecutionGateException("duplicate standard authority execution gate RuleId: " + rule.RuleId);
                if (!IsSupportedMode(rule.EffectiveEnforcementMode)) throw new StandardAuthorityExecutionGateException("standard authority execution gate EffectiveEnforcementMode is invalid: " + rule.RuleId);
                if (rule.Clauses == null) throw new StandardAuthorityExecutionGateException("standard authority execution gate Clauses are required: " + rule.RuleId);
                if (rule.Clauses.Count == 0) throw new StandardAuthorityExecutionGateException("standard authority execution gate Clauses are empty: " + rule.RuleId);

                var clauseIds = new HashSet<string>(StringComparer.Ordinal);
                var modes = new List<string>();
                bool quarantined = false;
                foreach (StandardClauseAuthorityResolution clause in rule.Clauses)
                {
                    ValidateClause(rule.RuleId, clause, clauseIds);
                    modes.Add(clause.EnforcementMode);
                    quarantined |= string.Equals(clause.AuthorityClass, StandardAuthorityClasses.Quarantined, StringComparison.Ordinal);
                }

                string aggregate = Aggregate(modes);
                if (!string.Equals(aggregate, rule.EffectiveEnforcementMode, StringComparison.Ordinal)) throw new StandardAuthorityExecutionGateException("standard authority execution gate EffectiveEnforcementMode is inconsistent: " + rule.RuleId);
                decisions.Add(new StandardRuleExecutionGateDecision
                {
                    RuleId = rule.RuleId,
                    EffectiveEnforcementMode = aggregate,
                    AutoExecutionAllowed = aggregate == StandardEnforcementModes.Deterministic,
                    DecisionState = DecisionState(aggregate, quarantined)
                });
            }
            return new StandardExecutionGateResolution { Rules = decisions.OrderBy(item => item.RuleId, StringComparer.Ordinal).ToList() };
        }

        private static void ValidateClause(string ruleId, StandardClauseAuthorityResolution clause, HashSet<string> clauseIds)
        {
            if (clause == null) throw new StandardAuthorityExecutionGateException("standard authority execution gate contains a null clause: " + ruleId);
            if (string.IsNullOrWhiteSpace(clause.ClauseId)) throw new StandardAuthorityExecutionGateException("standard authority execution gate ClauseId is required: " + ruleId);
            if (!clauseIds.Add(clause.ClauseId)) throw new StandardAuthorityExecutionGateException("duplicate standard authority execution gate ClauseId: " + ruleId + " | " + clause.ClauseId);
            if (string.IsNullOrWhiteSpace(clause.SourceId)) throw new StandardAuthorityExecutionGateException("standard authority execution gate SourceId is required: " + ruleId + " | " + clause.ClauseId);
            if (string.IsNullOrWhiteSpace(clause.PolicyId)) throw new StandardAuthorityExecutionGateException("standard authority execution gate PolicyId is required: " + ruleId + " | " + clause.ClauseId);
            if (clause.PolicyClauseId != null && string.IsNullOrWhiteSpace(clause.PolicyClauseId)) throw new StandardAuthorityExecutionGateException("standard authority execution gate PolicyClauseId is invalid: " + ruleId + " | " + clause.ClauseId);
            if (clause.PolicyClauseId != null && !string.Equals(clause.PolicyClauseId, clause.ClauseId, StringComparison.Ordinal)) throw new StandardAuthorityExecutionGateException("standard authority execution gate PolicyClauseId is inconsistent: " + ruleId + " | " + clause.ClauseId);
            if (!IsSupportedAuthorityClass(clause.AuthorityClass)) throw new StandardAuthorityExecutionGateException("standard authority execution gate AuthorityClass is invalid: " + ruleId + " | " + clause.ClauseId);
            if (!IsSupportedMode(clause.EnforcementMode)) throw new StandardAuthorityExecutionGateException("standard authority execution gate EnforcementMode is invalid: " + ruleId + " | " + clause.ClauseId);
            if (string.IsNullOrWhiteSpace(clause.Rationale)) throw new StandardAuthorityExecutionGateException("standard authority execution gate Rationale is required: " + ruleId + " | " + clause.ClauseId);
            if (!IsValidAuthorityModePair(clause.AuthorityClass, clause.EnforcementMode)) throw new StandardAuthorityExecutionGateException("standard authority execution gate AuthorityClass and EnforcementMode are inconsistent: " + ruleId + " | " + clause.ClauseId);
        }

        private static string Aggregate(IEnumerable<string> modes)
        {
            var values = new HashSet<string>(modes, StringComparer.Ordinal);
            if (values.Contains(StandardEnforcementModes.NeverAutoEnforce)) return StandardEnforcementModes.NeverAutoEnforce;
            if (values.Contains(StandardEnforcementModes.ConditionalReview)) return StandardEnforcementModes.ConditionalReview;
            if (values.All(value => value == StandardEnforcementModes.Deterministic)) return StandardEnforcementModes.Deterministic;
            if (values.All(value => value == StandardEnforcementModes.Advisory)) return StandardEnforcementModes.Advisory;
            return StandardEnforcementModes.ConditionalReview;
        }

        private static string? DecisionState(string mode, bool quarantined)
        {
            if (mode == StandardEnforcementModes.Deterministic) return null;
            if (mode == StandardEnforcementModes.Advisory) return StandardDecisionStates.Advisory;
            if (mode == StandardEnforcementModes.NeverAutoEnforce && quarantined) return StandardDecisionStates.SourceQuarantined;
            return StandardDecisionStates.ExpertReviewRequired;
        }

        private static bool IsSupportedMode(string value) =>
            string.Equals(value, StandardEnforcementModes.Deterministic, StringComparison.Ordinal)
            || string.Equals(value, StandardEnforcementModes.ConditionalReview, StringComparison.Ordinal)
            || string.Equals(value, StandardEnforcementModes.Advisory, StringComparison.Ordinal)
            || string.Equals(value, StandardEnforcementModes.NeverAutoEnforce, StringComparison.Ordinal);

        private static bool IsSupportedAuthorityClass(string value) =>
            string.Equals(value, StandardAuthorityClasses.GoverningProject, StringComparison.Ordinal)
            || string.Equals(value, StandardAuthorityClasses.ActivePrimary, StringComparison.Ordinal)
            || string.Equals(value, StandardAuthorityClasses.ActiveSupporting, StringComparison.Ordinal)
            || string.Equals(value, StandardAuthorityClasses.EngineeringGuidance, StringComparison.Ordinal)
            || string.Equals(value, StandardAuthorityClasses.LegacyConflict, StringComparison.Ordinal)
            || string.Equals(value, StandardAuthorityClasses.Quarantined, StringComparison.Ordinal);

        private static bool IsValidAuthorityModePair(string authorityClass, string enforcementMode)
        {
            if (authorityClass == StandardAuthorityClasses.GoverningProject || authorityClass == StandardAuthorityClasses.ActivePrimary)
                return enforcementMode == StandardEnforcementModes.Deterministic || enforcementMode == StandardEnforcementModes.ConditionalReview;
            if (authorityClass == StandardAuthorityClasses.ActiveSupporting || authorityClass == StandardAuthorityClasses.EngineeringGuidance)
                return enforcementMode == StandardEnforcementModes.ConditionalReview || enforcementMode == StandardEnforcementModes.Advisory;
            return enforcementMode == StandardEnforcementModes.NeverAutoEnforce;
        }
    }
}

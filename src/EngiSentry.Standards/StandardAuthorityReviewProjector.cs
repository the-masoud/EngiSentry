using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public static class StandardAuthorityReviewProjector
    {
        public static StandardAuthorityReviewProjection Create(StandardAuthorityResolution authorityResolution)
        {
            if (authorityResolution == null) throw new ArgumentNullException(nameof(authorityResolution));

            StandardExecutionGateResolution gate = StandardAuthorityExecutionGate.Evaluate(authorityResolution);
            var rules = authorityResolution.Rules.ToDictionary(rule => rule.RuleId, StringComparer.Ordinal);
            var items = new List<StandardAuthorityReviewItem>();
            foreach (StandardRuleExecutionGateDecision decision in gate.Rules)
            {
                if (decision.AutoExecutionAllowed) continue;
                StandardRuleAuthorityResolution rule = rules[decision.RuleId];
                items.Add(new StandardAuthorityReviewItem
                {
                    RuleId = decision.RuleId,
                    EffectiveEnforcementMode = decision.EffectiveEnforcementMode,
                    DecisionState = decision.DecisionState!,
                    Message = Message(decision.DecisionState!),
                    Clauses = rule.Clauses.Select(CopyClause).OrderBy(clause => clause.ClauseId, StringComparer.Ordinal).ToList()
                });
            }
            return new StandardAuthorityReviewProjection { ReviewItems = items.OrderBy(item => item.RuleId, StringComparer.Ordinal).ToList() };
        }

        private static string Message(string decisionState)
        {
            if (decisionState == StandardDecisionStates.Advisory) return "Standards authority is advisory; automatic deterministic evaluation is not allowed.";
            if (decisionState == StandardDecisionStates.ExpertReviewRequired) return "Standards authority requires engineering review; automatic deterministic evaluation is not allowed.";
            if (decisionState == StandardDecisionStates.SourceQuarantined) return "Standards authority is quarantined; automatic deterministic evaluation is not allowed.";
            throw new InvalidOperationException("unsupported standard authority review decision state: " + decisionState);
        }

        private static StandardClauseAuthorityResolution CopyClause(StandardClauseAuthorityResolution source) => new StandardClauseAuthorityResolution
        {
            ClauseId = source.ClauseId,
            SourceId = source.SourceId,
            PolicyId = source.PolicyId,
            PolicyClauseId = source.PolicyClauseId,
            AuthorityClass = source.AuthorityClass,
            EnforcementMode = source.EnforcementMode,
            Rationale = source.Rationale
        };
    }
}

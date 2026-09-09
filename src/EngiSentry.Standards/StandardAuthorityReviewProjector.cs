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

            return Create(authorityResolution, null, null);
        }

        public static StandardAuthorityReviewProjection Create(
            StandardAuthorityResolution authorityResolution,
            StandardSourceRegistry? sourceRegistry,
            StandardClauseRegistry? clauseRegistry)
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
                    Clauses = rule.Clauses.Select(clause => CopyClause(clause, sourceRegistry, clauseRegistry)).OrderBy(clause => clause.ClauseId, StringComparer.Ordinal).ToList()
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

        private static StandardClauseAuthorityResolution CopyClause(
            StandardClauseAuthorityResolution source,
            StandardSourceRegistry? sourceRegistry,
            StandardClauseRegistry? clauseRegistry)
        {
            StandardClauseAuthorityResolution copy = new StandardClauseAuthorityResolution
            {
                ClauseId = source.ClauseId,
                SourceId = source.SourceId,
                PolicyId = source.PolicyId,
                PolicyClauseId = source.PolicyClauseId,
                AuthorityClass = source.AuthorityClass,
                EnforcementMode = source.EnforcementMode,
                Rationale = source.Rationale
            };

            if (sourceRegistry != null && sourceRegistry.TryGet(source.SourceId, out StandardSourceDefinition? standardSource) && standardSource != null)
            {
                copy.SourceDocumentNumber = standardSource.DocumentNumber;
                copy.SourceTitle = standardSource.Title;
                copy.SourceIssuer = standardSource.Issuer;
                copy.SourceRevisionOrEdition = standardSource.RevisionOrEdition;
                copy.SourceFileName = standardSource.SourceFileName;
                copy.SourceSha256 = standardSource.SourceSha256;
            }

            if (clauseRegistry != null && clauseRegistry.TryGet(source.ClauseId, out StandardClauseDefinition? clause) && clause != null)
            {
                copy.ClauseReference = clause.ClauseReference;
                copy.ClauseText = clause.ClauseText;
                copy.ClauseHeading = clause.Heading;
                copy.PageNumber = clause.PageNumber;
            }

            return copy;
        }
    }
}

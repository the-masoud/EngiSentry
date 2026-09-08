using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class StandardAuthorityResolutionException : Exception
    {
        public StandardAuthorityResolutionException(string message) : base(message) { }
    }

    public static class StandardAuthorityResolver
    {
        public static StandardAuthorityResolution Resolve(StandardRulePackResolution ruleResolution, StandardClauseRegistry clauseRegistry, StandardAuthorityPolicyRegistry authorityRegistry)
        {
            if (ruleResolution == null) throw new ArgumentNullException(nameof(ruleResolution));
            if (clauseRegistry == null) throw new ArgumentNullException(nameof(clauseRegistry));
            if (authorityRegistry == null) throw new ArgumentNullException(nameof(authorityRegistry));
            if (ruleResolution.EffectiveRules == null) throw new StandardAuthorityResolutionException("standard authority resolution EffectiveRules are required");

            var ruleIds = new HashSet<string>(StringComparer.Ordinal);
            var rules = new List<StandardRuleAuthorityResolution>();
            foreach (EffectiveStandardRuleDefinition rule in ruleResolution.EffectiveRules)
            {
                if (rule == null) throw new StandardAuthorityResolutionException("standard authority resolution contains a null effective rule");
                if (string.IsNullOrWhiteSpace(rule.RuleId)) throw new StandardAuthorityResolutionException("standard authority resolution RuleId is required");
                if (rule.ClauseIds == null) throw new StandardAuthorityResolutionException("standard authority resolution ClauseIds are required: " + rule.RuleId);
                if (rule.ClauseIds.Count == 0) throw new StandardAuthorityResolutionException("standard authority resolution ClauseIds are empty: " + rule.RuleId);
                if (!ruleIds.Add(rule.RuleId)) throw new StandardAuthorityResolutionException("duplicate standard authority resolution RuleId: " + rule.RuleId);

                var clauseIds = new HashSet<string>(StringComparer.Ordinal);
                var clauses = new List<StandardClauseAuthorityResolution>();
                foreach (string clauseId in rule.ClauseIds)
                {
                    if (string.IsNullOrWhiteSpace(clauseId)) throw new StandardAuthorityResolutionException("standard authority resolution ClauseId is required: " + rule.RuleId);
                    if (!clauseIds.Add(clauseId)) throw new StandardAuthorityResolutionException("duplicate standard authority resolution ClauseId: " + rule.RuleId + " | " + clauseId);
                    if (!clauseRegistry.TryGet(clauseId, out StandardClauseDefinition? clause) || clause == null) throw new StandardAuthorityResolutionException("standard authority resolution references unknown ClauseId: " + rule.RuleId + " | " + clauseId);
                    if (!authorityRegistry.TryResolve(clause.SourceId, clause.ClauseId, out StandardAuthorityPolicyDefinition? policy) || policy == null) throw new StandardAuthorityResolutionException("standard authority resolution has no policy: " + rule.RuleId + " | " + clauseId);
                    if (!string.Equals(policy.SourceId, clause.SourceId, StringComparison.Ordinal) || (policy.ClauseId != null && !string.Equals(policy.ClauseId, clause.ClauseId, StringComparison.Ordinal))) throw new StandardAuthorityResolutionException("standard authority resolution policy is inconsistent: " + rule.RuleId + " | " + clauseId);
                    clauses.Add(new StandardClauseAuthorityResolution { ClauseId = clause.ClauseId, SourceId = clause.SourceId, PolicyId = policy.PolicyId, PolicyClauseId = policy.ClauseId, AuthorityClass = policy.AuthorityClass, EnforcementMode = policy.EnforcementMode, Rationale = policy.Rationale });
                }
                clauses = clauses.OrderBy(item => item.ClauseId, StringComparer.Ordinal).ToList();
                rules.Add(new StandardRuleAuthorityResolution { RuleId = rule.RuleId, EffectiveEnforcementMode = Aggregate(clauses.Select(item => item.EnforcementMode)), Clauses = clauses });
            }
            return new StandardAuthorityResolution { Rules = rules.OrderBy(item => item.RuleId, StringComparer.Ordinal).ToList() };
        }

        private static string Aggregate(IEnumerable<string> modes)
        {
            HashSet<string> values = new HashSet<string>(modes, StringComparer.Ordinal);
            if (values.Contains(StandardEnforcementModes.NeverAutoEnforce)) return StandardEnforcementModes.NeverAutoEnforce;
            if (values.Contains(StandardEnforcementModes.ConditionalReview)) return StandardEnforcementModes.ConditionalReview;
            if (values.All(value => value == StandardEnforcementModes.Deterministic)) return StandardEnforcementModes.Deterministic;
            if (values.All(value => value == StandardEnforcementModes.Advisory)) return StandardEnforcementModes.Advisory;
            return StandardEnforcementModes.ConditionalReview;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class StandardAuthorityPolicyRegistryValidationException : Exception
    {
        public StandardAuthorityPolicyRegistryValidationException(string message)
            : base(message) { }
    }

    public sealed class StandardAuthorityPolicyRegistry
    {
        private readonly List<StandardAuthorityPolicyDefinition> policies;
        private readonly HashSet<string> sourceIds;
        private readonly Dictionary<string, string> clauseSourceIds;

        private StandardAuthorityPolicyRegistry(List<StandardAuthorityPolicyDefinition> policies, HashSet<string> sourceIds, Dictionary<string, string> clauseSourceIds)
        {
            this.policies = policies;
            this.sourceIds = sourceIds;
            this.clauseSourceIds = clauseSourceIds;
        }

        public IReadOnlyList<StandardAuthorityPolicyDefinition> Policies => policies.Select(Copy).ToList();

        public static StandardAuthorityPolicyRegistry Create(StandardSourceRegistry sourceRegistry, StandardClauseRegistry clauseRegistry, IEnumerable<StandardAuthorityPolicyDefinition> policies)
        {
            if (sourceRegistry == null) throw new ArgumentNullException(nameof(sourceRegistry));
            if (clauseRegistry == null) throw new ArgumentNullException(nameof(clauseRegistry));
            if (policies == null) throw new ArgumentNullException(nameof(policies));

            var accepted = new List<StandardAuthorityPolicyDefinition>();
            var policyIds = new HashSet<string>(StringComparer.Ordinal);
            var sourceWideSourceIds = new HashSet<string>(StringComparer.Ordinal);
            var clauseIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (StandardAuthorityPolicyDefinition policy in policies)
            {
                if (policy == null) throw new StandardAuthorityPolicyRegistryValidationException("standard authority policy registry contains a null policy");
                Validate(policy, sourceRegistry, clauseRegistry);
                if (!policyIds.Add(policy.PolicyId)) throw new StandardAuthorityPolicyRegistryValidationException("duplicate standard authority PolicyId: " + policy.PolicyId);

                if (policy.ClauseId == null)
                {
                    if (!sourceWideSourceIds.Add(policy.SourceId)) throw new StandardAuthorityPolicyRegistryValidationException("duplicate standard authority source-wide policy: " + policy.SourceId);
                }
                else if (!clauseIds.Add(policy.ClauseId))
                {
                    throw new StandardAuthorityPolicyRegistryValidationException("duplicate standard authority clause policy: " + policy.ClauseId);
                }

                accepted.Add(Copy(policy));
            }

            return new StandardAuthorityPolicyRegistry(accepted
                .OrderBy(policy => policy.SourceId, StringComparer.Ordinal)
                .ThenBy(policy => policy.ClauseId == null ? 0 : 1)
                .ThenBy(policy => policy.ClauseId, StringComparer.Ordinal)
                .ThenBy(policy => policy.PolicyId, StringComparer.Ordinal)
                .ToList(),
                new HashSet<string>(sourceRegistry.Sources.Select(source => source.SourceId), StringComparer.Ordinal),
                clauseRegistry.Clauses.ToDictionary(clause => clause.ClauseId, clause => clause.SourceId, StringComparer.Ordinal));
        }

        public bool TryResolve(string sourceId, string? clauseId, out StandardAuthorityPolicyDefinition? policy)
        {
            policy = null;
            if (string.IsNullOrWhiteSpace(sourceId)) return false;
            if (clauseId != null && string.IsNullOrWhiteSpace(clauseId)) return false;
            if (!sourceIds.Contains(sourceId)) return false;

            StandardAuthorityPolicyDefinition? found;
            if (clauseId != null)
            {
                if (!clauseSourceIds.TryGetValue(clauseId, out string? clauseSourceId) || !string.Equals(clauseSourceId, sourceId, StringComparison.Ordinal)) return false;
                StandardAuthorityPolicyDefinition? clausePolicy = policies.FirstOrDefault(candidate =>
                    string.Equals(candidate.ClauseId, clauseId, StringComparison.Ordinal) && string.Equals(candidate.SourceId, sourceId, StringComparison.Ordinal));

                if (clausePolicy != null)
                {
                    policy = Copy(clausePolicy);
                    return true;
                }
            }

            found = policies.FirstOrDefault(candidate => candidate.ClauseId == null && string.Equals(candidate.SourceId, sourceId, StringComparison.Ordinal));
            if (found == null) return false;
            policy = Copy(found);
            return true;
        }

        public StandardAuthorityPolicyDefinition Get(string sourceId, string? clauseId)
        {
            if (TryResolve(sourceId, clauseId, out StandardAuthorityPolicyDefinition? policy) && policy != null) return policy;
            string suffix = clauseId == null ? string.Empty : " | " + clauseId;
            throw new StandardAuthorityPolicyRegistryValidationException("standard authority policy was not found: " + (sourceId ?? string.Empty) + suffix);
        }

        private static void Validate(StandardAuthorityPolicyDefinition policy, StandardSourceRegistry sourceRegistry, StandardClauseRegistry clauseRegistry)
        {
            if (string.IsNullOrWhiteSpace(policy.PolicyId)) throw new StandardAuthorityPolicyRegistryValidationException("standard authority PolicyId is required");
            if (string.IsNullOrWhiteSpace(policy.SourceId)) throw new StandardAuthorityPolicyRegistryValidationException("standard authority SourceId is required: " + policy.PolicyId);
            if (policy.ClauseId != null && string.IsNullOrWhiteSpace(policy.ClauseId)) throw new StandardAuthorityPolicyRegistryValidationException("standard authority ClauseId is empty: " + policy.PolicyId);
            if (string.IsNullOrWhiteSpace(policy.AuthorityClass)) throw new StandardAuthorityPolicyRegistryValidationException("standard authority AuthorityClass is required: " + policy.PolicyId);
            if (!IsAuthorityClass(policy.AuthorityClass)) throw new StandardAuthorityPolicyRegistryValidationException("standard authority AuthorityClass is unsupported: " + policy.AuthorityClass);
            if (string.IsNullOrWhiteSpace(policy.EnforcementMode)) throw new StandardAuthorityPolicyRegistryValidationException("standard authority EnforcementMode is required: " + policy.PolicyId);
            if (!IsEnforcementMode(policy.EnforcementMode)) throw new StandardAuthorityPolicyRegistryValidationException("standard authority EnforcementMode is unsupported: " + policy.EnforcementMode);
            if (!IsValidCombination(policy.AuthorityClass, policy.EnforcementMode)) throw new StandardAuthorityPolicyRegistryValidationException("standard authority/enforcement combination is invalid: " + policy.AuthorityClass + " | " + policy.EnforcementMode);
            if (string.IsNullOrWhiteSpace(policy.Rationale)) throw new StandardAuthorityPolicyRegistryValidationException("standard authority Rationale is required: " + policy.PolicyId);
            if (!sourceRegistry.TryGet(policy.SourceId, out _)) throw new StandardAuthorityPolicyRegistryValidationException("standard authority policy references unknown SourceId: " + policy.SourceId);

            if (policy.ClauseId != null)
            {
                if (!clauseRegistry.TryGet(policy.ClauseId, out StandardClauseDefinition? clause)) throw new StandardAuthorityPolicyRegistryValidationException("standard authority policy references unknown ClauseId: " + policy.ClauseId);
                if (!string.Equals(clause!.SourceId, policy.SourceId, StringComparison.Ordinal)) throw new StandardAuthorityPolicyRegistryValidationException("standard authority policy ClauseId belongs to a different SourceId: " + policy.ClauseId);
            }
        }

        private static bool IsAuthorityClass(string value) =>
            value == StandardAuthorityClasses.GoverningProject ||
            value == StandardAuthorityClasses.ActivePrimary ||
            value == StandardAuthorityClasses.ActiveSupporting ||
            value == StandardAuthorityClasses.EngineeringGuidance ||
            value == StandardAuthorityClasses.LegacyConflict ||
            value == StandardAuthorityClasses.Quarantined;

        private static bool IsEnforcementMode(string value) =>
            value == StandardEnforcementModes.Deterministic ||
            value == StandardEnforcementModes.ConditionalReview ||
            value == StandardEnforcementModes.Advisory ||
            value == StandardEnforcementModes.NeverAutoEnforce;

        private static bool IsValidCombination(string authorityClass, string enforcementMode)
        {
            if (authorityClass == StandardAuthorityClasses.GoverningProject || authorityClass == StandardAuthorityClasses.ActivePrimary)
                return enforcementMode == StandardEnforcementModes.Deterministic || enforcementMode == StandardEnforcementModes.ConditionalReview;
            if (authorityClass == StandardAuthorityClasses.ActiveSupporting || authorityClass == StandardAuthorityClasses.EngineeringGuidance)
                return enforcementMode == StandardEnforcementModes.ConditionalReview || enforcementMode == StandardEnforcementModes.Advisory;
            return (authorityClass == StandardAuthorityClasses.LegacyConflict || authorityClass == StandardAuthorityClasses.Quarantined) && enforcementMode == StandardEnforcementModes.NeverAutoEnforce;
        }

        private static StandardAuthorityPolicyDefinition Copy(StandardAuthorityPolicyDefinition policy)
        {
            return new StandardAuthorityPolicyDefinition
            {
                PolicyId = policy.PolicyId,
                SourceId = policy.SourceId,
                ClauseId = policy.ClauseId,
                AuthorityClass = policy.AuthorityClass,
                EnforcementMode = policy.EnforcementMode,
                Rationale = policy.Rationale
            };
        }
    }
}

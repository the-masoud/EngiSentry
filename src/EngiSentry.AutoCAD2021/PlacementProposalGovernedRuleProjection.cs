using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;
using EngiSentry.Standards;

namespace EngiSentry.AutoCAD2021
{
    /// <summary>
    /// The exact governed-rule composition the ES_SUGGEST command performs: it maps the resolved P9 project
    /// governance aggregate onto the governed pack projection through the real standards-authority chain. It
    /// creates no authority of its own — it wires already-resolved evidence.
    ///
    /// Authority bindings (<see cref="ProjectRuleAuthorityBinding"/>) are an explicit, evidence-backed
    /// contract, supplied here as a parameter so the project↔authority identity boundary is never crossed by
    /// naming convention or a hard-coded mapping. PRODUCTION SUPPLIES NO BINDING: with no validated binding a
    /// project distance rule can never receive execution authority, so the real path stays fail-closed at
    /// 0 auto-executable rules. A governed deployment — or an isolated TEST_ONLY governed case — may supply
    /// bindings, each of which is still validated against the authority chain and still passes the existing
    /// approval/currentness/trust checks before it can grant anything.
    /// </summary>
    public static class PlacementProposalGovernedRuleProjection
    {
        /// <summary>Builds the governed rules ES_SUGGEST hands to the placement proposal runtime.</summary>
        public static IReadOnlyList<StandardsPackRule> Build(
            ProjectDistanceCriteria criteria,
            ProjectDistanceGovernanceAggregateResult governance,
            PlotPlanStandardsAuthorityRuntimeContext authorityContext,
            IReadOnlyList<ProjectRuleAuthorityBinding>? authorityBindings = null)
        {
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));
            if (authorityContext == null) throw new ArgumentNullException(nameof(authorityContext));

            return StandardsPackProjectionBuilder.Build(
                authorityContext,
                ProjectDistanceRuleBinder.Create(criteria),
                GovernedResolutions(governance),
                CurrentnessByRuleId(governance),
                ApprovalAuthorityByRuleId(governance),
                packNamesByPackId: null,
                authorityBindings);
        }

        /// <summary>Maps the P9 aggregate onto the governed-resolution lookup the projection consumes. The
        /// aggregate is the single source; no resolution is re-derived or invented.</summary>
        private static IReadOnlyDictionary<string, ProjectDistanceGovernedRuleResolution> GovernedResolutions(ProjectDistanceGovernanceAggregateResult? governance)
        {
            if (governance == null || governance.Items == null) return new Dictionary<string, ProjectDistanceGovernedRuleResolution>(StringComparer.Ordinal);
            return governance.Items
                .Where(x => x != null && x.Resolution != null && !string.IsNullOrEmpty(x.ProjectRuleId))
                .GroupBy(x => x.ProjectRuleId!, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.OrderBy(y => y.CandidateId, StringComparer.Ordinal).First().Resolution!, StringComparer.Ordinal);
        }

        /// <summary>Carries the existing P9 approval-currentness evidence into the projection ONLY where the
        /// chain actually produced a verdict, so a stale approval can never be executable and the fail-closed
        /// default for unapproved project rules is unchanged.</summary>
        private static IReadOnlyDictionary<string, EngineeringDistanceApprovalCurrentnessResult> CurrentnessByRuleId(ProjectDistanceGovernanceAggregateResult? governance)
        {
            var map = new Dictionary<string, EngineeringDistanceApprovalCurrentnessResult>(StringComparer.Ordinal);
            if (governance == null || governance.Items == null) return map;
            foreach (ProjectDistanceGovernanceAggregateItem item in governance.Items
                .Where(x => x != null && !string.IsNullOrEmpty(x.ProjectRuleId) && !string.IsNullOrEmpty(x.CurrentnessState))
                .OrderBy(x => x.ProjectRuleId!, StringComparer.Ordinal))
            {
                map[item.ProjectRuleId!] = new EngineeringDistanceApprovalCurrentnessResult { Status = item.CurrentnessState!, Reasons = new List<string>() };
            }
            return map;
        }

        /// <summary>Carries the existing P9 approval-authority (trust) evidence into the projection ONLY where
        /// the chain actually produced a verdict, so an untrusted or non-deterministic approval can never
        /// execute, while unapproved project rules keep their current behavior.</summary>
        private static IReadOnlyDictionary<string, EngineeringDistanceApprovalAuthorityResult> ApprovalAuthorityByRuleId(ProjectDistanceGovernanceAggregateResult? governance)
        {
            var map = new Dictionary<string, EngineeringDistanceApprovalAuthorityResult>(StringComparer.Ordinal);
            if (governance == null || governance.Items == null) return map;
            foreach (ProjectDistanceGovernanceAggregateItem item in governance.Items
                .Where(x => x != null && !string.IsNullOrEmpty(x.ProjectRuleId) && !string.IsNullOrEmpty(x.AuthorityState))
                .OrderBy(x => x.ProjectRuleId!, StringComparer.Ordinal))
            {
                ProjectDistanceGovernanceProvenance? provenance = item.Resolution?.GovernanceProvenance;
                map[item.ProjectRuleId!] = new EngineeringDistanceApprovalAuthorityResult
                {
                    State = item.AuthorityState!,
                    ProjectDirectory = governance.ProjectDirectory,
                    CandidateId = item.CandidateId,
                    ApprovalRecordId = provenance?.ApprovalRecordId ?? item.ApprovalRecordId ?? string.Empty,
                    PolicyId = provenance?.PolicyId,
                    AuthorityBasisReference = provenance?.AuthorityBasisReference
                };
            }
            return map;
        }
    }
}

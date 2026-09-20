using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>
    /// Builds the governed <see cref="StandardsPackRule"/> projection: a read-only view composed entirely of
    /// existing authoritative P6→P9 types. It never creates a source, clause, policy, approval, trust
    /// decision or distance. Authority, executability and currentness continue to be decided by the existing
    /// authority chain exactly as they are today (authority/gate/review 4/4/4, auto-executable 0, flare
    /// SOURCE_QUARANTINED).
    ///
    /// The projection reads only registered sources/clauses/policies and the resolved rule/authority/gate
    /// chain. An empty registry yields an empty pack, which yields INSUFFICIENT_EVIDENCE downstream — never
    /// a fabricated criterion. Any absent value stays absent; missing provenance is never synthesized.
    /// </summary>
    public static class StandardsPackProjectionBuilder
    {
        /// <summary>
        /// Projects the resolved authority chain plus the executed engineering-distance rule set into governed
        /// pack rules. The rule set supplies the criterion (MinimumDistanceMeters) and the bounded
        /// relationship; the authority chain supplies authority/executability/evidence/currentness.
        ///
        /// Dependency layer: this builder lives in EngiSentry.Standards, which references
        /// EngiSentry.Contracts only (Phase A architecture A.1/A.2). It therefore accepts the rule set as the
        /// contract-level <see cref="EngineeringDistanceRuleDefinition"/> list rather than the
        /// <see cref="EngineeringDistanceRuleRegistry"/> service, which lives in EngiSentry.Rules. Callers
        /// pass <c>registry.Rules</c>; no new project reference and no reversal of the existing dependency
        /// direction is introduced.
        /// </summary>
        public static IReadOnlyList<StandardsPackRule> Build(
            PlotPlanStandardsAuthorityRuntimeContext authorityContext,
            IReadOnlyList<EngineeringDistanceRuleDefinition> rules,
            IReadOnlyDictionary<string, ProjectDistanceGovernedRuleResolution>? governedResolutions = null,
            IReadOnlyDictionary<string, EngineeringDistanceApprovalCurrentnessResult>? currentnessByRuleId = null,
            IReadOnlyDictionary<string, EngineeringDistanceApprovalAuthorityResult>? approvalAuthorityByRuleId = null,
            IReadOnlyDictionary<string, string>? packNamesByPackId = null)
        {
            if (authorityContext == null) throw new ArgumentNullException(nameof(authorityContext));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (rules.Any(x => x == null)) throw new ArgumentNullException(nameof(rules), "The engineering distance rule set contains a null rule.");

            Dictionary<string, StandardRuleExecutionGateDecision> gateByRuleId = authorityContext.ExecutionGate.Rules
                .Where(x => x != null && !string.IsNullOrEmpty(x.RuleId))
                .ToDictionary(x => x.RuleId!, x => x, StringComparer.Ordinal);

            Dictionary<string, StandardRuleAuthorityResolution> authorityByRuleId = authorityContext.AuthorityResolution.Rules
                .Where(x => x != null && !string.IsNullOrEmpty(x.RuleId))
                .ToDictionary(x => x.RuleId!, x => x, StringComparer.Ordinal);

            Dictionary<string, EffectiveStandardRuleDefinition> effectiveByRuleId = authorityContext.RuleResolution.EffectiveRules
                .Where(x => x != null && !string.IsNullOrEmpty(x.RuleId))
                .ToDictionary(x => x.RuleId!, x => x, StringComparer.Ordinal);

            return rules
                .OrderBy(x => x.RuleId, StringComparer.Ordinal)
                .Select(rule => BuildRule(rule, effectiveByRuleId, authorityByRuleId, gateByRuleId, authorityContext, governedResolutions, currentnessByRuleId, approvalAuthorityByRuleId, packNamesByPackId))
                .ToList();
        }

        private static StandardsPackRule BuildRule(
            EngineeringDistanceRuleDefinition rule,
            IDictionary<string, EffectiveStandardRuleDefinition> effectiveByRuleId,
            IDictionary<string, StandardRuleAuthorityResolution> authorityByRuleId,
            IDictionary<string, StandardRuleExecutionGateDecision> gateByRuleId,
            PlotPlanStandardsAuthorityRuntimeContext authorityContext,
            IReadOnlyDictionary<string, ProjectDistanceGovernedRuleResolution>? governedResolutions,
            IReadOnlyDictionary<string, EngineeringDistanceApprovalCurrentnessResult>? currentnessByRuleId,
            IReadOnlyDictionary<string, EngineeringDistanceApprovalAuthorityResult>? approvalAuthorityByRuleId,
            IReadOnlyDictionary<string, string>? packNamesByPackId)
        {
            effectiveByRuleId.TryGetValue(rule.RuleId, out EffectiveStandardRuleDefinition? effective);
            authorityByRuleId.TryGetValue(rule.RuleId, out StandardRuleAuthorityResolution? authority);
            gateByRuleId.TryGetValue(rule.RuleId, out StandardRuleExecutionGateDecision gate);

            string? packName = null;
            if (packNamesByPackId != null && effective != null && !string.IsNullOrEmpty(effective.WinningPackId))
                packNamesByPackId.TryGetValue(effective.WinningPackId, out packName);

            bool autoExecutionAllowed = gate != null && gate.AutoExecutionAllowed;
            List<StandardClauseAuthorityResolution> clauses = authority != null && authority.Clauses != null
                ? authority.Clauses.Where(x => x != null).OrderBy(x => x.ClauseId, StringComparer.Ordinal).Select(CopyClause).ToList()
                : new List<StandardClauseAuthorityResolution>();

            var packRule = new StandardsPackRule
            {
                RuleId = rule.RuleId,
                // Pack identity comes from the effective pack resolution; never invented.
                PackId = effective?.WinningPackId,
                Company = EffectiveCompany(effective, packName),
                DocumentNumber = FirstClause(clauses, x => x.SourceDocumentNumber),
                DocumentTitle = FirstClause(clauses, x => x.SourceTitle),
                RevisionOrEdition = FirstClause(clauses, x => x.SourceRevisionOrEdition),
                ClauseId = FirstClause(clauses, x => x.ClauseId),
                ClauseReference = FirstClause(clauses, x => x.ClauseReference),
                PageNumber = FirstClauseNullable(clauses, x => x.PageNumber),
                RequirementText = FirstClause(clauses, x => x.ClauseText),
                SubjectType = ResolveSubjectType(rule),
                ReferenceType = rule.RequiredMeasurementBasisKey,
                MeasurementBasisKey = rule.RequiredMeasurementBasisKey,
                MinimumDistanceMeters = rule.MinimumDistanceMeters,
                ApplicabilityKey = rule.RequiredApplicabilityKey,
                AuthorityClass = FirstClause(clauses, x => x.AuthorityClass),
                // Derived, never asserted: a rule is review-free only when the existing gate allows
                // auto-execution and the decision state is clean. Today this is true for no rule.
                EngineeringReviewRequired = !autoExecutionAllowed || !IsCleanDecision(gate),
                EffectiveEnforcementMode = gate?.EffectiveEnforcementMode,
                AutoExecutionAllowed = autoExecutionAllowed,
                DecisionState = gate?.DecisionState,
                Clauses = clauses,
                SourceSha256 = FirstClause(clauses, x => x.SourceSha256),
                ConstraintTreatment = ResolveConstraintTreatment(rule, gate, clauses)
            };

            ApplyGovernedResolution(packRule, rule.RuleId, governedResolutions);
            ApplyCurrentness(packRule, rule.RuleId, currentnessByRuleId);
            ApplyApprovalAuthority(packRule, rule.RuleId, approvalAuthorityByRuleId);

            return packRule;
        }

        private static string? EffectiveCompany(EffectiveStandardRuleDefinition? effective, string? packName)
        {
            // The existing pack contract carries no company attribute, and no source registry records one.
            // Company is metadata only and carries no authority. It is reported only when a registered pack
            // name exists; otherwise it stays null rather than being invented (never a remembered or guessed
            // company name such as Saudi Aramco, Shell, BP, Chevron, TotalEnergies or IPS).
            if (effective == null || string.IsNullOrEmpty(effective.WinningPackId)) return null;
            return packName;
        }

        private static string? FirstClause(IList<StandardClauseAuthorityResolution> clauses, Func<StandardClauseAuthorityResolution, string?> selector)
        {
            foreach (StandardClauseAuthorityResolution clause in clauses)
            {
                string? value = selector(clause);
                if (!string.IsNullOrEmpty(value)) return value;
            }
            return null;
        }

        private static int? FirstClauseNullable(IList<StandardClauseAuthorityResolution> clauses, Func<StandardClauseAuthorityResolution, int?> selector)
        {
            foreach (StandardClauseAuthorityResolution clause in clauses)
            {
                int? value = selector(clause);
                if (value.HasValue) return value;
            }
            return null;
        }

        private static bool IsCleanDecision(StandardRuleExecutionGateDecision? gate)
        {
            if (gate == null) return false;
            return string.IsNullOrEmpty(gate.DecisionState) || gate.DecisionState == StandardDecisionStates.Pass;
        }

        /// <summary>Maps the applicability key onto the bounded subject type vocabulary.</summary>
        private static string? ResolveSubjectType(EngineeringDistanceRuleDefinition rule)
        {
            if (string.IsNullOrEmpty(rule.RequiredApplicabilityKey)) return null;
            return rule.RequiredApplicabilityKey;
        }

        /// <summary>
        /// Records the constraint treatment. Treatment is metadata only and never authority-bearing: it can
        /// never make a non-executable rule executable. Project-criteria rules without an approved governed
        /// basis stay PROJECT_REQUIREMENT / REVIEW_REQUIRED; quarantined and conflicting rules stay
        /// REVIEW_REQUIRED.
        /// </summary>
        private static string? ResolveConstraintTreatment(EngineeringDistanceRuleDefinition rule, StandardRuleExecutionGateDecision? gate, IList<StandardClauseAuthorityResolution> clauses)
        {
            if (gate != null && !gate.AutoExecutionAllowed) return ConstraintTreatment.ReviewRequired;

            string? authorityClass = FirstClause(clauses, x => x.AuthorityClass);
            if (!string.IsNullOrEmpty(authorityClass))
            {
                if (authorityClass == StandardAuthorityClasses.Quarantined) return ConstraintTreatment.ReviewRequired;
                if (authorityClass == StandardAuthorityClasses.LegacyConflict) return ConstraintTreatment.ReviewRequired;
                if (authorityClass == StandardAuthorityClasses.GoverningProject) return ConstraintTreatment.ProjectRequirement;
            }

            // Project criteria remain project requirements unless an approved governed basis exists.
            if (rule.CriteriaProvenance != null) return ConstraintTreatment.ProjectRequirement;

            return gate != null && gate.AutoExecutionAllowed ? ConstraintTreatment.HardConstraint : ConstraintTreatment.ReviewRequired;
        }

        private static void ApplyGovernedResolution(StandardsPackRule packRule, string ruleId, IReadOnlyDictionary<string, ProjectDistanceGovernedRuleResolution>? governedResolutions)
        {
            if (governedResolutions == null) return;
            if (!governedResolutions.TryGetValue(ruleId, out ProjectDistanceGovernedRuleResolution? resolution) || resolution == null) return;

            // P9 project-governance state is retained verbatim; PROJECT_RULE_ONLY never becomes a company
            // standard and never gains a fabricated source clause.
            packRule.ConstraintTreatment = resolution.State == ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly
                ? ConstraintTreatment.ProjectRequirement
                : packRule.ConstraintTreatment;

            if (resolution.GovernanceProvenance != null)
            {
                // An approved governed candidate supplies the authoritative criterion; it does not invent one.
                if (resolution.GovernanceProvenance.ApprovedMinimumDistanceMeters > 0m)
                {
                    packRule.MinimumDistanceMeters = resolution.GovernanceProvenance.ApprovedMinimumDistanceMeters;
                }
                if (!string.IsNullOrEmpty(resolution.GovernanceProvenance.AuthorityState))
                {
                    packRule.DecisionState = resolution.GovernanceProvenance.AuthorityState;
                }
            }
        }

        private static void ApplyCurrentness(StandardsPackRule packRule, string ruleId, IReadOnlyDictionary<string, EngineeringDistanceApprovalCurrentnessResult>? currentnessByRuleId)
        {
            if (currentnessByRuleId == null) return;
            if (!currentnessByRuleId.TryGetValue(ruleId, out EngineeringDistanceApprovalCurrentnessResult? currentness) || currentness == null) return;

            // Staleness blocks execution: a stale approval cannot execute and is surfaced as review-required.
            packRule.CurrentnessState = currentness.Status;
            packRule.CurrentnessReasons = currentness.Reasons != null
                ? currentness.Reasons.Where(x => !string.IsNullOrEmpty(x)).OrderBy(x => x, StringComparer.Ordinal).ToList()
                : new List<string>();
            if (currentness.Status == EngineeringDistanceApprovalCurrentnessStates.Stale)
            {
                packRule.AutoExecutionAllowed = false;
                packRule.EngineeringReviewRequired = true;
                if (string.IsNullOrEmpty(packRule.ConstraintTreatment) || packRule.ConstraintTreatment != ConstraintTreatment.ReviewRequired)
                {
                    packRule.ConstraintTreatment = ConstraintTreatment.ReviewRequired;
                }
            }
        }

        private static void ApplyApprovalAuthority(StandardsPackRule packRule, string ruleId, IReadOnlyDictionary<string, EngineeringDistanceApprovalAuthorityResult>? approvalAuthorityByRuleId)
        {
            if (approvalAuthorityByRuleId == null) return;
            if (!approvalAuthorityByRuleId.TryGetValue(ruleId, out EngineeringDistanceApprovalAuthorityResult? authority) || authority == null) return;

            // P8 human review and P9 engineering approval are distinct. A NOT_APPROVED / STALE_APPROVAL /
            // inactive approval never becomes executable authority.
            packRule.DecisionState = string.IsNullOrEmpty(authority.State) ? packRule.DecisionState : authority.State;
            if (authority.State != EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic)
            {
                packRule.AutoExecutionAllowed = false;
                packRule.EngineeringReviewRequired = true;
                if (packRule.ConstraintTreatment != ConstraintTreatment.ReviewRequired)
                {
                    packRule.ConstraintTreatment = ConstraintTreatment.ReviewRequired;
                }
            }
        }

        private static StandardClauseAuthorityResolution CopyClause(StandardClauseAuthorityResolution clause) => new StandardClauseAuthorityResolution
        {
            ClauseId = clause.ClauseId,
            SourceId = clause.SourceId,
            PolicyId = clause.PolicyId,
            PolicyClauseId = clause.PolicyClauseId,
            AuthorityClass = clause.AuthorityClass,
            EnforcementMode = clause.EnforcementMode,
            Rationale = clause.Rationale,
            SourceDocumentNumber = clause.SourceDocumentNumber,
            SourceTitle = clause.SourceTitle,
            SourceIssuer = clause.SourceIssuer,
            SourceRevisionOrEdition = clause.SourceRevisionOrEdition,
            SourceFileName = clause.SourceFileName,
            SourceSha256 = clause.SourceSha256,
            ClauseReference = clause.ClauseReference,
            ClauseText = clause.ClauseText,
            ClauseHeading = clause.ClauseHeading,
            PageNumber = clause.PageNumber
        };
    }
}

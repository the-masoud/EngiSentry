using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>
    /// The validated outcome of an explicit <see cref="ProjectRuleAuthorityBinding"/>. A binding grants
    /// execution authority only when every check passes; any problem leaves <see cref="IsValid"/> false and
    /// the project rule non-executable (fail-closed). <see cref="Reasons"/> records why, for audit evidence.
    /// </summary>
    public sealed class ProjectRuleAuthorityBindingValidationResult
    {
        public bool IsValid { get; set; }

        /// <summary>The bound authority gate decision, carried so the projection can stamp the project rule
        /// with the authority chain's own evidence instead of a copy. Set only when valid.</summary>
        public StandardRuleExecutionGateDecision? BoundGateDecision { get; set; }

        /// <summary>The resolved clause evidence the binding was validated against (verbatim copies).</summary>
        public List<StandardClauseAuthorityResolution> Clauses { get; set; } = new List<StandardClauseAuthorityResolution>();

        public string? AuthorityClass { get; set; }
        public string? SourceSha256 { get; set; }
        public List<string> Reasons { get; set; } = new List<string>();
    }

    /// <summary>
    /// Validates an explicit project-rule → authority-evidence binding against the standards-authority chain's
    /// actual resolution. This is the sole place a project distance rule can acquire execution authority, and
    /// it fail-closes on every dimension: shape, existence of the bound authority, deterministic enforcement,
    /// clean decision state, field-by-field evidence consistency, the accepted authority/mode pairing, and the
    /// existing approval-currentness/approval-authority (trust) verdicts.
    ///
    /// The validator never grants authority it was not able to confirm in the authority context, and never
    /// consults a governed-compatible numerical value: a value is not authority.
    /// </summary>
    public static class ProjectRuleAuthorityBindingValidator
    {
        /// <summary>
        /// Validates one binding. <paramref name="currentnessByRuleId"/> and <paramref name="approvalAuthorityByRuleId"/>
        /// are the existing P9 evidence maps; they are consulted only where the chain already produced a verdict,
        /// preserving — never bypassing — the accepted approval/currentness/trust checks.
        /// </summary>
        public static ProjectRuleAuthorityBindingValidationResult Validate(
            ProjectRuleAuthorityBinding? binding,
            PlotPlanStandardsAuthorityRuntimeContext? authorityContext,
            IReadOnlyDictionary<string, EngineeringDistanceApprovalCurrentnessResult>? currentnessByRuleId = null,
            IReadOnlyDictionary<string, EngineeringDistanceApprovalAuthorityResult>? approvalAuthorityByRuleId = null)
        {
            var result = new ProjectRuleAuthorityBindingValidationResult();

            // 1. A binding must exist and be well-formed. No binding => no authority (fail-closed by default).
            if (binding == null) { result.Reasons.Add("authority binding is absent"); return result; }
            if (string.IsNullOrWhiteSpace(binding.ProjectRuleId)) { result.Reasons.Add("authority binding ProjectRuleId is required"); return result; }
            if (string.IsNullOrWhiteSpace(binding.AuthorityRuleId)) { result.Reasons.Add("authority binding AuthorityRuleId is required"); return result; }
            if (string.Equals(binding.ProjectRuleId, binding.AuthorityRuleId, StringComparison.Ordinal)) { result.Reasons.Add("authority binding must not map a rule to itself: " + binding.ProjectRuleId); return result; }
            if (authorityContext == null) { result.Reasons.Add("standards authority context is required to validate a binding"); return result; }

            // 2. The bound authority rule must EXIST in the real authority chain: a resolved gate decision AND
            //    resolved clause evidence. A binding may not point at an invented authority rule.
            StandardRuleExecutionGateDecision? gate = authorityContext.ExecutionGate?.Rules
                ?.FirstOrDefault(x => x != null && string.Equals(x.RuleId, binding.AuthorityRuleId, StringComparison.Ordinal));
            StandardRuleAuthorityResolution? authority = authorityContext.AuthorityResolution?.Rules
                ?.FirstOrDefault(x => x != null && string.Equals(x.RuleId, binding.AuthorityRuleId, StringComparison.Ordinal));
            if (gate == null) { result.Reasons.Add("binding AuthorityRuleId resolves to no authority gate decision: " + binding.AuthorityRuleId); return result; }
            if (authority == null || authority.Clauses == null || authority.Clauses.Count == 0) { result.Reasons.Add("binding AuthorityRuleId resolves to no clause evidence: " + binding.AuthorityRuleId); return result; }

            // 3. Only a DETERMINISTIC gate carries execution authority; every other mode is review/advisory.
            if (!gate.AutoExecutionAllowed || gate.EffectiveEnforcementMode != StandardEnforcementModes.Deterministic)
            { result.Reasons.Add("bound authority is not DETERMINISTIC: " + gate.EffectiveEnforcementMode + " (" + binding.AuthorityRuleId + ")"); return result; }

            // 4. The gate's decision state must be clean (null/PASS). Quarantined/expert-review gates never grant.
            if (!string.IsNullOrEmpty(gate.DecisionState) && gate.DecisionState != StandardDecisionStates.Pass)
            { result.Reasons.Add("bound authority gate decision state is not clean: " + gate.DecisionState); return result; }

            // 5. Evidence consistency: every cited clause must be one the chain actually resolved for the bound
            //    rule, and every provenance field the binding records must match that resolution verbatim.
            Dictionary<string, StandardClauseAuthorityResolution> resolved = authority.Clauses
                .Where(x => x != null && !string.IsNullOrEmpty(x.ClauseId))
                .ToDictionary(x => x.ClauseId!, x => x, StringComparer.Ordinal);

            List<string> cited = (binding.ClauseIds ?? new List<string>()).Where(x => !string.IsNullOrEmpty(x)).ToList();
            if (cited.Count == 0) cited.AddRange(resolved.Keys.OrderBy(x => x, StringComparer.Ordinal));

            List<StandardClauseAuthorityResolution> matched = new List<StandardClauseAuthorityResolution>();
            foreach (string clauseId in cited)
            {
                if (!resolved.TryGetValue(clauseId, out StandardClauseAuthorityResolution? clause) || clause == null)
                { result.Reasons.Add("binding cites a clause the authority chain did not resolve: " + clauseId); return result; }
                matched.Add(clause);
            }

            StandardClauseAuthorityResolution governing = matched.OrderBy(x => x.ClauseId, StringComparer.Ordinal).First();

            if (!string.IsNullOrEmpty(binding.AuthorityClass) && !string.Equals(binding.AuthorityClass, governing.AuthorityClass, StringComparison.Ordinal))
            { result.Reasons.Add("binding AuthorityClass mismatch: recorded " + binding.AuthorityClass + " but the authority chain resolved " + governing.AuthorityClass); return result; }
            if (!string.IsNullOrEmpty(binding.EffectiveEnforcementMode) && !string.Equals(binding.EffectiveEnforcementMode, gate.EffectiveEnforcementMode, StringComparison.Ordinal))
            { result.Reasons.Add("binding EnforcementMode mismatch: recorded " + binding.EffectiveEnforcementMode + " but the gate resolved " + gate.EffectiveEnforcementMode); return result; }
            if (!string.IsNullOrEmpty(binding.SourceId) && !string.Equals(binding.SourceId, governing.SourceId, StringComparison.Ordinal))
            { result.Reasons.Add("binding SourceId mismatch: " + binding.SourceId); return result; }
            if (!string.IsNullOrEmpty(binding.SourceSha256) && !string.Equals(binding.SourceSha256, ResolveSourceSha256(authorityContext, governing), StringComparison.Ordinal))
            { result.Reasons.Add("binding SourceSha256 mismatch: the recorded source hash is not the registered source hash"); return result; }
            if (!string.IsNullOrEmpty(binding.PolicyId) && !string.Equals(binding.PolicyId, governing.PolicyId, StringComparison.Ordinal))
            { result.Reasons.Add("binding PolicyId mismatch: " + binding.PolicyId); return result; }

            // 6. Only a governing-project / active-primary authority class can legitimately pair with
            //    DETERMINISTIC under the accepted authority/mode pairing. Quarantined and legacy-conflict
            //    sources never execute regardless of what a binding claims.
            if (governing.AuthorityClass != StandardAuthorityClasses.GoverningProject && governing.AuthorityClass != StandardAuthorityClasses.ActivePrimary)
            { result.Reasons.Add("bound authority class cannot carry deterministic authority: " + governing.AuthorityClass); return result; }

            // 7. Preserve the existing P9 approval/trust checks: where the chain produced an approval-authority
            //    verdict it must be GOVERNING_PROJECT_DETERMINISTIC, and where it produced a currentness verdict
            //    the approval must be CURRENT. These are the accepted checks, not new ones.
            if (approvalAuthorityByRuleId != null && approvalAuthorityByRuleId.TryGetValue(binding.ProjectRuleId, out EngineeringDistanceApprovalAuthorityResult? approvalAuthority) && approvalAuthority != null)
            {
                if (approvalAuthority.State != EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic)
                { result.Reasons.Add("approval authority state is not deterministic: " + approvalAuthority.State); return result; }
                if (!string.IsNullOrEmpty(binding.ApprovalRecordId) && !string.Equals(binding.ApprovalRecordId, approvalAuthority.ApprovalRecordId, StringComparison.Ordinal))
                { result.Reasons.Add("binding ApprovalRecordId does not match the resolved approval authority"); return result; }
            }
            if (currentnessByRuleId != null && currentnessByRuleId.TryGetValue(binding.ProjectRuleId, out EngineeringDistanceApprovalCurrentnessResult? currentness) && currentness != null)
            {
                if (currentness.Status == EngineeringDistanceApprovalCurrentnessStates.Stale)
                { result.Reasons.Add("approval authority is stale: " + string.Join(", ", currentness.Reasons ?? new List<string>())); return result; }
            }

            result.IsValid = true;
            result.BoundGateDecision = gate;
            result.Clauses = matched.OrderBy(x => x.ClauseId, StringComparer.Ordinal).Select(CopyClause).ToList();
            result.AuthorityClass = governing.AuthorityClass;
            result.SourceSha256 = ResolveSourceSha256(authorityContext, governing);
            return result;
        }

        /// <summary>The authoritative source hash for the governing clause: the clause's own recorded hash, or —
        /// because the accepted authority resolver leaves clause hashes to the source registry — the registered
        /// source's hash. Null when neither is available.</summary>
        private static string? ResolveSourceSha256(PlotPlanStandardsAuthorityRuntimeContext authorityContext, StandardClauseAuthorityResolution governing)
        {
            if (!string.IsNullOrEmpty(governing.SourceSha256)) return governing.SourceSha256;
            if (authorityContext.SourceRegistry != null && authorityContext.SourceRegistry.TryGet(governing.SourceId, out StandardSourceDefinition? source) && source != null) return source.SourceSha256;
            return null;
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

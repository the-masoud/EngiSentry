using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    /// <summary>
    /// An explicit, evidence-backed binding between a project distance rule (identity namespace
    /// <c>project.plotplan.equipment.*</c>) and the authority evidence that may govern its executability
    /// (a rule resolved by the existing P6→P9 standards-authority chain, identity namespace
    /// <c>runtime.plotplan.authority.*</c> / <c>std.plotplan.*</c>).
    ///
    /// This contract exists because those two identity namespaces are deliberately distinct: the governed
    /// projection must NEVER join them by a guessed, derived or hard-coded rule-id mapping. Instead a binding
    /// states explicitly WHICH authority rule governs the project rule and WHICH resolved evidence (clause,
    /// source hash, policy, authority class, enforcement mode) it relies on. Before it can grant anything the
    /// binding is validated field-by-field against the authority chain's ACTUAL resolution, so it can never
    /// invent authority: an absent, mismatched, stale, untrusted, quarantined or non-deterministic binding
    /// grants nothing and the project rule stays non-executable (fail-closed).
    ///
    /// Hard invariants:
    /// - With no validated binding, a project rule receives no execution authority; the real production path,
    ///   which supplies no binding, therefore stays at 0 auto-executable rules.
    /// - A governed-compatible VALUE alone (an approved minimum distance from
    ///   <see cref="ProjectDistanceGovernanceProvenance"/>) is not authority and never grants executability;
    ///   only a validated binding to DETERMINISTIC authority evidence can.
    /// - The binding carries no authority of its own; it is provenance that must be confirmed by the authority
    ///   chain before the existing approval/currentness/trust checks additionally apply.
    /// </summary>
    [DataContract]
    public sealed class ProjectRuleAuthorityBinding
    {
        /// <summary>The governed project distance rule this binding authorizes, e.g.
        /// project.plotplan.equipment.road.minimum-clearance.</summary>
        [DataMember(Order = 1)] public string ProjectRuleId { get; set; } = string.Empty;

        /// <summary>The authority-chain rule whose resolved gate decision governs the project rule. Must exist
        /// in the resolved standards-authority context; it is never looked up by proximity or naming
        /// convention.</summary>
        [DataMember(Order = 2)] public string AuthorityRuleId { get; set; } = string.Empty;

        /// <summary>The clauses the binding relies on. Every cited clause must be one the authority chain
        /// actually resolved for <see cref="AuthorityRuleId"/>; empty means "all clauses the chain resolved".</summary>
        [DataMember(Order = 3)] public List<string> ClauseIds { get; set; } = new List<string>();

        [DataMember(Order = 4, EmitDefaultValue = false)] public string? PolicyId { get; set; }
        [DataMember(Order = 5, EmitDefaultValue = false)] public string? SourceId { get; set; }
        [DataMember(Order = 6, EmitDefaultValue = false)] public string? SourceSha256 { get; set; }
        [DataMember(Order = 7, EmitDefaultValue = false)] public string? AuthorityClass { get; set; }
        [DataMember(Order = 8, EmitDefaultValue = false)] public string? EffectiveEnforcementMode { get; set; }
        [DataMember(Order = 9, EmitDefaultValue = false)] public string? ApprovalRecordId { get; set; }
        [DataMember(Order = 10, EmitDefaultValue = false)] public string? AuthorityState { get; set; }
        [DataMember(Order = 11, EmitDefaultValue = false)] public string? Rationale { get; set; }

        /// <summary>The absolute project directory this binding is scoped to (normalized upper-cased form as
        /// recorded by the signed intake artifact). A binding is authoritative only inside its own project scope;
        /// the intake loader compares this against the current project before the binding is ever handed to the
        /// projection. It is part of the signed payload, so it cannot be retargeted without invalidating the
        /// signature.</summary>
        [DataMember(Order = 12, EmitDefaultValue = false)] public string? ProjectDirectory { get; set; }
    }
}

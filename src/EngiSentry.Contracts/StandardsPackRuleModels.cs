using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    /// <summary>
    /// Read-only governed projection over the existing P6→P9 authority/governance chain (Phase A
    /// architecture D). This is contract scaffolding only: it is NOT a second authority system and never
    /// creates a source, clause, policy, approval, or trust decision. Every field either reads an existing
    /// authoritative type verbatim, carries an existing derived value, or is one of the three authority-free
    /// metadata fields (Company, Exceptions, EngineeringReviewRequired).
    ///
    /// Deliberately unpopulated in Phase B. It contains NO company rules of any kind, NO clause text, and NO
    /// distances: an unavailable or unauthorized standard is not a rule and stays SOURCE_REQUIRED
    /// (expressed through the existing StandardDecisionStates / StandardEnforcementModes vocabulary).
    /// </summary>
    [DataContract]
    public sealed class StandardsPackRule
    {
        /// <summary>From <see cref="EffectiveStandardRuleDefinition.RuleId"/>; identity validated by
        /// EngineeringDistanceRuleRegistry.Create.</summary>
        [DataMember(Order = 1)] public string RuleId { get; set; } = string.Empty;

        /// <summary>Metadata only: the existing pack contract has no company attribute. Carries no
        /// authority and defaults to empty.</summary>
        [DataMember(Order = 2, EmitDefaultValue = false)] public string? Company { get; set; }
        [DataMember(Order = 3, EmitDefaultValue = false)] public string? PackId { get; set; }
        [DataMember(Order = 4, EmitDefaultValue = false)] public string? PackName { get; set; }

        /// <summary>From <see cref="StandardSourceDefinition.DocumentNumber"/> / .Title.</summary>
        [DataMember(Order = 5, EmitDefaultValue = false)] public string? DocumentNumber { get; set; }
        [DataMember(Order = 6, EmitDefaultValue = false)] public string? DocumentTitle { get; set; }
        /// <summary>From <see cref="StandardSourceDefinition.RevisionOrEdition"/>.</summary>
        [DataMember(Order = 7, EmitDefaultValue = false)] public string? RevisionOrEdition { get; set; }

        /// <summary>From <see cref="StandardClauseDefinition.ClauseId"/> / .ClauseReference.</summary>
        [DataMember(Order = 8, EmitDefaultValue = false)] public string? ClauseId { get; set; }
        [DataMember(Order = 9, EmitDefaultValue = false)] public string? ClauseReference { get; set; }
        /// <summary>From <see cref="StandardClauseDefinition.PageNumber"/>.</summary>
        [DataMember(Order = 10, EmitDefaultValue = false)] public int? PageNumber { get; set; }
        /// <summary>From <see cref="StandardClauseDefinition.ClauseText"/>.</summary>
        [DataMember(Order = 11, EmitDefaultValue = false)] public string? RequirementText { get; set; }

        /// <summary>Maps onto <see cref="EngineeringDistanceEvidenceKeys"/> applicability keys.</summary>
        [DataMember(Order = 12, EmitDefaultValue = false)] public string? SubjectType { get; set; }
        /// <summary>Maps onto <see cref="EngineeringDistanceEvidenceKeys"/> measurement basis keys.</summary>
        [DataMember(Order = 13, EmitDefaultValue = false)] public string? ReferenceType { get; set; }
        [DataMember(Order = 14, EmitDefaultValue = false)] public string? MeasurementBasisKey { get; set; }

        /// <summary>From <see cref="EngineeringDistanceRuleDefinition.MinimumDistanceMeters"/>; for governed
        /// candidates, executable only through EngineeringDistanceApprovalGate.TryCreateExecutableRule.</summary>
        [DataMember(Order = 15)] public decimal MinimumDistanceMeters { get; set; }

        [DataMember(Order = 16, EmitDefaultValue = false)] public string? ApplicabilityKey { get; set; }

        /// <summary>Metadata only: no existing type carries exceptions. Populated solely from authorized pack
        /// metadata, never inferred from clause text.</summary>
        [DataMember(Order = 17)] public List<string> Exceptions { get; set; } = new List<string>();

        /// <summary>From <see cref="StandardAuthorityPolicyDefinition.AuthorityClass"/> — one of the six
        /// existing <see cref="StandardAuthorityClasses"/>.</summary>
        [DataMember(Order = 18, EmitDefaultValue = false)] public string? AuthorityClass { get; set; }

        /// <summary>Derived, never asserted: true unless the existing execution gate allows auto-execution
        /// with a null/PASS decision state. Consistent with the accepted runtime invariant of 0
        /// auto-executable rules.</summary>
        [DataMember(Order = 19)] public bool EngineeringReviewRequired { get; set; } = true;

        /// <summary>From <see cref="StandardRuleExecutionGateDecision"/> — authoritative.</summary>
        [DataMember(Order = 20, EmitDefaultValue = false)] public string? EffectiveEnforcementMode { get; set; }
        [DataMember(Order = 21)] public bool AutoExecutionAllowed { get; set; }
        [DataMember(Order = 22, EmitDefaultValue = false)] public string? DecisionState { get; set; }

        /// <summary>Clause evidence in the existing accepted resolution shape.</summary>
        [DataMember(Order = 23)] public List<StandardClauseAuthorityResolution> Clauses { get; set; } = new List<StandardClauseAuthorityResolution>();

        /// <summary>From <see cref="StandardSourceDefinition.SourceSha256"/> /
        /// <see cref="StandardClauseAuthorityResolution.SourceSha256"/>.</summary>
        [DataMember(Order = 24, EmitDefaultValue = false)] public string? SourceSha256 { get; set; }

        /// <summary>From EngineeringDistanceApprovalCurrentnessStates / Reasons produced by the existing
        /// EngineeringDistanceApprovalCurrentnessValidator; review-side staleness via the existing
        /// HumanEngineeringReviewDecisionStalenessValidator.</summary>
        [DataMember(Order = 25, EmitDefaultValue = false)] public string? CurrentnessState { get; set; }
        [DataMember(Order = 26)] public List<string> CurrentnessReasons { get; set; } = new List<string>();

        /// <summary>Treatment recorded at pack-metadata time (Phase A E.2); never authority-bearing.</summary>
        [DataMember(Order = 27, EmitDefaultValue = false)] public string? ConstraintTreatment { get; set; }
    }
}

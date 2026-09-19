using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    /// <summary>
    /// One deterministic proposed point position for one equipment placement, together with its complete
    /// re-evaluated constraint result set. Distinct name by design: <see cref="EngineeringDistanceCandidateDefinition"/>
    /// already means a candidate engineering criterion/rule awaiting engineering approval, and MUST NOT be
    /// reused for proposed equipment positions. <c>Placement</c> is the established domain term for an
    /// equipment location in this codebase (EquipmentPlacement, PlacementId), which makes the collision
    /// structurally impossible. See Phase A architecture F.1.
    /// </summary>
    [DataContract]
    public sealed class PlacementProposalCandidate
    {
        /// <summary>Deterministic identity, e.g. proposal-candidate:&lt;PlacementId&gt;:&lt;sequence&gt;.
        /// Never random and never time-derived (the determinism discipline already enforced by
        /// StandardAuthorityReviewPackageBuilder).</summary>
        [DataMember(Order = 1)] public string CandidateId { get; set; } = string.Empty;

        [DataMember(Order = 2)] public string PlacementId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string EquipmentEntityId { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string NormalizedTag { get; set; } = string.Empty;

        /// <summary>Existing resolved coordinate point (accepted point-based semantics).</summary>
        [DataMember(Order = 5)] public double OriginalEasting { get; set; }
        [DataMember(Order = 6)] public double OriginalNorthing { get; set; }

        [DataMember(Order = 7)] public double ProposedEasting { get; set; }
        [DataMember(Order = 8)] public double ProposedNorthing { get; set; }

        [DataMember(Order = 9)] public double DisplacementX { get; set; }
        [DataMember(Order = 10)] public double DisplacementY { get; set; }
        [DataMember(Order = 11)] public double DisplacementDistance { get; set; }

        /// <summary>Deterministic direction label (E, NE, N, ...).</summary>
        [DataMember(Order = 12, EmitDefaultValue = false)] public string? DisplacementDirection { get; set; }

        [DataMember(Order = 13, EmitDefaultValue = false)] public string? GeneratingConstraintId { get; set; }
        [DataMember(Order = 14, EmitDefaultValue = false)] public string? GeneratingRuleId { get; set; }

        /// <summary>Portable source/evidence identifiers only. No host entity is ever carried.</summary>
        [DataMember(Order = 15, EmitDefaultValue = false)] public string? LabelSourceHandle { get; set; }
        [DataMember(Order = 16, EmitDefaultValue = false)] public string? GeometrySourceHandle { get; set; }
        [DataMember(Order = 17, EmitDefaultValue = false)] public string? GeometrySourceLayer { get; set; }
        [DataMember(Order = 18, EmitDefaultValue = false)] public string? GeometryKind { get; set; }

        /// <summary>Every bounded constraint re-evaluated at this candidate point. Presence in this list
        /// means evaluated, never satisfied.</summary>
        [DataMember(Order = 19)] public List<PlacementProposalConstraintOutcome> CheckedConstraints { get; set; } = new List<PlacementProposalConstraintOutcome>();

        /// <summary>Mandatory non-empty in v1: statically declares the known-unavailable constraint
        /// categories, so "not checked" is always visible.</summary>
        [DataMember(Order = 20)] public List<string> UncheckedConstraints { get; set; } = new List<string>();

        [DataMember(Order = 21)] public List<string> Assumptions { get; set; } = new List<string>();

        /// <summary>Candidates generated and then discarded, each with its deterministic reason.</summary>
        [DataMember(Order = 22)] public List<string> RejectedReasons { get; set; } = new List<string>();

        /// <summary>Hard true for every v1 candidate: no field path produces a candidate presented as final.</summary>
        [DataMember(Order = 23)] public bool EngineeringReviewRequired { get; set; } = true;
    }

    /// <summary>
    /// Deterministic ranking summary (Phase A architecture J.3). Makes candidate ranking auditable from the
    /// artifact alone: counts per state, the winning candidate identity, and the reason each rejected
    /// candidate was dropped. Ranking orders already-computed outcomes and never changes a measurement,
    /// evidence, or governance state.
    /// </summary>
    [DataContract]
    public sealed class PlacementProposalRankingSummary
    {
        /// <summary>Candidates in ranked order (mandatory-constraint validity first, then unresolved counts
        /// retained visibly, then the declared objective, then deterministic tie-breaking).</summary>
        [DataMember(Order = 1)] public List<string> OrderedCandidateIds { get; set; } = new List<string>();

        [DataMember(Order = 2, EmitDefaultValue = false)] public string? WinningCandidateId { get; set; }

        [DataMember(Order = 3)] public int SatisfiedCount { get; set; }
        [DataMember(Order = 4)] public int ViolatedCount { get; set; }
        /// <summary>Never collapsed to zero and never hidden.</summary>
        [DataMember(Order = 5)] public int InsufficientEvidenceCount { get; set; }
        [DataMember(Order = 6)] public int ReviewRequiredCount { get; set; }
        [DataMember(Order = 7)] public int NotApplicableCount { get; set; }

        /// <summary>Each rejected candidate with the deterministic reason it was dropped.</summary>
        [DataMember(Order = 8)] public List<PlacementProposalRejectedCandidate> RejectedCandidates { get; set; } = new List<PlacementProposalRejectedCandidate>();
    }

    [DataContract]
    public sealed class PlacementProposalRejectedCandidate
    {
        [DataMember(Order = 1)] public string CandidateId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string RejectionReason { get; set; } = string.Empty;
    }
}

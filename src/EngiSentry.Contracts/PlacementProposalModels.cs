using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    /// <summary>
    /// One complete, explainable, read-only CHECK + SUGGEST explanation for one governing violation
    /// (Phase A architecture G.1). No field in this contract can express a drawing mutation: the type
    /// contains no host entity and no write capability. SUGGEST itself is not implemented in Phase B.
    /// </summary>
    [DataContract]
    public sealed class PlacementProposal
    {
        /// <summary>Deterministic identity from PlacementId + generating constraint + candidate set.
        /// Never random, never time-derived.</summary>
        [DataMember(Order = 1)] public string ProposalId { get; set; } = string.Empty;

        /// <summary>The violated governed rule.</summary>
        [DataMember(Order = 2)] public StandardsPackRule? ViolatedRule { get; set; }

        [DataMember(Order = 3)] public double ObservedDistanceMeters { get; set; }
        [DataMember(Order = 4, EmitDefaultValue = false)] public string? ObservedMeasurementBasis { get; set; }
        [DataMember(Order = 5, EmitDefaultValue = false)] public string? ObservedMeasurementId { get; set; }

        [DataMember(Order = 6)] public decimal RequiredDistanceMeters { get; set; }

        [DataMember(Order = 7, EmitDefaultValue = false)] public string? MeasurementBasisKey { get; set; }
        [DataMember(Order = 8, EmitDefaultValue = false)] public string? MeasurementApplicabilityKey { get; set; }
        [DataMember(Order = 9, EmitDefaultValue = false)] public string? UnitContext { get; set; }

        /// <summary>Source/clause evidence in the existing accepted citation shape.</summary>
        [DataMember(Order = 10)] public List<StandardClauseCitation> ClauseEvidence { get; set; } = new List<StandardClauseCitation>();

        [DataMember(Order = 11)] public double OriginalEasting { get; set; }
        [DataMember(Order = 12)] public double OriginalNorthing { get; set; }
        [DataMember(Order = 13)] public double ProposedEasting { get; set; }
        [DataMember(Order = 14)] public double ProposedNorthing { get; set; }
        [DataMember(Order = 15)] public double MovementDistanceMeters { get; set; }
        [DataMember(Order = 16, EmitDefaultValue = false)] public string? MovementDirection { get; set; }

        /// <summary>Road / Plant Fence / Unit Boundary re-evaluation at the proposed point, or null with a
        /// recorded reason.</summary>
        [DataMember(Order = 17, EmitDefaultValue = false)] public PlacementProposalConstraintOutcome? RoadReEvaluation { get; set; }
        [DataMember(Order = 18, EmitDefaultValue = false)] public PlacementProposalConstraintOutcome? PlantFenceReEvaluation { get; set; }
        [DataMember(Order = 19, EmitDefaultValue = false)] public PlacementProposalConstraintOutcome? UnitBoundaryReEvaluation { get; set; }

        [DataMember(Order = 20)] public List<PlacementProposalConstraintOutcome> SatisfiedConstraints { get; set; } = new List<PlacementProposalConstraintOutcome>();
        [DataMember(Order = 21)] public List<PlacementProposalConstraintOutcome> ViolatedConstraints { get; set; } = new List<PlacementProposalConstraintOutcome>();
        /// <summary>Never collapsed into satisfied.</summary>
        [DataMember(Order = 22)] public List<PlacementProposalConstraintOutcome> InsufficientEvidenceConstraints { get; set; } = new List<PlacementProposalConstraintOutcome>();
        [DataMember(Order = 23)] public List<string> UncheckedConstraints { get; set; } = new List<string>();
        [DataMember(Order = 24)] public List<string> Assumptions { get; set; } = new List<string>();
        [DataMember(Order = 25)] public List<PlacementProposalCandidate> RejectedCandidates { get; set; } = new List<PlacementProposalCandidate>();

        /// <summary>Always true in v1, plus the recorded reason.</summary>
        [DataMember(Order = 26)] public bool EngineeringReviewRequired { get; set; } = true;
        [DataMember(Order = 27, EmitDefaultValue = false)] public string? EngineeringReviewReason { get; set; }

        /// <summary>Mandatory non-empty in v1: statically includes the point-based limitation.</summary>
        [DataMember(Order = 28)] public List<string> KnownLimitations { get; set; } = new List<string>();
    }

    /// <summary>
    /// Static default assumption list (Phase A architecture G.3). Non-empty by construction in v1.
    /// </summary>
    public static class ProposalAssumptions
    {
        public static readonly string[] Defaults =
        {
            "Equipment location is represented as a single resolved coordinate point.",
            "Road classification uses the closed explicit road-edge allow-list; labels merely containing 'ROAD' are not classified as road edges.",
            "Curved geometry (bulge != 0) is not supported and cannot underpin a facility element.",
            "Association distance and ambiguity margins follow the existing accepted extraction settings.",
            "Project criteria remain PROJECT_RULE_ONLY unless an approved governed basis exists."
        };
    }

    /// <summary>
    /// Static declaration of the constraint categories v1 does not check (Phase A architecture G.4).
    /// Mandatory non-empty so 'not checked' is always visible.
    /// </summary>
    public static class ProposalUncheckedConstraints
    {
        public static readonly string[] Defaults =
        {
            "Pipe routing",
            "Maintenance access envelopes",
            "Underground utilities",
            "Crane and nozzle interactions",
            "Process layout",
            "Equipment footprint-based clearance",
            "Occupied-building siting",
            "Flare and hazardous-area separation",
            "Any constraint whose required subject kind is not Road, Plant Fence, or Unit Boundary"
        };
    }

    /// <summary>
    /// Static known-limitation list (Phase A architecture G.2). The point-based limitation is carried
    /// verbatim and is a contract-level invariant, not a UI nicety, so it cannot be silently dropped.
    /// </summary>
    public static class ProposalKnownLimitations
    {
        public const string PointBasedLimitation =
            "Equipment location is represented as a single resolved coordinate point derived from drawing " +
            "coordinate evidence. Equipment footprint, orientation, and physical extent are not captured. " +
            "Distances are point-to-geometry measurements, not edge-to-edge clearances. Candidate positions " +
            "are candidate points. Pipe routing, maintenance access, underground utilities, crane and nozzle " +
            "interactions, and process layout are not evaluated. Engineering review is required.";

        public static readonly string[] Defaults =
        {
            PointBasedLimitation
        };
    }
}

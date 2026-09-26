using System;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    public static class PlacementProposalDispositionSchema
    {
        public const string Current = "PPD-1";
    }

    public static class PlacementProposalDispositions
    {
        public const string PendingReview = "PENDING_REVIEW";
        public const string Approved = "APPROVED";
        public const string Rejected = "REJECTED";
        public const string Modified = "MODIFIED";
        public const string Superseded = "SUPERSEDED";
        public const string Cancelled = "CANCELLED";
    }

    /// <summary>
    /// Engineer-authored, append-only disposition of one immutable generated placement proposal.
    /// The embedded proposal is the original evidence snapshot; its fingerprint is rechecked
    /// whenever this record is loaded or used to activate a resolution.
    /// </summary>
    [DataContract]
    public sealed class PlacementProposalDispositionRecord
    {
        [DataMember(Order = 1)] public string SchemaVersion { get; set; } = PlacementProposalDispositionSchema.Current;
        [DataMember(Order = 2)] public string RecordId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string ProposalId { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string Disposition { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string EngineerDecisionSource { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string OriginalProposalFingerprint { get; set; } = string.Empty;
        [DataMember(Order = 7)] public PlacementProposal OriginalProposal { get; set; } = new PlacementProposal();
        [DataMember(Order = 8, EmitDefaultValue = false)] public PlacementProposalSelectedPlacement? SelectedOrModifiedPlacement { get; set; }
        [DataMember(Order = 9)] public string ReasonOrComment { get; set; } = string.Empty;
        [DataMember(Order = 10)] public DateTime CreatedUtc { get; set; }
        [DataMember(Order = 11)] public string EngineerIdentity { get; set; } = string.Empty;
        [DataMember(Order = 12)] public string EngineerRole { get; set; } = string.Empty;
        [DataMember(Order = 13, EmitDefaultValue = false)] public string? PreviousDispositionId { get; set; }
        [DataMember(Order = 14)] public string AuditCorrelationId { get; set; } = string.Empty;
        [DataMember(Order = 15)] public string ReviewRequestId { get; set; } = string.Empty;
        [DataMember(Order = 16)] public string ReviewRequestSha256 { get; set; } = string.Empty;
        [DataMember(Order = 17)] public string ReviewResponseId { get; set; } = string.Empty;
        [DataMember(Order = 18)] public string ReviewClassification { get; set; } = string.Empty;
    }

    [DataContract]
    public sealed class PlacementProposalSelectedPlacement
    {
        [DataMember(Order = 1)] public string EquipmentEntityId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string NormalizedTag { get; set; } = string.Empty;
        [DataMember(Order = 3)] public decimal Easting { get; set; }
        [DataMember(Order = 4)] public decimal Northing { get; set; }
        [DataMember(Order = 5)] public string InsertionUnits { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string Source { get; set; } = string.Empty;
        [DataMember(Order = 7, EmitDefaultValue = false)] public string? SourceHandle { get; set; }
        [DataMember(Order = 8, EmitDefaultValue = false)] public string? SourceLayer { get; set; }
        [DataMember(Order = 9)] public double PickedPointX { get; set; }
        [DataMember(Order = 10)] public double PickedPointY { get; set; }
        [DataMember(Order = 11)] public double PickedPointZ { get; set; }
        [DataMember(Order = 12)] public string PlacementFingerprint { get; set; } = string.Empty;
    }

    /// <summary>The effective view of a proposal's immutable append-only disposition history.</summary>
    public sealed class PlacementProposalDispositionStatus
    {
        public string Disposition { get; set; } = PlacementProposalDispositions.PendingReview;
        public string? CurrentRecordId { get; set; }
        public string? PreviousDispositionId { get; set; }
        public PlacementProposalDispositionRecord? CurrentRecord { get; set; }
    }

    public sealed class PlacementProposalDispositionHistoryEntry
    {
        public string EffectiveDisposition { get; set; } = string.Empty;
        public PlacementProposalDispositionRecord Record { get; set; } = new PlacementProposalDispositionRecord();
    }
}

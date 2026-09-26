using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class EquipmentPlacementResolutionValidationException : Exception
    {
        public EquipmentPlacementResolutionValidationException(string message) : base(message) { }
    }

    /// <summary>
    /// Structural validation of an engineer placement resolution record. This checks the schema,
    /// the source-specific pick fields, the review envelope fields, and that both identities
    /// recompute from the record content. It deliberately does NOT decide trust: unit validity
    /// against the current drawing and review-envelope validity against the persisted review are
    /// decided by <see cref="EquipmentPlacementResolutionService"/>.
    /// </summary>
    public static class EquipmentPlacementResolutionValidator
    {
        public static void Validate(EquipmentPlacementResolutionRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (record.SchemaVersion != EquipmentPlacementResolutionSchema.Current) throw new EquipmentPlacementResolutionValidationException("unsupported resolution SchemaVersion: " + record.SchemaVersion);
            if (string.IsNullOrWhiteSpace(record.RecordId)) throw new EquipmentPlacementResolutionValidationException("RecordId is required");
            if (string.IsNullOrWhiteSpace(record.ResolutionId)) throw new EquipmentPlacementResolutionValidationException("ResolutionId is required");
            if (string.IsNullOrWhiteSpace(record.DrawingName)) throw new EquipmentPlacementResolutionValidationException("DrawingName is required");
            if (!IsSha256(record.DrawingSha256)) throw new EquipmentPlacementResolutionValidationException("DrawingSha256 must be a 64-character hexadecimal SHA256");
            if (string.IsNullOrWhiteSpace(record.EquipmentEntityId)) throw new EquipmentPlacementResolutionValidationException("EquipmentEntityId is required");
            if (string.IsNullOrWhiteSpace(record.NormalizedTag)) throw new EquipmentPlacementResolutionValidationException("NormalizedTag is required");
            if (string.IsNullOrWhiteSpace(record.InsertionUnits)) throw new EquipmentPlacementResolutionValidationException("InsertionUnits is required");
            if (!EquipmentPlacementResolutionSources.All.Contains(record.Source)) throw new EquipmentPlacementResolutionValidationException("unsupported resolution source: " + record.Source);
            if (string.Equals(record.Source, EquipmentPlacementResolutionSources.EntityPick, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(record.SourceHandle)) throw new EquipmentPlacementResolutionValidationException("an entity pick requires a proven source handle");
                if (string.IsNullOrWhiteSpace(record.SourceLayer)) throw new EquipmentPlacementResolutionValidationException("an entity pick requires a proven source layer");
            }
            else if (string.Equals(record.Source, EquipmentPlacementResolutionSources.FreeSpace, StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(record.SourceHandle)) throw new EquipmentPlacementResolutionValidationException("a free-space pick must not carry a source handle");
                if (!string.IsNullOrWhiteSpace(record.SourceLayer)) throw new EquipmentPlacementResolutionValidationException("a free-space pick must not carry a source layer");
            }
            else if (!string.IsNullOrWhiteSpace(record.SourceHandle) || !string.IsNullOrWhiteSpace(record.SourceLayer))
                throw new EquipmentPlacementResolutionValidationException("an approved proposal must not claim a direct entity pick");
            if (string.IsNullOrWhiteSpace(record.ReviewedCaseId)) throw new EquipmentPlacementResolutionValidationException("ReviewedCaseId is required");
            if (string.IsNullOrWhiteSpace(record.ReviewRequestId)) throw new EquipmentPlacementResolutionValidationException("ReviewRequestId is required");
            if (!IsSha256(record.RequestSha256)) throw new EquipmentPlacementResolutionValidationException("RequestSha256 must be a 64-character hexadecimal SHA256");
            if (string.IsNullOrWhiteSpace(record.ResponseId)) throw new EquipmentPlacementResolutionValidationException("ResponseId is required");
            if (string.IsNullOrWhiteSpace(record.ReviewerDisplayName)) throw new EquipmentPlacementResolutionValidationException("reviewer display name is required");
            if (string.IsNullOrWhiteSpace(record.ReviewerRole)) throw new EquipmentPlacementResolutionValidationException("reviewer role is required");
            if (record.ResponseTimestampUtc.Kind != DateTimeKind.Utc) throw new EquipmentPlacementResolutionValidationException("resolution response timestamp must be UTC");
            if (record.RecordedTimestampUtc.Kind != DateTimeKind.Utc) throw new EquipmentPlacementResolutionValidationException("resolution recorded timestamp must be UTC");
            if (!EquipmentPlacementResolutionStates.All.Contains(record.State)) throw new EquipmentPlacementResolutionValidationException("unsupported resolution state: " + record.State);
            bool hasProposalLink = !string.IsNullOrWhiteSpace(record.ProposalId) || !string.IsNullOrWhiteSpace(record.ProposalFingerprint)
                || !string.IsNullOrWhiteSpace(record.DispositionRecordId) || !string.IsNullOrWhiteSpace(record.SelectedPlacementFingerprint);
            if (hasProposalLink && (string.IsNullOrWhiteSpace(record.ProposalId) || !IsSha256(record.ProposalFingerprint)
                || !IsSha256(record.DispositionRecordId) || !IsSha256(record.SelectedPlacementFingerprint)))
                throw new EquipmentPlacementResolutionValidationException("proposal resolution link is incomplete or malformed");
            if (string.Equals(record.Source, EquipmentPlacementResolutionSources.ProposalApproval, StringComparison.Ordinal) && !hasProposalLink)
                throw new EquipmentPlacementResolutionValidationException("proposal approval requires a complete disposition link");
            if (!string.Equals(record.ResolutionId, EquipmentPlacementResolutionIdentity.ComputeResolutionId(record), StringComparison.Ordinal)) throw new EquipmentPlacementResolutionValidationException("ResolutionId does not match its content");
            if (!string.Equals(record.RecordId, EquipmentPlacementResolutionIdentity.ComputeRecordId(record), StringComparison.Ordinal)) throw new EquipmentPlacementResolutionValidationException("RecordId does not match its content");
        }

        internal static bool IsSha256(string? value)
        {
            if (value == null || value.Length != 64) return false;
            return value.All(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'));
        }
    }
}

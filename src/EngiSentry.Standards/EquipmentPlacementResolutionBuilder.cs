using System;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>
    /// Builds an engineer placement resolution record from an explicit pick and an already-persisted
    /// governed review response. Nothing is inferred: the drawing hash, equipment identity, tag,
    /// coordinates, units, source handle/layer, and review envelope are all supplied verbatim, and
    /// the request/response binding is re-validated before the record is produced.
    /// </summary>
    public static class EquipmentPlacementResolutionBuilder
    {
        public static EquipmentPlacementResolutionRecord Build(
            string drawingName,
            string drawingSha256,
            string equipmentEntityId,
            string normalizedTag,
            decimal easting,
            decimal northing,
            string insertionUnits,
            string source,
            string? sourceHandle,
            string? sourceLayer,
            double pickedPointX,
            double pickedPointY,
            double pickedPointZ,
            ProjectEngineerReviewRequest request,
            ProjectEngineerReviewResponse response,
            DateTime recordedTimestampUtc)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (response == null) throw new ArgumentNullException(nameof(response));
            if (string.IsNullOrWhiteSpace(drawingSha256) || !EquipmentPlacementResolutionValidator.IsSha256(drawingSha256)) throw new EquipmentPlacementResolutionValidationException("a current 64-character drawing SHA256 is required to bind a resolution");
            if (!string.Equals(request.DrawingSha256, drawingSha256, StringComparison.Ordinal)) throw new EquipmentPlacementResolutionValidationException("the review request is not bound to the current drawing hash");
            if (!string.Equals(request.DrawingName, drawingName, StringComparison.Ordinal)) throw new EquipmentPlacementResolutionValidationException("the review request is not bound to the current drawing name");
            if (string.IsNullOrWhiteSpace(response.ResponseId)) throw new EquipmentPlacementResolutionValidationException("the review response must be persisted before it can bind a resolution");

            // The response must be a complete, binding-valid answer to this exact request.
            ProjectEngineerReviewService.ValidateResponse(request, response);
            if (!string.Equals(response.ResponseId, ProjectEngineerReviewService.ComputeResponseId(response), StringComparison.Ordinal)) throw new EquipmentPlacementResolutionValidationException("the review response identity does not match its content");

            var record = new EquipmentPlacementResolutionRecord
            {
                SchemaVersion = EquipmentPlacementResolutionSchema.Current,
                DrawingName = drawingName,
                DrawingSha256 = drawingSha256,
                EquipmentEntityId = equipmentEntityId,
                NormalizedTag = normalizedTag,
                Easting = easting,
                Northing = northing,
                InsertionUnits = insertionUnits,
                Source = source,
                SourceHandle = string.IsNullOrWhiteSpace(sourceHandle) ? null : sourceHandle,
                SourceLayer = string.IsNullOrWhiteSpace(sourceLayer) ? null : sourceLayer,
                PickedPointX = pickedPointX,
                PickedPointY = pickedPointY,
                PickedPointZ = pickedPointZ,
                ReviewedCaseId = request.ReviewedCaseId,
                ReviewRequestId = request.ReviewRequestId,
                RequestSha256 = request.RequestSha256,
                ResponseId = response.ResponseId!,
                ReviewerDisplayName = response.ReviewerDisplayName,
                ReviewerRole = response.ReviewerRole,
                // Stored at the precision the record is persisted with, so the in-memory record is
                // byte-identical to the record that is read back from disk.
                ResponseTimestampUtc = EquipmentPlacementResolutionIdentity.NormalizeTimestamp(response.ResponseTimestampUtc),
                RecordedTimestampUtc = EquipmentPlacementResolutionIdentity.NormalizeTimestamp(recordedTimestampUtc),
                State = EquipmentPlacementResolutionStates.Active
            };
            record.ResolutionId = EquipmentPlacementResolutionIdentity.ComputeResolutionId(record);
            record.RecordId = EquipmentPlacementResolutionIdentity.ComputeRecordId(record);
            EquipmentPlacementResolutionValidator.Validate(record);
            return record;
        }
    }
}

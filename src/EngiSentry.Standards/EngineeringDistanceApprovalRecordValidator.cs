using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class EngineeringDistanceApprovalRecordValidationException : Exception { public EngineeringDistanceApprovalRecordValidationException(string message) : base(message) { } }
    public static class EngineeringDistanceApprovalRecordValidator
    {
        public const string SchemaVersion = "1.0";
        public static void Validate(EngineeringDistanceApprovalRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (record.SchemaVersion != SchemaVersion) Fail("SchemaVersion is invalid");
            if (!IsSha(record.ApprovalRecordId)) Fail("ApprovalRecordId is invalid");
            if (record.ProjectDirectory != NormalizeProjectDirectory(record.ProjectDirectory)) Fail("ProjectDirectory is invalid or not normalized");
            EngineeringDistanceCandidateBinding c = record.CandidateBinding ?? throw new EngineeringDistanceApprovalRecordValidationException("CandidateBinding is required");
            if (string.IsNullOrWhiteSpace(c.CandidateId) || string.IsNullOrWhiteSpace(c.PackId) || string.IsNullOrWhiteSpace(c.RuleId)) Fail("CandidateBinding identity is required");
            if (c.MeasurementKind != "EquipmentToRoad2D" && c.MeasurementKind != "EquipmentToBoundary2D") Fail("CandidateBinding MeasurementKind is invalid");
            if (c.ProposedMinimumDistanceMeters <= 0m || !EngineeringDistanceEvidenceKeys.IsSupportedMeasurementBasisKey(c.MeasurementKind, c.RequiredMeasurementBasisKey) || !EngineeringDistanceEvidenceKeys.IsSupportedApplicabilityKey(c.RequiredApplicabilityKey)) Fail("CandidateBinding engineering fields are invalid");
            if (!IsSha(c.CandidateDefinitionSha256)) Fail("CandidateDefinitionSha256 is invalid");
            if (c.ClauseIds == null || c.ClauseIds.Count == 0 || c.ClauseIds.Any(string.IsNullOrWhiteSpace) || c.ClauseIds.Distinct(StringComparer.Ordinal).Count() != c.ClauseIds.Count || !c.ClauseIds.SequenceEqual(c.ClauseIds.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal)) Fail("CandidateBinding ClauseIds are invalid");
            if (!string.Equals(c.CandidateDefinitionSha256, EngineeringDistanceApprovalRecordBuilder.ComputeCandidateDefinitionSha256(c), StringComparison.Ordinal)) Fail("CandidateDefinitionSha256 does not match CandidateBinding");
            EngineeringDistanceApprovalDecision decision = record.Decision ?? throw new EngineeringDistanceApprovalRecordValidationException("Decision is required");
            if (decision.CandidateId != c.CandidateId || !IsDecision(decision.DecisionStatus)) Fail("Decision is invalid");
            if (decision.DecisionStatus == "APPROVED" && string.IsNullOrWhiteSpace(decision.ApprovalReference)) Fail("APPROVED requires ApprovalReference");
            if (string.IsNullOrWhiteSpace(record.ApproverDisplayName)) Fail("ApproverDisplayName is required");
            if (record.DecisionTimestampUtc.Kind != DateTimeKind.Utc || record.DecisionTimestampUtc.Ticks % TimeSpan.TicksPerMillisecond != 0) Fail("DecisionTimestampUtc must be UTC whole milliseconds");
            if (record.SupersedesApprovalRecordId != null && (!IsSha(record.SupersedesApprovalRecordId) || record.SupersedesApprovalRecordId == record.ApprovalRecordId)) Fail("SupersedesApprovalRecordId is invalid");
            List<EngineeringDistanceApprovalEvidenceBinding> evidence = record.EvidenceBindings ?? throw new EngineeringDistanceApprovalRecordValidationException("EvidenceBindings are required");
            if (evidence.Count == 0 || evidence.Any(x => x == null || string.IsNullOrWhiteSpace(x.ClauseId) || string.IsNullOrWhiteSpace(x.SourceId) || !IsSha(x.SourceSha256))) Fail("EvidenceBindings are invalid");
            if (!evidence.Select(x => x.ClauseId).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(c.ClauseIds, StringComparer.Ordinal) || evidence.Select(x => x.ClauseId + "|" + x.SourceId + "|" + x.SourceSha256).Distinct(StringComparer.Ordinal).Count() != evidence.Count || !evidence.SequenceEqual(evidence.OrderBy(x => x.ClauseId, StringComparer.Ordinal).ThenBy(x => x.SourceId, StringComparer.Ordinal).ThenBy(x => x.SourceSha256, StringComparer.Ordinal))) Fail("EvidenceBindings do not exactly match CandidateBinding");
        }
        public static string NormalizeProjectDirectory(string value) { if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value)) Fail("ProjectDirectory must be absolute"); string full = Path.GetFullPath(value); string root = Path.GetPathRoot(full) ?? string.Empty; string trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); return (trimmed.Length == 0 ? root : trimmed).ToUpperInvariant(); }
        public static bool IsSha(string? value) => value != null && value.Length == 64 && value.All(x => (x >= '0' && x <= '9') || (x >= 'A' && x <= 'F') || (x >= 'a' && x <= 'f'));
        private static bool IsDecision(string value) => value == "PENDING" || value == "REJECTED" || value == "REFERENCE_ONLY" || value == "APPROVED";
        private static void Fail(string message) => throw new EngineeringDistanceApprovalRecordValidationException(message);
    }
}

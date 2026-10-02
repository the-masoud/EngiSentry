using System;
using System.Globalization;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace EngiSentry.Contracts
{
    public static class DrawingPlacementCandidateActions
    {
        public const string Accept = "ACCEPT";
        public const string Reject = "REJECT";
        public const string ConfirmedSource = "ENGINEER_CONFIRMED_DWG_CANDIDATE";
    }

    /// <summary>Append-only, exact-occurrence human review of one inferred drawing candidate.</summary>
    [DataContract]
    public sealed class DrawingPlacementCandidateDecision
    {
        [DataMember(Order = 1)] public string SchemaVersion { get; set; } = "EPR-CANDIDATE-1";
        [DataMember(Order = 2)] public string DecisionId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string DrawingName { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string DrawingSha256 { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string OccurrenceId { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string EquipmentTag { get; set; } = string.Empty;
        [DataMember(Order = 7)] public string TagHandle { get; set; } = string.Empty;
        [DataMember(Order = 8)] public string CandidateFingerprint { get; set; } = string.Empty;
        [DataMember(Order = 9)] public decimal CandidateX { get; set; }
        [DataMember(Order = 10)] public decimal CandidateY { get; set; }
        [DataMember(Order = 11)] public string XHandle { get; set; } = string.Empty;
        [DataMember(Order = 12)] public string YHandle { get; set; } = string.Empty;
        [DataMember(Order = 13)] public string EvidenceType { get; set; } = string.Empty;
        [DataMember(Order = 14)] public string EvidenceSummary { get; set; } = string.Empty;
        [DataMember(Order = 15)] public string InsertionUnits { get; set; } = string.Empty;
        [DataMember(Order = 16)] public string Action { get; set; } = string.Empty;
        [DataMember(Order = 17)] public string ReviewRequestId { get; set; } = string.Empty;
        [DataMember(Order = 18)] public string RequestSha256 { get; set; } = string.Empty;
        [DataMember(Order = 19)] public string ResponseId { get; set; } = string.Empty;
        [DataMember(Order = 20)] public string ReviewerDisplayName { get; set; } = string.Empty;
        [DataMember(Order = 21)] public string ReviewerRole { get; set; } = string.Empty;
        [DataMember(Order = 22)] public DateTime ResponseTimestampUtc { get; set; }
        [DataMember(Order = 23)] public string? EngineerComment { get; set; }
    }

    public static class DrawingPlacementCandidateDecisionIdentity
    {
        public static string CandidateFingerprint(EquipmentPlacement placement, DrawingPlacementCandidate candidate) => Hash(
            placement.PlacementId, placement.NormalizedTag, placement.TagSourceHandle,
            Canonical(candidate.CandidateX), Canonical(candidate.CandidateY), candidate.XEntityHandle,
            candidate.YEntityHandle, candidate.EvidenceType, candidate.EvidenceSummary,
            candidate.CoordinateBasis, candidate.CoordinatesInDrawingUnits ? "1" : "0");

        public static string DecisionId(DrawingPlacementCandidateDecision record) => Hash(
            record.DrawingSha256, record.OccurrenceId, record.EquipmentTag, record.TagHandle,
            record.CandidateFingerprint, Canonical(record.CandidateX), Canonical(record.CandidateY),
            record.XHandle, record.YHandle, record.EvidenceType, record.EvidenceSummary,
            record.InsertionUnits, record.Action, record.ReviewRequestId, record.RequestSha256,
            record.ResponseId, record.ReviewerDisplayName, record.ReviewerRole,
            record.ResponseTimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            record.EngineerComment ?? string.Empty);

        private static string Canonical(decimal value) => value.ToString("0." + new string('#', 28), CultureInfo.InvariantCulture);
        private static string Hash(params string[] fields)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", fields)))).Replace("-", string.Empty);
        }
    }
}

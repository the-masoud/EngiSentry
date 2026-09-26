using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>Deterministic content fingerprint of the complete generated proposal projection.</summary>
    public static class PlacementProposalFingerprint
    {
        public static string Compute(PlacementProposal proposal)
        {
            if (proposal == null) throw new ArgumentNullException(nameof(proposal));
            string saved = proposal.ProposalFingerprint ?? string.Empty;
            try
            {
                proposal.ProposalFingerprint = string.Empty;
                var serializer = new DataContractJsonSerializer(typeof(PlacementProposal));
                using (var stream = new MemoryStream())
                {
                    serializer.WriteObject(stream, proposal);
                    return Sha256(stream.ToArray());
                }
            }
            finally { proposal.ProposalFingerprint = saved; }
        }

        internal static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
        }

        internal static string Sha256(string value) => Sha256(Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    /// <summary>
    /// Append-only engineer dispositions for immutable placement proposals. The current state is
    /// derived from the latest valid record; a proposal with no record is PENDING_REVIEW and can
    /// never activate a placement resolution.
    /// </summary>
    public sealed class PlacementProposalDispositionService
    {
        public const string DecisionSourceEsPlacementResolve = "ES_PLACEMENTRESOLVE";
        public const string RootOverrideVariable = "ENGISENTRY_PLACEMENT_PROPOSAL_DISPOSITION_ROOT";
        public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EngiSentry", "artifacts", "qa", "placement-proposal-dispositions");

        private readonly string root;
        public PlacementProposalDispositionService() : this(ResolveRoot()) { }
        public PlacementProposalDispositionService(string root) { this.root = root ?? throw new ArgumentNullException(nameof(root)); }
        public string Root => root;
        public static string ResolveRoot()
        {
            string value = Environment.GetEnvironmentVariable(RootOverrideVariable);
            return string.IsNullOrWhiteSpace(value) ? DefaultRoot : value.Trim();
        }

        public PlacementProposalDispositionStatus GetStatus(PlacementProposal proposal)
        {
            if (proposal == null) throw new ArgumentNullException(nameof(proposal));
            ValidateProposal(proposal, proposal.ProposalFingerprint, proposal.ProposalFingerprint);
            List<PlacementProposalDispositionRecord> history = LoadAll()
                .Where(x => string.Equals(x.ProposalId, proposal.ProposalId, StringComparison.Ordinal)
                    && string.Equals(x.OriginalProposalFingerprint, proposal.ProposalFingerprint, StringComparison.Ordinal))
                .OrderBy(x => x.CreatedUtc).ThenBy(x => x.RecordId, StringComparer.Ordinal).ToList();
            PlacementProposalDispositionRecord? current = history.LastOrDefault();
            return new PlacementProposalDispositionStatus
            {
                Disposition = current?.Disposition ?? PlacementProposalDispositions.PendingReview,
                CurrentRecordId = current?.RecordId,
                PreviousDispositionId = current?.PreviousDispositionId,
                CurrentRecord = current
            };
        }

        public PlacementProposalDispositionRecord? GetCurrentRecord(string proposalId, string proposalFingerprint)
        {
            return LoadAll()
                .Where(x => string.Equals(x.ProposalId, proposalId, StringComparison.Ordinal)
                    && string.Equals(x.OriginalProposalFingerprint, proposalFingerprint, StringComparison.Ordinal))
                .OrderBy(x => x.CreatedUtc).ThenBy(x => x.RecordId, StringComparer.Ordinal).LastOrDefault();
        }

        public IReadOnlyList<PlacementProposalDispositionHistoryEntry> GetHistory(PlacementProposal proposal)
        {
            PlacementProposalDispositionRecord[] records = LoadAll().Where(x => x.ProposalId == proposal.ProposalId
                && x.OriginalProposalFingerprint == proposal.ProposalFingerprint)
                .OrderBy(x => x.CreatedUtc).ThenBy(x => x.RecordId, StringComparer.Ordinal).ToArray();
            string? currentId = records.LastOrDefault()?.RecordId;
            return records.Select(record => new PlacementProposalDispositionHistoryEntry
            {
                Record = record,
                EffectiveDisposition = record.RecordId == currentId ? record.Disposition : PlacementProposalDispositions.Superseded
            }).ToList();
        }

        public IReadOnlyList<PlacementProposalDispositionRecord> LoadAll()
        {
            var records = new List<PlacementProposalDispositionRecord>();
            if (!Directory.Exists(root)) return records;
            foreach (string path in Directory.GetFiles(root, "*.ppd.json", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
            {
                try
                {
                    PlacementProposalDispositionRecord record = Read(path);
                    Validate(record);
                    records.Add(record);
                }
                catch (Exception ex) when (ex is IOException || ex is System.Runtime.Serialization.SerializationException || ex is ArgumentException || ex is InvalidOperationException) { }
            }
            return records.OrderBy(x => x.ProposalId, StringComparer.Ordinal).ThenBy(x => x.CreatedUtc).ThenBy(x => x.RecordId, StringComparer.Ordinal).ToList();
        }

        public PlacementProposalDispositionRecord Record(
            PlacementProposal currentProposal,
            string expectedProposalFingerprint,
            string currentArtifactFingerprint,
            string disposition,
            PlacementProposalSelectedPlacement? selectedPlacement,
            string reasonOrComment,
            string engineerIdentity,
            string engineerRole,
            string? expectedPreviousDispositionId,
            ProjectEngineerReviewRequest reviewRequest,
            ProjectEngineerReviewResponse reviewResponse,
            DateTime createdUtc)
        {
            ValidateProposal(currentProposal, expectedProposalFingerprint, currentArtifactFingerprint);
            if (disposition != PlacementProposalDispositions.Approved && disposition != PlacementProposalDispositions.Rejected
                && disposition != PlacementProposalDispositions.Modified && disposition != PlacementProposalDispositions.Superseded
                && disposition != PlacementProposalDispositions.Cancelled)
                throw new InvalidOperationException("Unsupported engineer disposition: " + disposition);
            if (string.IsNullOrWhiteSpace(engineerIdentity) || string.IsNullOrWhiteSpace(engineerRole)) throw new InvalidOperationException("Engineer identity and role are required.");
            if (reviewRequest == null || reviewResponse == null) throw new InvalidOperationException("A proposal-bound engineer review request and response are required.");
            ProjectEngineerReviewService.ValidateRequest(reviewRequest);
            ProjectEngineerReviewService.ValidateResponse(reviewRequest, reviewResponse);
            if (!string.Equals(reviewRequest.DrawingSha256, currentProposal.SourceDrawingSha256, StringComparison.Ordinal)
                || !string.Equals(reviewResponse.ResponseId, ProjectEngineerReviewService.ComputeResponseId(reviewResponse), StringComparison.Ordinal))
                throw new InvalidOperationException("The engineer review response is stale or is not bound to this proposal drawing.");
            if (reviewRequest.WorkflowSourceType != ProjectEngineerReviewService.WorkflowSourcePlacementProposal
                || reviewRequest.ReviewedCaseId != "P11.1-" + currentProposal.ProposalId
                || reviewResponse.ReviewerDisplayName != engineerIdentity.Trim()
                || reviewResponse.ReviewerRole != engineerRole.Trim()
                || !ProjectEngineerReviewClassifications.All.Contains(reviewResponse.Classification ?? string.Empty))
                throw new InvalidOperationException("The review request or response identity does not match the engineer disposition.");
            if (!reviewRequest.EvidenceReferences.Any(x => x.Kind == "PlacementProposalId" && x.Value == currentProposal.ProposalId)
                || !reviewRequest.EvidenceReferences.Any(x => x.Kind == "PlacementProposalFingerprint" && x.Value == expectedProposalFingerprint)
                || !reviewRequest.EvidenceReferences.Any(x => x.Kind == "EngineerDisposition" && x.Value == disposition))
                throw new InvalidOperationException("The review request does not contain the current proposal evidence binding.");
            if (string.IsNullOrWhiteSpace(reasonOrComment)) throw new InvalidOperationException("A reason or comment is required for every proposal disposition.");
            if (createdUtc.Kind != DateTimeKind.Utc) throw new InvalidOperationException("CreatedUtc must be UTC.");

            PlacementProposalDispositionStatus current = GetStatus(currentProposal);
            if (!string.Equals(current.CurrentRecordId, expectedPreviousDispositionId, StringComparison.Ordinal))
                throw new InvalidOperationException("The proposal disposition is stale; reload its latest engineer decision before saving.");

            ValidateSelected(currentProposal, disposition, selectedPlacement);
            PlacementProposalDispositionRecord record = new PlacementProposalDispositionRecord
            {
                ProposalId = currentProposal.ProposalId,
                Disposition = disposition,
                EngineerDecisionSource = DecisionSourceEsPlacementResolve,
                OriginalProposalFingerprint = expectedProposalFingerprint,
                OriginalProposal = CloneProposal(currentProposal),
                SelectedOrModifiedPlacement = selectedPlacement,
                ReasonOrComment = reasonOrComment.Trim(),
                CreatedUtc = NormalizeUtc(createdUtc),
                EngineerIdentity = engineerIdentity.Trim(),
                EngineerRole = engineerRole.Trim(),
                PreviousDispositionId = expectedPreviousDispositionId,
                ReviewRequestId = reviewRequest.ReviewRequestId,
                ReviewRequestSha256 = reviewRequest.RequestSha256,
                ReviewResponseId = reviewResponse.ResponseId ?? string.Empty,
                ReviewClassification = reviewResponse.Classification ?? string.Empty
            };
            record.AuditCorrelationId = ComputeAuditCorrelationId(record);
            record.RecordId = ComputeRecordId(record);
            Validate(record);

            PlacementProposalDispositionRecord? replay = LoadAll().FirstOrDefault(x => string.Equals(x.AuditCorrelationId, record.AuditCorrelationId, StringComparison.Ordinal));
            if (replay != null) return replay;
            string directory = Path.Combine(root, SafeSegment(record.ProposalId));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, record.RecordId + ".ppd.json");
            var serializer = new DataContractJsonSerializer(typeof(PlacementProposalDispositionRecord));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) serializer.WriteObject(stream, record);
            return record;
        }

        public static bool Validate(PlacementProposalDispositionRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (record.SchemaVersion != PlacementProposalDispositionSchema.Current) throw new InvalidOperationException("Unsupported disposition schema.");
            if (string.IsNullOrWhiteSpace(record.ProposalId) || record.OriginalProposal == null) throw new InvalidOperationException("Proposal evidence is required.");
            if (!string.Equals(record.ProposalId, record.OriginalProposal.ProposalId, StringComparison.Ordinal)) throw new InvalidOperationException("Disposition ProposalId does not bind to its proposal.");
            string actualFingerprint = PlacementProposalFingerprint.Compute(record.OriginalProposal);
            if (!string.Equals(actualFingerprint, record.OriginalProposalFingerprint, StringComparison.Ordinal)
                || !string.Equals(record.OriginalProposal.ProposalFingerprint, record.OriginalProposalFingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException("The original proposal fingerprint does not match its content.");
            if (record.Disposition != PlacementProposalDispositions.Approved && record.Disposition != PlacementProposalDispositions.Rejected
                && record.Disposition != PlacementProposalDispositions.Modified && record.Disposition != PlacementProposalDispositions.Superseded
                && record.Disposition != PlacementProposalDispositions.Cancelled) throw new InvalidOperationException("Unsupported disposition state.");
            if (record.Disposition == PlacementProposalDispositions.Superseded && string.IsNullOrWhiteSpace(record.PreviousDispositionId)) throw new InvalidOperationException("A superseding disposition must reference its prior disposition.");
            if (record.EngineerDecisionSource != DecisionSourceEsPlacementResolve) throw new InvalidOperationException("Only ES_PLACEMENTRESOLVE may author a disposition.");
            if (record.CreatedUtc.Kind != DateTimeKind.Utc) throw new InvalidOperationException("Disposition timestamp must be UTC.");
            if (string.IsNullOrWhiteSpace(record.EngineerIdentity) || string.IsNullOrWhiteSpace(record.EngineerRole) || string.IsNullOrWhiteSpace(record.ReasonOrComment)) throw new InvalidOperationException("Engineer identity, role, and reason are required.");
            if (string.IsNullOrWhiteSpace(record.ReviewRequestId) || !EquipmentPlacementResolutionValidator.IsSha256(record.ReviewRequestSha256) || string.IsNullOrWhiteSpace(record.ReviewResponseId)) throw new InvalidOperationException("Review response binding is required.");
            if (!ProjectEngineerReviewClassifications.All.Contains(record.ReviewClassification ?? string.Empty)) throw new InvalidOperationException("Review classification is unsupported.");
            if (!string.Equals(record.ReviewRequestId, "PER-" + record.ReviewRequestSha256.Substring(0, 16), StringComparison.Ordinal)) throw new InvalidOperationException("ReviewRequestId does not match its deterministic request identity.");
            if (string.Equals(record.RecordId, record.PreviousDispositionId, StringComparison.Ordinal)) throw new InvalidOperationException("A disposition cannot supersede itself.");
            ValidateSelected(record.OriginalProposal, record.Disposition, record.SelectedOrModifiedPlacement);
            if (!string.Equals(record.AuditCorrelationId, ComputeAuditCorrelationId(record), StringComparison.Ordinal)) throw new InvalidOperationException("AuditCorrelationId does not match the disposition content.");
            if (!string.Equals(record.RecordId, ComputeRecordId(record), StringComparison.Ordinal)) throw new InvalidOperationException("Disposition RecordId does not match its content.");
            return true;
        }

        public static string ComputeSelectedPlacementFingerprint(PlacementProposalSelectedPlacement selected)
        {
            string canonical = string.Join("\n", new[] { selected.EquipmentEntityId ?? string.Empty, selected.NormalizedTag ?? string.Empty,
                EquipmentPlacementResolutionIdentity.CanonicalDecimal(selected.Easting), EquipmentPlacementResolutionIdentity.CanonicalDecimal(selected.Northing),
                selected.InsertionUnits ?? string.Empty, selected.Source ?? string.Empty, selected.SourceHandle ?? string.Empty, selected.SourceLayer ?? string.Empty,
                selected.PickedPointX.ToString("R", CultureInfo.InvariantCulture), selected.PickedPointY.ToString("R", CultureInfo.InvariantCulture), selected.PickedPointZ.ToString("R", CultureInfo.InvariantCulture) });
            return PlacementProposalFingerprint.Sha256(canonical);
        }

        public static string ComputeRecordId(PlacementProposalDispositionRecord record) => PlacementProposalFingerprint.Sha256(record.AuditCorrelationId ?? string.Empty);

        public static void BindResolutionToDisposition(EquipmentPlacementResolutionRecord resolution, PlacementProposalDispositionRecord disposition)
        {
            if (resolution == null) throw new ArgumentNullException(nameof(resolution));
            if (disposition == null) throw new ArgumentNullException(nameof(disposition));
            Validate(disposition);
            if (disposition.Disposition != PlacementProposalDispositions.Approved && disposition.Disposition != PlacementProposalDispositions.Modified)
                throw new InvalidOperationException("Only approved or modified proposals can bind an active resolution.");
            PlacementProposalSelectedPlacement selected = disposition.SelectedOrModifiedPlacement!;
            if (resolution.EquipmentEntityId != selected.EquipmentEntityId || resolution.Easting != selected.Easting || resolution.Northing != selected.Northing || resolution.InsertionUnits != selected.InsertionUnits)
                throw new InvalidOperationException("Resolution does not match the selected proposal placement.");
            resolution.ProposalId = disposition.ProposalId;
            resolution.ProposalFingerprint = disposition.OriginalProposalFingerprint;
            resolution.DispositionRecordId = disposition.RecordId;
            resolution.SelectedPlacementFingerprint = selected.PlacementFingerprint;
            resolution.RecordId = EquipmentPlacementResolutionIdentity.ComputeRecordId(resolution);
            EquipmentPlacementResolutionValidator.Validate(resolution);
        }

        private static string ComputeAuditCorrelationId(PlacementProposalDispositionRecord record)
        {
            PlacementProposalSelectedPlacement? selected = record.SelectedOrModifiedPlacement;
            return PlacementProposalFingerprint.Sha256(string.Join("\n", new[] { record.ProposalId ?? string.Empty, record.OriginalProposalFingerprint ?? string.Empty,
                record.PreviousDispositionId ?? string.Empty, record.Disposition ?? string.Empty, record.EngineerDecisionSource ?? string.Empty,
                record.EngineerIdentity ?? string.Empty, record.EngineerRole ?? string.Empty, record.ReasonOrComment ?? string.Empty,
                record.ReviewResponseId ?? string.Empty, selected?.PlacementFingerprint ?? string.Empty }));
        }

        private static void ValidateProposal(PlacementProposal proposal, string? expected, string? current)
        {
            if (proposal == null) throw new ArgumentNullException(nameof(proposal));
            if (string.IsNullOrWhiteSpace(proposal.ProposalId) || string.IsNullOrWhiteSpace(proposal.PlacementId)
                || string.IsNullOrWhiteSpace(proposal.EquipmentEntityId) || string.IsNullOrWhiteSpace(proposal.NormalizedTag)
                || string.IsNullOrWhiteSpace(proposal.SourceDrawingSha256) || !EquipmentPlacementResolutionValidator.IsSha256(proposal.SourceDrawingSha256)
                || string.IsNullOrWhiteSpace(proposal.InsertionUnits) || string.IsNullOrWhiteSpace(proposal.GeneratorIdentity))
                throw new InvalidOperationException("The proposal lacks required identity, drawing, units, or generator provenance.");
            string actual = PlacementProposalFingerprint.Compute(proposal);
            if (!string.Equals(actual, proposal.ProposalFingerprint, StringComparison.Ordinal)
                || !string.Equals(actual, expected, StringComparison.Ordinal)
                || !string.Equals(actual, current, StringComparison.Ordinal))
                throw new InvalidOperationException("The proposal is stale or its fingerprint does not match the current generated evidence.");
        }

        private static void ValidateSelected(PlacementProposal proposal, string disposition, PlacementProposalSelectedPlacement? selected)
        {
            bool activates = disposition == PlacementProposalDispositions.Approved || disposition == PlacementProposalDispositions.Modified;
            if (!activates)
            {
                if (selected != null) throw new InvalidOperationException("Rejected, cancelled, or superseded dispositions cannot carry an active placement.");
                return;
            }
            if (selected == null) throw new InvalidOperationException("Approved and modified dispositions require a selected placement.");
            if (selected.EquipmentEntityId != proposal.EquipmentEntityId || selected.NormalizedTag != proposal.NormalizedTag || selected.InsertionUnits != proposal.InsertionUnits)
                throw new InvalidOperationException("Selected placement identity or units do not match the proposal.");
            if (selected.Source == EquipmentPlacementResolutionSources.ProposalApproval)
            {
                decimal? proposedEasting = ToStoredDecimal(proposal.ProposedEasting);
                decimal? proposedNorthing = ToStoredDecimal(proposal.ProposedNorthing);
                if (disposition != PlacementProposalDispositions.Approved
                    || !proposedEasting.HasValue || !proposedNorthing.HasValue
                    || selected.Easting != proposedEasting.Value || selected.Northing != proposedNorthing.Value)
                    throw new InvalidOperationException("Approved placement must be the original proposal coordinates.");
            }
            else if (selected.Source != EquipmentPlacementResolutionSources.EntityPick && selected.Source != EquipmentPlacementResolutionSources.FreeSpace)
                throw new InvalidOperationException("Unsupported engineer placement source.");
            decimal? originalEasting = ToStoredDecimal(proposal.ProposedEasting);
            decimal? originalNorthing = ToStoredDecimal(proposal.ProposedNorthing);
            if (disposition == PlacementProposalDispositions.Modified && originalEasting.HasValue && originalNorthing.HasValue
                && selected.Easting == originalEasting.Value && selected.Northing == originalNorthing.Value)
                throw new InvalidOperationException("A modified placement must differ from the original proposed point.");
            if (string.IsNullOrWhiteSpace(selected.InsertionUnits) || !string.Equals(selected.PlacementFingerprint, ComputeSelectedPlacementFingerprint(selected), StringComparison.Ordinal))
                throw new InvalidOperationException("Selected placement fingerprint does not match its content.");
        }

        private static PlacementProposal CloneProposal(PlacementProposal proposal)
        {
            var serializer = new DataContractJsonSerializer(typeof(PlacementProposal));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, proposal);
                stream.Position = 0;
                return (PlacementProposal)serializer.ReadObject(stream)!;
            }
        }

        private static PlacementProposalDispositionRecord Read(string path)
        {
            var serializer = new DataContractJsonSerializer(typeof(PlacementProposalDispositionRecord));
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) return (PlacementProposalDispositionRecord)serializer.ReadObject(stream)!;
        }

        private static DateTime NormalizeUtc(DateTime timestamp) => new DateTime(timestamp.ToUniversalTime().Ticks - timestamp.ToUniversalTime().Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
        private static string SafeSegment(string value) => PlacementProposalFingerprint.Sha256(value ?? string.Empty);
        private static decimal? ToStoredDecimal(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return null;
            try { return Convert.ToDecimal(value, CultureInfo.InvariantCulture); }
            catch (OverflowException) { return null; }
        }
    }
}

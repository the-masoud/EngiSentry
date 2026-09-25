using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    /// <summary>
    /// Loads and resolves engineer placement confirmations. A resolution is trusted only when it is
    /// ACTIVE, hash-valid (both identities recompute), unit-valid (its insertion units equal the
    /// current drawing's insertion units), and bound to a persisted, still-valid governed review.
    /// Anything else leaves the equipment unresolved — never Complete.
    ///
    /// This records human engineering observation evidence only. It never creates an approval
    /// record, an authority policy, a trusted-issuer entry, a private key, or an activated rule.
    /// </summary>
    public sealed class EquipmentPlacementResolutionService
    {
        public const string RootOverrideVariable = "ENGISENTRY_EQUIPMENT_PLACEMENT_RESOLUTION_ROOT";

        public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EngiSentry", "artifacts", "qa", "equipment-placement-resolution");

        public static string? ResolveRootOverride()
        {
            string? value = Environment.GetEnvironmentVariable(RootOverrideVariable);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        public static string ResolveRoot() => ResolveRootOverride() ?? DefaultRoot;

        private readonly string root;
        private readonly ProjectEngineerReviewService reviewService;

        public EquipmentPlacementResolutionService() : this(ResolveRoot(), new ProjectEngineerReviewService()) { }
        public EquipmentPlacementResolutionService(string root) : this(root, new ProjectEngineerReviewService()) { }
        public EquipmentPlacementResolutionService(string root, ProjectEngineerReviewService reviewService)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            this.reviewService = reviewService ?? throw new ArgumentNullException(nameof(reviewService));
        }

        public string Root => root;

        /// <summary>Appends a validated confirmation. An existing confirmation is never overwritten.</summary>
        public string Save(EquipmentPlacementResolutionRecord record) => EquipmentPlacementResolutionFileStore.Write(record, root);

        /// <summary>
        /// Every structurally valid persisted record, deterministically ordered. A record whose
        /// identity does not recompute (or that is otherwise malformed) is skipped, never trusted.
        /// </summary>
        public IReadOnlyList<EquipmentPlacementResolutionRecord> LoadAll()
        {
            var records = new List<EquipmentPlacementResolutionRecord>();
            foreach (string path in EquipmentPlacementResolutionFileStore.EnumerateRecordPaths(root))
            {
                try { records.Add(EquipmentPlacementResolutionFileStore.Read(path)); }
                catch (EquipmentPlacementResolutionValidationException) { }
                catch (IOException) { }
                catch (System.Runtime.Serialization.SerializationException) { }
            }
            return records.OrderBy(x => x.ResolutionId, StringComparer.Ordinal).ThenBy(x => x.RecordId, StringComparer.Ordinal).ToList();
        }

        /// <summary>True only when the record still binds to a persisted, still-valid governed review.</summary>
        public bool IsReviewBindingValid(EquipmentPlacementResolutionRecord record)
        {
            if (record == null) return false;
            try
            {
                ProjectEngineerReviewRequest? request = reviewService.LoadRequest(record.ReviewRequestId);
                if (request == null) return false;
                if (!string.Equals(request.RequestSha256, record.RequestSha256, StringComparison.Ordinal)) return false;
                if (!string.Equals(request.DrawingSha256, record.DrawingSha256, StringComparison.Ordinal)) return false;
                if (!string.Equals(request.ReviewedCaseId, record.ReviewedCaseId, StringComparison.Ordinal)) return false;
                ProjectEngineerReviewResponse? response = reviewService.LoadResponse(record.ReviewRequestId);
                if (response == null) return false;
                if (!string.Equals(response.ResponseId, record.ResponseId, StringComparison.Ordinal)) return false;
                if (!string.Equals(response.RequestSha256, record.RequestSha256, StringComparison.Ordinal)) return false;
                if (!string.Equals(response.ReviewerDisplayName, record.ReviewerDisplayName, StringComparison.Ordinal)) return false;
                return response.ResponseTimestampUtc.ToUniversalTime().Ticks == record.ResponseTimestampUtc.ToUniversalTime().Ticks;
            }
            catch (Exception)
            {
                // A malformed, stale, or tampered review is not a valid review: fail closed.
                return false;
            }
        }

        /// <summary>
        /// The trusted resolution for one equipment identity in the current drawing, or null when
        /// none is ACTIVE, hash-valid, unit-valid, and review-valid. The most recent confirmation wins.
        /// </summary>
        public EquipmentPlacementResolution? ResolveTrusted(string drawingSha256, string equipmentEntityId, string insertionUnits)
        {
            if (string.IsNullOrWhiteSpace(drawingSha256) || !EquipmentPlacementResolutionValidator.IsSha256(drawingSha256)) return null;
            if (string.IsNullOrWhiteSpace(equipmentEntityId)) return null;
            if (string.IsNullOrWhiteSpace(insertionUnits)) return null;
            EquipmentPlacementResolutionRecord? best = LoadAll()
                .Where(x => string.Equals(x.State, EquipmentPlacementResolutionStates.Active, StringComparison.Ordinal)
                    && string.Equals(x.DrawingSha256, drawingSha256, StringComparison.Ordinal)
                    && string.Equals(x.EquipmentEntityId, equipmentEntityId, StringComparison.Ordinal)
                    && string.Equals(x.InsertionUnits, insertionUnits, StringComparison.Ordinal))
                .Where(IsReviewBindingValid)
                .OrderByDescending(x => x.ResponseTimestampUtc)
                .ThenByDescending(x => x.RecordId, StringComparer.Ordinal)
                .FirstOrDefault();
            return best == null ? null : Project(best);
        }

        /// <summary>Every trusted resolution for the current drawing and units, deterministically ordered.</summary>
        public IReadOnlyList<EquipmentPlacementResolution> ResolveTrustedForDrawing(string drawingSha256, string insertionUnits)
        {
            if (string.IsNullOrWhiteSpace(drawingSha256) || !EquipmentPlacementResolutionValidator.IsSha256(drawingSha256)) return Array.Empty<EquipmentPlacementResolution>();
            if (string.IsNullOrWhiteSpace(insertionUnits)) return Array.Empty<EquipmentPlacementResolution>();
            return LoadAll()
                .Where(x => string.Equals(x.State, EquipmentPlacementResolutionStates.Active, StringComparison.Ordinal)
                    && string.Equals(x.DrawingSha256, drawingSha256, StringComparison.Ordinal)
                    && string.Equals(x.InsertionUnits, insertionUnits, StringComparison.Ordinal))
                .Where(IsReviewBindingValid)
                .GroupBy(x => x.EquipmentEntityId, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(x => x.ResponseTimestampUtc).ThenByDescending(x => x.RecordId, StringComparer.Ordinal).First())
                .OrderBy(x => x.EquipmentEntityId, StringComparer.Ordinal)
                .Select(Project)
                .ToList();
        }

        private static EquipmentPlacementResolution Project(EquipmentPlacementResolutionRecord record) => new EquipmentPlacementResolution
        {
            ResolutionId = record.ResolutionId,
            RecordId = record.RecordId,
            EquipmentEntityId = record.EquipmentEntityId,
            NormalizedTag = record.NormalizedTag,
            Easting = record.Easting,
            Northing = record.Northing,
            InsertionUnits = record.InsertionUnits,
            Source = record.Source,
            SourceHandle = record.SourceHandle,
            SourceLayer = record.SourceLayer,
            ReviewerDisplayName = record.ReviewerDisplayName,
            ReviewerRole = record.ReviewerRole,
            ResponseTimestampUtc = record.ResponseTimestampUtc
        };
    }
}

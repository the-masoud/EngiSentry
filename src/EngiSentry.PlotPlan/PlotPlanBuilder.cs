using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.PlotPlan
{
    public static class PlotPlanBuilder
    {
        public const double MaxAssociationDistanceFactor = 30.0;
        public const double AmbiguityMarginFactor = 2.0;

        public static PlotPlanModel Build(EngineeringEntityIndex index, EngineeringDrawingModel model, DrawingSnapshot snapshot, PlotGeometryCapture? geometryCapture = null, IReadOnlyList<EquipmentPlacementResolution>? engineerResolutions = null, IReadOnlyList<AuthoritativeEquipmentCoordinate>? authoritativeCoordinates = null, IReadOnlyList<DrawingPlacementCandidateDecision>? candidateDecisions = null)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            List<TextSnapshot> allText = snapshot.TextEntities.Concat(snapshot.InstantiatedTextEntities ?? new List<TextSnapshot>()).ToList();
            var textByHandle = allText.Where(x => !string.IsNullOrEmpty(x.Handle)).GroupBy(x => x.Handle!, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);
            var sources = new List<Tuple<EquipmentEntity, EquipmentOccurrence, TextSnapshot>>();
            foreach (EquipmentEntity entity in index.EquipmentEntities.OrderBy(x => x.CanonicalId, StringComparer.Ordinal))
            foreach (EquipmentOccurrence occurrence in entity.Occurrences.Where(x => string.Equals(x.Role, "DrawingTag", StringComparison.Ordinal) && !string.IsNullOrEmpty(x.SourceHandle)).OrderBy(x => x.SourceHandle, StringComparer.Ordinal))
            {
                List<TextSnapshot> matches;
                if (!textByHandle.TryGetValue(occurrence.SourceHandle!, out matches) || matches.Count != 1) throw new InvalidOperationException("DrawingTag source handle must map to exactly one text record: " + occurrence.SourceHandle);
                sources.Add(Tuple.Create(entity, occurrence, matches[0]));
            }

            List<double> positiveHeights = sources.Select(x => x.Item3.Height).Where(IsPositiveFinite).Distinct().OrderBy(x => x).ToList();
            if (positiveHeights.Count == 0) positiveHeights = allText.Select(x => x.Height).Where(IsPositiveFinite).Distinct().OrderBy(x => x).ToList();
            double fallback = Median(positiveHeights);
            var coordinates = model.Coordinates.Where(x => string.Equals(x.ParseStatus, "Parsed", StringComparison.Ordinal) && (x.Axis == "E" || x.Axis == "N")).ToList();
            var placements = new List<EquipmentPlacement>();
            foreach (var source in sources)
            {
                TextSnapshot text = source.Item3;
                double scale = IsPositiveFinite(text.Height) ? text.Height : fallback;
                string handle = source.Item2.SourceHandle!;
                List<CoordinateEvidence> compatibleCoordinates = coordinates.Where(c => IsCoordinateCompatible(source.Item2.SourceLayer ?? text.Layer, handle, c, textByHandle)).ToList();
                List<CoordinateCandidate> eastingCandidates = FindCandidates("E", text.PositionX, text.PositionY, scale, compatibleCoordinates, handle);
                List<CoordinateCandidate> northingCandidates = FindCandidates("N", text.PositionX, text.PositionY, scale, compatibleCoordinates, handle);
                CoordinateAssociation easting = Associate("E", eastingCandidates, scale);
                CoordinateAssociation northing = Associate("N", northingCandidates, scale);
                PreferCoherentPair(easting, northing, eastingCandidates, northingCandidates, scale);
                var placement = new EquipmentPlacement { PlacementId = "placement:" + source.Item1.CanonicalId + ":" + handle, EquipmentEntityId = source.Item1.CanonicalId, NormalizedTag = source.Item2.NormalizedTag ?? source.Item1.NormalizedTag, TagSourceHandle = handle, TagSourceLayer = source.Item2.SourceLayer ?? text.Layer, TagPositionX = text.PositionX, TagPositionY = text.PositionY, TagPositionZ = text.PositionZ, TagTextHeight = text.Height, TagRotation = text.Rotation, AssociationScale = scale, Easting = easting, Northing = northing, EngineeringApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment };
                placement.PlacementStatus = placement.Easting.Status == "Ambiguous" || placement.Northing.Status == "Ambiguous" ? "Ambiguous" : placement.Easting.Status == "Resolved" && placement.Northing.Status == "Resolved" ? "Complete" : "Incomplete";
                ApplyCenterlineEvidence(placement, text, geometryCapture?.Primitives, model.Drawing);
                placements.Add(placement);
            }
            HashSet<string> unresolvedCenterlineEntities = new HashSet<string>(placements.Where(x => x.CenterlineEvidence?.Status == "Ambiguous" || x.CenterlineEvidence?.Status == "Conflict").Select(x => x.EquipmentEntityId), StringComparer.Ordinal);
            foreach (EquipmentPlacement placement in placements.Where(x => unresolvedCenterlineEntities.Contains(x.EquipmentEntityId))) placement.PlacementStatus = "Ambiguous";
            ApplyEngineerResolutions(placements, engineerResolutions, model.Drawing);
            ApplyAuthoritativeCoordinates(placements, authoritativeCoordinates);
            DrawingPlacementCandidateResolver.Apply(placements, allText, model.Drawing);
            ApplyCandidateDecisions(placements, candidateDecisions, model.Drawing);
            placements = placements.OrderBy(x => x.PlacementId, StringComparer.Ordinal).ToList();
            PlotGeometryModel geometry = geometryCapture == null ? new PlotGeometryModel() : PlotGeometryBuilder.Build(model, geometryCapture);
            if (geometryCapture != null)
            {
                geometry.SpatialConstraints = PlotSpatialBuilder.Build(model, geometryCapture);
                List<SpatialCoordinateConstraint> constraints = geometry.SpatialConstraints;
                geometry.Statistics.SpatialConstraintCount = constraints.Count;
                geometry.Statistics.RoadSpatialConstraintCount = constraints.Count(x => x.SubjectKind == "Road");
                geometry.Statistics.BoundarySpatialConstraintCount = constraints.Count(x => x.SubjectKind == "Boundary");
                geometry.Statistics.ResolvedSpatialConstraintCount = constraints.Count(x => x.Status == "Resolved");
                geometry.Statistics.MissingSpatialConstraintCount = constraints.Count(x => x.Status == "Missing");
                geometry.Statistics.AmbiguousSpatialConstraintCount = constraints.Count(x => x.Status == "Ambiguous");
            }
            geometry.EngineeringSpatialSubjects = EngineeringSpatialSubjectBuilder.Build(model, geometry).ToList();
            PlotSpatialMeasurementModel measurements = PlotSpatialMeasurementBuilder.Build(placements, geometry);
            // Provisional evidence is stored separately. Governed rule evaluators consume only
            // EquipmentSpatialMeasurements, so an inferred point cannot produce PASS or FAIL.
            var provisionalPlacements = placements.Where(x => x.ResolutionState == PlacementResolutionState.ReviewRequired
                    && x.DrawingCandidate != null && x.DrawingCandidate.CoordinatesInDrawingUnits)
                .Select(x => new EquipmentPlacement
                {
                    PlacementId = x.PlacementId, EquipmentEntityId = x.EquipmentEntityId,
                    NormalizedTag = x.NormalizedTag, EngineeringApplicabilityKey = x.EngineeringApplicabilityKey,
                    PlacementStatus = "Complete",
                    Easting = new CoordinateAssociation { Axis = "E", Status = "Resolved", SelectedValue = x.DrawingCandidate!.CandidateX },
                    Northing = new CoordinateAssociation { Axis = "N", Status = "Resolved", SelectedValue = x.DrawingCandidate!.CandidateY }
                }).ToList();
            if (provisionalPlacements.Count > 0)
                measurements.ProvisionalSpatialMeasurements = PlotSpatialMeasurementBuilder.Build(provisionalPlacements, geometry)
                    .EquipmentSpatialMeasurements;
            return new PlotPlanModel { Drawing = model.Drawing, EquipmentPlacements = placements, Geometry = geometry, Measurements = measurements, Statistics = new PlotPlanStatistics { EquipmentPlacementCount = placements.Count, CompletePlacementCount = placements.Count(x => x.PlacementStatus == "Complete"), IncompletePlacementCount = placements.Count(x => x.PlacementStatus == "Incomplete"), AmbiguousPlacementCount = placements.Count(x => x.PlacementStatus == "Ambiguous"), ResolvedEastingCount = placements.Count(x => x.Easting.Status == "Resolved"), ResolvedNorthingCount = placements.Count(x => x.Northing.Status == "Resolved") } };
        }

        private static void ApplyCandidateDecisions(List<EquipmentPlacement> placements,
            IReadOnlyList<DrawingPlacementCandidateDecision>? decisions, SemanticDrawing? drawing)
        {
            if (decisions == null) return;
            foreach (DrawingPlacementCandidateDecision decision in decisions)
            {
                EquipmentPlacement? placement = placements.SingleOrDefault(x => x.PlacementId == decision.OccurrenceId);
                if (placement == null || placement.ResolutionState == PlacementResolutionState.Resolved) continue;
                DrawingPlacementCandidate? candidate = placement.DrawingCandidate;
                if (candidate == null || placement.ResolutionState != PlacementResolutionState.ReviewRequired ||
                    !string.Equals(drawing?.InsertionUnits, decision.InsertionUnits, StringComparison.Ordinal) ||
                    placement.NormalizedTag != decision.EquipmentTag || placement.TagSourceHandle != decision.TagHandle ||
                    candidate.CandidateX != decision.CandidateX || candidate.CandidateY != decision.CandidateY ||
                    candidate.XEntityHandle != decision.XHandle || candidate.YEntityHandle != decision.YHandle ||
                    candidate.EvidenceType != decision.EvidenceType || candidate.EvidenceSummary != decision.EvidenceSummary ||
                    DrawingPlacementCandidateDecisionIdentity.CandidateFingerprint(placement, candidate) != decision.CandidateFingerprint)
                {
                    placement.ResolutionReason = "Stored candidate decision is stale for this drawing occurrence";
                    placement.DrawingDiagnostic.RejectionReason = placement.ResolutionReason;
                    continue;
                }
                placement.CandidateDecision = decision;
                if (decision.Action == DrawingPlacementCandidateActions.Reject)
                {
                    placement.ResolutionState = PlacementResolutionState.Unresolved;
                    placement.ResolutionReason = "Engineer rejected this exact drawing candidate";
                    placement.DrawingDiagnostic.RejectionReason = placement.ResolutionReason;
                    continue;
                }
                if (decision.Action != DrawingPlacementCandidateActions.Accept || !candidate.CoordinatesInDrawingUnits) continue;
                placement.Easting = new CoordinateAssociation { Axis = "E", Status = "Resolved", SelectedValue = candidate.CandidateX,
                    SelectedSourceHandle = candidate.XEntityHandle, SelectedSourceLayer = "EQ-ID",
                    SelectedRawText = DrawingPlacementCandidateActions.ConfirmedSource };
                placement.Northing = new CoordinateAssociation { Axis = "N", Status = "Resolved", SelectedValue = candidate.CandidateY,
                    SelectedSourceHandle = candidate.YEntityHandle, SelectedSourceLayer = "EQ-ID",
                    SelectedRawText = DrawingPlacementCandidateActions.ConfirmedSource };
                placement.PlacementStatus = "Complete";
                placement.ResolutionState = PlacementResolutionState.Resolved;
                placement.ResolutionReason = "Engineer confirmed this exact DWG candidate; source remains inferred annotation evidence";
            }
        }

        // An explicit engineer pick is the only way an otherwise-unresolved placement becomes
        // Complete. Only trusted resolutions reach this method: the caller (the resolution service)
        // has already verified ACTIVE state, both identities, the drawing-hash binding, the review
        // envelope, and the unit binding. Unit validity is re-checked here against the drawing so a
        // resolution can never complete a placement in a drawing whose units differ. Deterministic
        // evidence is never overwritten: only unresolved placements are resolved.
        private static void ApplyEngineerResolutions(List<EquipmentPlacement> placements, IReadOnlyList<EquipmentPlacementResolution>? resolutions, SemanticDrawing? drawing)
        {
            if (resolutions == null || resolutions.Count == 0) return;
            var trusted = new Dictionary<string, EquipmentPlacementResolution>(StringComparer.Ordinal);
            var trustedOccurrences = new Dictionary<string, EquipmentPlacementResolution>(StringComparer.Ordinal);
            foreach (EquipmentPlacementResolution resolution in resolutions)
            {
                if (resolution == null || string.IsNullOrWhiteSpace(resolution.EquipmentEntityId)) continue;
                if (!IsResolutionUnitValid(resolution, drawing)) continue;
                if (string.Equals(resolution.Source, EquipmentPlacementResolutionSources.EngineerSelectedDwgPoint, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(resolution.OccurrenceId))
                {
                    if (!trustedOccurrences.TryGetValue(resolution.OccurrenceId, out EquipmentPlacementResolution? existingOccurrence)
                        || resolution.ResponseTimestampUtc >= existingOccurrence.ResponseTimestampUtc)
                        trustedOccurrences[resolution.OccurrenceId] = resolution;
                    continue;
                }
                if (!trusted.TryGetValue(resolution.EquipmentEntityId, out EquipmentPlacementResolution? existing) || resolution.ResponseTimestampUtc >= existing.ResponseTimestampUtc) trusted[resolution.EquipmentEntityId] = resolution;
            }
            if (trusted.Count == 0 && trustedOccurrences.Count == 0) return;
            foreach (EquipmentPlacement placement in placements)
            {
                if (placement.PlacementStatus == "Complete") continue;
                if (trustedOccurrences.TryGetValue(placement.PlacementId, out EquipmentPlacementResolution? occurrenceResolution)
                    && string.Equals(placement.TagSourceHandle, occurrenceResolution.OccurrenceSourceHandle, StringComparison.Ordinal))
                {
                    placement.Easting = EngineerAssociation("E", occurrenceResolution);
                    placement.Northing = EngineerAssociation("N", occurrenceResolution);
                    placement.PlacementStatus = "Complete";
                    placement.ResolutionState = PlacementResolutionState.Resolved;
                    placement.ResolutionReason = "Engineer confirmed this exact drawing occurrence point";
                    placement.EngineerResolution = EngineerResolution(occurrenceResolution);
                    continue;
                }
                // EPR-1 is keyed by canonical equipment identity, not occurrence. It cannot
                // safely assign one reviewed point to multiple drawing occurrences.
                if (placements.Count(x => x.EquipmentEntityId == placement.EquipmentEntityId) != 1) continue;
                if (!trusted.TryGetValue(placement.EquipmentEntityId, out EquipmentPlacementResolution? resolution)) continue;
                placement.Easting = EngineerAssociation("E", resolution);
                placement.Northing = EngineerAssociation("N", resolution);
                placement.PlacementStatus = "Complete";
                placement.EngineerResolution = EngineerResolution(resolution);
            }
        }

        private static EquipmentPlacementEngineerResolution EngineerResolution(EquipmentPlacementResolution resolution) => new EquipmentPlacementEngineerResolution
        {
            ResolutionId = resolution.ResolutionId, RecordId = resolution.RecordId, Source = resolution.Source,
            SourceHandle = resolution.SourceHandle, SourceLayer = resolution.SourceLayer,
            ReviewerDisplayName = resolution.ReviewerDisplayName, ReviewerRole = resolution.ReviewerRole,
            ResponseTimestampUtc = resolution.ResponseTimestampUtc, ProposalId = resolution.ProposalId,
            ProposalFingerprint = resolution.ProposalFingerprint, DispositionRecordId = resolution.DispositionRecordId,
            OccurrenceId = resolution.OccurrenceId, OccurrenceSourceHandle = resolution.OccurrenceSourceHandle
        };

        // An authoritative project coordinate package is the project's own statement of where each
        // equipment occurrence sits. It is applied only after every deterministic and engineer-derived
        // source, and it never overwrites trusted evidence:
        //   * when a placement has no selected value for an axis, the authoritative value resolves it;
        //   * when every already-selected value agrees EXACTLY (no tolerance is invented), the existing
        //     evidence is kept and the authoritative coordinate is recorded as corroboration;
        //   * when any already-selected value disagrees, or a centerline conflict already exists, the
        //     placement is marked Ambiguous and the authoritative evidence is recorded as
        //     COORDINATE_EVIDENCE_CONFLICT. Nothing is silently replaced or deleted.
        private static void ApplyAuthoritativeCoordinates(List<EquipmentPlacement> placements, IReadOnlyList<AuthoritativeEquipmentCoordinate>? authoritativeCoordinates)
        {
            if (authoritativeCoordinates == null || authoritativeCoordinates.Count == 0) return;
            var byOccurrence = new Dictionary<string, AuthoritativeEquipmentCoordinate>(StringComparer.Ordinal);
            foreach (AuthoritativeEquipmentCoordinate coordinate in authoritativeCoordinates)
            {
                if (coordinate == null || string.IsNullOrWhiteSpace(coordinate.OccurrenceId)) continue;
                byOccurrence[coordinate.OccurrenceId.Trim()] = coordinate;
            }
            if (byOccurrence.Count == 0) return;

            foreach (EquipmentPlacement placement in placements)
            {
                // Both the occurrence identity and the equipment tag must match. A tag alone is never enough.
                if (!byOccurrence.TryGetValue(placement.PlacementId, out AuthoritativeEquipmentCoordinate? coordinate)) continue;
                if (!string.Equals(NormalizeTag(coordinate.EquipmentTag), NormalizeTag(placement.NormalizedTag), StringComparison.Ordinal)) continue;

                bool eastingSelected = placement.Easting.SelectedValue.HasValue;
                bool northingSelected = placement.Northing.SelectedValue.HasValue;
                bool eastingAgrees = !eastingSelected || placement.Easting.SelectedValue!.Value == coordinate.Easting;
                bool northingAgrees = !northingSelected || placement.Northing.SelectedValue!.Value == coordinate.Northing;
                bool centerlineConflict = string.Equals(placement.CenterlineEvidence?.Status, "Conflict", StringComparison.Ordinal);

                if (eastingAgrees && northingAgrees && !centerlineConflict)
                {
                    if (!eastingSelected) placement.Easting = AuthoritativeAssociation("E", coordinate);
                    if (!northingSelected) placement.Northing = AuthoritativeAssociation("N", coordinate);
                    placement.PlacementStatus = "Complete";
                    placement.AuthoritativeCoordinate = AuthoritativeEvidence(coordinate, AuthoritativePlacementStatuses.Resolved, null);
                }
                else
                {
                    string conflicting = !eastingAgrees ? SourceOf(placement.Easting)
                        : !northingAgrees ? SourceOf(placement.Northing)
                        : "existing centerline conflict";
                    placement.PlacementStatus = "Ambiguous";
                    placement.AuthoritativeCoordinate = AuthoritativeEvidence(coordinate, AuthoritativePlacementStatuses.Conflict, conflicting);
                }
            }
        }

        private static string NormalizeTag(string? tag) => (tag ?? string.Empty).Trim().ToUpperInvariant();

        private static string SourceOf(CoordinateAssociation association)
            => association.SelectedSourceLayer ?? association.SelectedSourceHandle ?? "existing deterministic evidence";

        private static CoordinateAssociation AuthoritativeAssociation(string axis, AuthoritativeEquipmentCoordinate coordinate) => new CoordinateAssociation
        {
            Axis = axis,
            Status = "Resolved",
            SelectedSourceHandle = null,
            SelectedValue = axis == "E" ? coordinate.Easting : coordinate.Northing,
            SelectedRawText = "Authoritative project schedule",
            SelectedSourceLayer = null,
            SelectedDistance = null,
            Candidates = new List<CoordinateCandidate>()
        };

        private static AuthoritativeCoordinatePlacementEvidence AuthoritativeEvidence(AuthoritativeEquipmentCoordinate coordinate, string status, string? conflictSource) => new AuthoritativeCoordinatePlacementEvidence
        {
            PlacementStatus = status,
            CoordinateSourceType = coordinate.CoordinateSourceType,
            SourceUnits = coordinate.SourceUnits,
            SourceCoordinateBasis = coordinate.SourceCoordinateBasis,
            TransformType = coordinate.TransformType,
            OccurrenceId = coordinate.OccurrenceId,
            EquipmentTag = coordinate.EquipmentTag,
            Easting = coordinate.Easting,
            Northing = coordinate.Northing,
            Elevation = coordinate.Elevation,
            PackageFingerprint = coordinate.PackageFingerprint,
            ManifestSha256 = coordinate.ManifestSha256,
            ScheduleSha256 = coordinate.ScheduleSha256,
            IssuedBy = coordinate.IssuedBy,
            SourceReference = coordinate.SourceReference,
            EvidenceSummary = coordinate.EvidenceSummary,
            ConflictWithEvidenceSource = conflictSource,
            SourceWorkbookSha256 = coordinate.SourceWorkbookSha256,
            SourceDocumentNumber = coordinate.SourceDocumentNumber,
            SourceRevision = coordinate.SourceRevision,
            SourceIssueStatus = coordinate.SourceIssueStatus,
            IssueDate = coordinate.IssueDate,
            SourceX = coordinate.Easting,
            SourceY = coordinate.Northing,
            SourceZ = coordinate.Elevation,
            // P14.1 performs no coordinate transform, so the placed WCS values are the source values.
            FinalWcsX = coordinate.Easting,
            FinalWcsY = coordinate.Northing,
            FinalWcsZ = coordinate.Elevation
        };

        private static bool IsResolutionUnitValid(EquipmentPlacementResolution resolution, SemanticDrawing? drawing)
        {
            if (drawing == null || string.IsNullOrWhiteSpace(drawing.InsertionUnits)) return false;
            if (!string.Equals(resolution.InsertionUnits, drawing.InsertionUnits, StringComparison.Ordinal)) return false;
            if (!IsFinite((double)resolution.Easting) || !IsFinite((double)resolution.Northing)) return false;
            try { DrawingUnitResolver.Resolve(drawing.InsertionUnits!); return true; }
            catch (DrawingUnitResolutionException) { return false; }
        }

        private static CoordinateAssociation EngineerAssociation(string axis, EquipmentPlacementResolution resolution)
        {
            bool entityPick = string.Equals(resolution.Source, EquipmentPlacementResolutionSources.EntityPick, StringComparison.Ordinal);
            return new CoordinateAssociation
            {
                Axis = axis,
                Status = "Resolved",
                SelectedSourceHandle = entityPick ? resolution.SourceHandle : null,
                SelectedValue = axis == "E" ? resolution.Easting : resolution.Northing,
                SelectedRawText = entityPick ? "Engineer-confirmed entity pick" : "Engineer-confirmed free-space point",
                SelectedSourceLayer = entityPick ? resolution.SourceLayer : null,
                SelectedDistance = null,
                Candidates = new List<CoordinateCandidate>()
            };
        }

        private static List<CoordinateCandidate> FindCandidates(string axis, double x, double y, double scale, IEnumerable<CoordinateEvidence> all, string equipmentHandle)
        {
            double max = scale * MaxAssociationDistanceFactor;
            return all.Where(c => c.Axis == axis && !string.IsNullOrEmpty(c.SourceHandle)).Select(c => new CoordinateCandidate { SourceHandle = c.SourceHandle!, RawText = c.RawText, SourceLayer = c.SourceLayer, Value = c.Value, PositionX = c.PositionX, PositionY = c.PositionY, PositionZ = c.PositionZ, Distance = Distance(x, y, c.PositionX, c.PositionY), DeterministicallyOwned = SharesInstantiatedOwner(equipmentHandle, c.SourceHandle!) }).Where(c => !double.IsNaN(c.Distance) && !double.IsInfinity(c.Distance) && c.Distance <= max).OrderBy(c => c.Distance).ThenBy(c => c.SourceHandle, StringComparer.Ordinal).ToList();
        }

        private static bool IsCoordinateCompatible(string? equipmentLayer, string equipmentHandle, CoordinateEvidence coordinate, Dictionary<string, List<TextSnapshot>> textByHandle)
        {
            string coordinateLayer = coordinate.SourceLayer ?? string.Empty;
            List<TextSnapshot>? sourceRecords = null;
            bool hasSourceRecord = !string.IsNullOrEmpty(coordinate.SourceHandle) && textByHandle.TryGetValue(coordinate.SourceHandle!, out sourceRecords) && sourceRecords!.Count == 1;
            TextSnapshot? source = hasSourceRecord ? sourceRecords![0] : null;

            // Model-space scope is required when the source record is available. VPORTS is
            // presentation infrastructure regardless of its recorded space.
            if (string.Equals(coordinateLayer, "VPORTS", StringComparison.OrdinalIgnoreCase) ||
                (source != null && string.Equals(source.Layer, "VPORTS", StringComparison.OrdinalIgnoreCase)) ||
                (source != null && string.Equals(source.SpaceKind, "Paper", StringComparison.OrdinalIgnoreCase))) return false;

            bool sharedOwner = SharesInstantiatedOwner(equipmentHandle, coordinate.SourceHandle ?? string.Empty);

            // Generic text identifiers carry no equipment ownership by themselves.
            if (string.Equals(coordinateLayer, "TXT-ID", StringComparison.OrdinalIgnoreCase)) return sharedOwner;

            // The explicit equipment coordinate family present in the pilot. Layer compatibility alone
            // is only enough to retain the coordinate as a diagnostic proximity candidate; it is never
            // enough to resolve a trusted placement. See Associate and SharesInstantiatedOwner.
            if (string.Equals(equipmentLayer, "EQ-TAG", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(coordinateLayer, "SLP-ID", StringComparison.OrdinalIgnoreCase)) return false;
                return string.Equals(coordinateLayer, "EQ-ID", StringComparison.OrdinalIgnoreCase) || sharedOwner;
            }

            return sharedOwner;
        }

        // Deterministic ownership is the only trusted coordinate linkage: the coordinate text and the
        // equipment tag are attributes of the same instantiated block occurrence (shared instantiation
        // root). Layer compatibility and proximity are diagnostics, never trusted ownership.
        private static bool SharesInstantiatedOwner(string equipmentHandle, string coordinateHandle)
        {
            string? equipmentRoot = InstantiatedRoot(equipmentHandle);
            string? coordinateRoot = InstantiatedRoot(coordinateHandle);
            return equipmentRoot != null && coordinateRoot != null && string.Equals(equipmentRoot, coordinateRoot, StringComparison.Ordinal);
        }

        private static void ApplyCenterlineEvidence(EquipmentPlacement placement, TextSnapshot tag, IEnumerable<PlotGeometryPrimitive>? primitives, SemanticDrawing? drawing)
        {
            if (primitives == null || !string.Equals(placement.TagSourceLayer, "EQ-TAG", StringComparison.OrdinalIgnoreCase)) return;

            // Deterministic linkage is anchored on the captured EQ-TAG text extents. When the extents
            // are unavailable (GeometricExtents threw, was non-finite, or degenerate) the linkage fails
            // closed instead of falling back to the insertion point or a text-height heuristic.
            TagExtents? tagExtents = TagExtents.From(tag);
            if (tagExtents == null) return;

            List<PlotGeometryPrimitive> centerlines = primitives.Where(x => x != null
                    && string.Equals(x.SourceLayer, "EQ-CEN", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.GeometryKind, "Line", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(x.SourceHandle)
                    && x.Vertices != null && x.Vertices.Count == 2
                    && IsFinite(x.Vertices[0].X) && IsFinite(x.Vertices[0].Y)
                    && IsFinite(x.Vertices[1].X) && IsFinite(x.Vertices[1].Y))
                .OrderBy(x => x.SourceHandle, StringComparer.Ordinal).ToList();
            List<CenterlineSegment> verticals = centerlines.Select(ToCenterlineSegment).Where(x => x != null && x.IsVertical).Select(x => x!).ToList();
            List<CenterlineSegment> horizontals = centerlines.Select(ToCenterlineSegment).Where(x => x != null && x.IsHorizontal).Select(x => x!).ToList();
            List<CenterlineSegment> crossingVerticals = verticals.Where(x => tagExtents.CrossesVertical(x)).ToList();
            List<CenterlineSegment> crossingHorizontals = horizontals.Where(x => tagExtents.CrossesHorizontal(x)).ToList();
            List<EquipmentCenterlineIntersection> candidates = new List<EquipmentCenterlineIntersection>();
            foreach (CenterlineSegment vertical in crossingVerticals)
            foreach (CenterlineSegment horizontal in crossingHorizontals)
            {
                double x = vertical.FixedX;
                double y = horizontal.FixedY;
                if (x < horizontal.MinimumX || x > horizontal.MaximumX ||
                    y < vertical.MinimumY || y > vertical.MaximumY) continue;
                candidates.Add(new EquipmentCenterlineIntersection
                {
                    X = x, Y = y,
                    VerticalSourceHandle = vertical.SourceHandle, HorizontalSourceHandle = horizontal.SourceHandle,
                    VerticalSourceLayer = vertical.SourceLayer, HorizontalSourceLayer = horizontal.SourceLayer,
                    TagToVerticalDistance = Math.Abs(tag.PositionX - x)
                });
            }
            candidates = candidates.OrderBy(x => x.X).ThenBy(x => x.Y).ThenBy(x => x.VerticalSourceHandle, StringComparer.Ordinal).ThenBy(x => x.HorizontalSourceHandle, StringComparer.Ordinal).ToList();
            if (candidates.Count == 0) return;

            var evidence = new EquipmentCenterlineEvidence
            {
                Status = candidates.Count == 1 ? "Resolved" : "Ambiguous",
                CalloutEastingSourceHandle = placement.Easting.SelectedSourceHandle,
                CalloutEastingSourceLayer = placement.Easting.SelectedSourceLayer,
                CalloutEastingValue = placement.Easting.SelectedValue,
                CalloutNorthingSourceHandle = placement.Northing.SelectedSourceHandle,
                CalloutNorthingSourceLayer = placement.Northing.SelectedSourceLayer,
                CalloutNorthingValue = placement.Northing.SelectedValue,
                Candidates = candidates
            };
            placement.CenterlineEvidence = evidence;
            if (candidates.Count != 1)
            {
                placement.PlacementStatus = "Ambiguous";
                return;
            }

            EquipmentCenterlineIntersection selected = candidates[0];
            evidence.Selected = selected;
            // Only deterministically owned callouts are trusted callout evidence. Ambiguous callouts
            // still fail closed. Proximity-only callouts neither block nor confirm: the unique EQ-CEN
            // intersection is existing explicit deterministic evidence and remains trusted on its own.
            bool eastingAmbiguous = placement.Easting.Status == "Ambiguous";
            bool northingAmbiguous = placement.Northing.Status == "Ambiguous";
            if (eastingAmbiguous || northingAmbiguous)
            {
                evidence.Status = "CalloutEvidenceIncomplete";
                return;
            }
            bool eastingTrusted = placement.Easting.Status == "Resolved" && placement.Easting.SelectedValue.HasValue;
            bool northingTrusted = placement.Northing.Status == "Resolved" && placement.Northing.SelectedValue.HasValue;
            if (eastingTrusted != northingTrusted)
            {
                evidence.Status = "CalloutEvidenceIncomplete";
                return;
            }
            if (eastingTrusted && northingTrusted)
            {
                bool unitConflict = HasDrawingUnitConflict(drawing);
                if (unitConflict || !IsUnitComparable(drawing))
                {
                    evidence.Status = "UnitEvidenceUnresolved";
                    placement.PlacementStatus = "Ambiguous";
                    return;
                }
                decimal centerEasting = ToDecimal(selected.X);
                decimal centerNorthing = ToDecimal(selected.Y);
                if (placement.Easting.SelectedValue != centerEasting || placement.Northing.SelectedValue != centerNorthing)
                {
                    evidence.Status = "Conflict";
                    placement.PlacementStatus = "Ambiguous";
                    return;
                }
                evidence.Status = "ConfirmedByCoordinateCallouts";
            }
            placement.Easting = CenterlineAssociation("E", selected.X, selected.VerticalSourceHandle, selected.VerticalSourceLayer);
            placement.Northing = CenterlineAssociation("N", selected.Y, selected.HorizontalSourceHandle, selected.HorizontalSourceLayer);
            placement.PlacementStatus = "Complete";
        }

        private static CoordinateAssociation CenterlineAssociation(string axis, double value, string sourceHandle, string sourceLayer) => new CoordinateAssociation
        {
            Axis = axis, Status = "Resolved", SelectedSourceHandle = sourceHandle, SelectedValue = ToDecimal(value),
            SelectedRawText = "EQ-CEN orthogonal centerline intersection", SelectedSourceLayer = sourceLayer
        };

        private static CenterlineSegment? ToCenterlineSegment(PlotGeometryPrimitive primitive)
        {
            double x1 = primitive.Vertices[0].X, y1 = primitive.Vertices[0].Y;
            double x2 = primitive.Vertices[1].X, y2 = primitive.Vertices[1].Y;
            bool vertical = x1 == x2 && y1 != y2;
            bool horizontal = y1 == y2 && x1 != x2;
            if (!vertical && !horizontal) return null;
            return new CenterlineSegment
            {
                SourceHandle = primitive.SourceHandle, SourceLayer = primitive.SourceLayer,
                IsVertical = vertical, IsHorizontal = horizontal,
                FixedX = vertical ? (x1 + x2) / 2d : 0d, FixedY = horizontal ? (y1 + y2) / 2d : 0d,
                MinimumX = Math.Min(x1, x2), MaximumX = Math.Max(x1, x2),
                MinimumY = Math.Min(y1, y2), MaximumY = Math.Max(y1, y2)
            };
        }

        private static bool IsUnitComparable(SemanticDrawing? drawing)
        {
            if (drawing == null || string.IsNullOrWhiteSpace(drawing.InsertionUnits)) return false;
            try { DrawingUnitResolver.Resolve(drawing.InsertionUnits!); return true; }
            catch (DrawingUnitResolutionException) { return false; }
        }

        private static bool HasDrawingUnitConflict(SemanticDrawing? drawing)
        {
            if (drawing == null) return true;
            return (drawing.ExplicitUnitDeclarations ?? new List<DrawingUnitDeclaration>()).Any(x =>
                !string.Equals(x.Unit, drawing.InsertionUnits, StringComparison.OrdinalIgnoreCase));
        }

        private static decimal ToDecimal(double value)
        {
            if (!IsFinite(value)) throw new InvalidOperationException("EQ-CEN intersection coordinates must be finite.");
            try { return Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture); }
            catch (OverflowException exception) { throw new InvalidOperationException("EQ-CEN intersection coordinates exceed supported range.", exception); }
        }

        private sealed class CenterlineSegment
        {
            public string SourceHandle { get; set; } = string.Empty;
            public string SourceLayer { get; set; } = string.Empty;
            public bool IsVertical { get; set; }
            public bool IsHorizontal { get; set; }
            public double FixedX { get; set; }
            public double FixedY { get; set; }
            public double MinimumX { get; set; }
            public double MaximumX { get; set; }
            public double MinimumY { get; set; }
            public double MaximumY { get; set; }
        }

        // The captured WCS axis-aligned extents of an EQ-TAG text. A centerline segment "crosses" these
        // extents only when the segment actually intersects the rectangle: the fixed ordinate falls
        // inside the corresponding rectangle span and the segment's real span overlaps the other span.
        private sealed class TagExtents
        {
            public double MinimumX { get; private set; }
            public double MinimumY { get; private set; }
            public double MaximumX { get; private set; }
            public double MaximumY { get; private set; }

            public static TagExtents? From(TextSnapshot tag)
            {
                if (!tag.ExtentMinX.HasValue || !tag.ExtentMinY.HasValue || !tag.ExtentMaxX.HasValue || !tag.ExtentMaxY.HasValue) return null;
                double minimumX = tag.ExtentMinX.Value, minimumY = tag.ExtentMinY.Value, maximumX = tag.ExtentMaxX.Value, maximumY = tag.ExtentMaxY.Value;
                if (!IsFinite(minimumX) || !IsFinite(minimumY) || !IsFinite(maximumX) || !IsFinite(maximumY)) return null;
                if (maximumX <= minimumX || maximumY <= minimumY) return null;
                return new TagExtents { MinimumX = minimumX, MinimumY = minimumY, MaximumX = maximumX, MaximumY = maximumY };
            }

            public bool CrossesVertical(CenterlineSegment vertical)
            {
                return vertical.FixedX >= MinimumX && vertical.FixedX <= MaximumX
                    && vertical.MinimumY <= MaximumY && vertical.MaximumY >= MinimumY;
            }

            public bool CrossesHorizontal(CenterlineSegment horizontal)
            {
                return horizontal.FixedY >= MinimumY && horizontal.FixedY <= MaximumY
                    && horizontal.MinimumX <= MaximumX && horizontal.MaximumX >= MinimumX;
            }
        }

        private static CoordinateAssociation Associate(string axis, List<CoordinateCandidate> candidates, double scale)
        {
            var result = new CoordinateAssociation { Axis = axis, Candidates = candidates };
            if (candidates.Count == 0) { result.Status = "Missing"; return result; }

            // Only deterministically owned coordinates may resolve. Proximity-only candidates (for
            // example EQ-ID layer compatibility without shared instantiated ownership) are retained as
            // diagnostic evidence but never trusted to resolve a placement or drive engineering distances.
            List<CoordinateCandidate> owned = candidates.Where(x => x.DeterministicallyOwned).ToList();
            if (owned.Count == 0)
            {
                if (candidates.Count > 1 && candidates[1].Distance - candidates[0].Distance <= scale * AmbiguityMarginFactor)
                {
                    result.Status = "Ambiguous";
                    result.Candidates = candidates.Where(c => c.Distance - candidates[0].Distance <= scale * AmbiguityMarginFactor).ToList();
                    return result;
                }
                result.Status = "ProximityOnly";
                return result;
            }

            if (owned.Count == 1) { result.Status = "Resolved"; result.Candidates = new List<CoordinateCandidate> { owned[0] }; Select(result, owned[0]); return result; }
            if (owned[1].Distance - owned[0].Distance <= scale * AmbiguityMarginFactor) { result.Status = "Ambiguous"; result.Candidates = owned.Where(c => c.Distance - owned[0].Distance <= scale * AmbiguityMarginFactor).ToList(); return result; }
            result.Status = "Resolved"; result.Candidates = new List<CoordinateCandidate> { owned[0] }; Select(result, owned[0]); return result;
        }
        private static void PreferCoherentPair(CoordinateAssociation easting, CoordinateAssociation northing, List<CoordinateCandidate> eastingCandidates, List<CoordinateCandidate> northingCandidates, double scale)
        {
            if (easting.Status == "Resolved" && northing.Status == "Resolved") return;
            if (easting.Status == "Missing" || northing.Status == "Missing") return;
            // Proximity-only evidence is never upgraded into trusted ownership by coherence.
            if (easting.Status == "ProximityOnly" || northing.Status == "ProximityOnly") return;
            IEnumerable<CoordinateCandidate> eastingPool = easting.Status == "Ambiguous" ? easting.Candidates : eastingCandidates.Where(x => string.Equals(x.SourceHandle, easting.SelectedSourceHandle, StringComparison.Ordinal));
            IEnumerable<CoordinateCandidate> northingPool = northing.Status == "Ambiguous" ? northing.Candidates : northingCandidates.Where(x => string.Equals(x.SourceHandle, northing.SelectedSourceHandle, StringComparison.Ordinal));
            List<CoordinatePairCandidate> pairs = eastingPool.SelectMany(e => northingPool.Select(n => new CoordinatePairCandidate { Easting = e, Northing = n, TotalDistance = e.Distance + n.Distance, CoherenceScore = CoherenceScore(e, n) })).Where(x => x.CoherenceScore > 0).OrderByDescending(x => x.CoherenceScore).ThenBy(x => x.TotalDistance).ThenBy(x => x.Easting.SourceHandle, StringComparer.Ordinal).ThenBy(x => x.Northing.SourceHandle, StringComparer.Ordinal).ToList();
            if (pairs.Count == 0) return;
            List<CoordinatePairCandidate> best = pairs.Where(x => x.CoherenceScore == pairs[0].CoherenceScore && x.TotalDistance - pairs[0].TotalDistance <= scale * AmbiguityMarginFactor).ToList();
            if (best.Count == 1)
            {
                if (easting.Status == "Ambiguous") SetResolved(easting, best[0].Easting);
                if (northing.Status == "Ambiguous") SetResolved(northing, best[0].Northing);
                return;
            }
        }
        private static int CoherenceScore(CoordinateCandidate easting, CoordinateCandidate northing)
        {
            string? eastingRoot = InstantiatedRoot(easting.SourceHandle);
            string? northingRoot = InstantiatedRoot(northing.SourceHandle);
            int score = 0;
            if (eastingRoot != null && northingRoot != null && string.Equals(eastingRoot, northingRoot, StringComparison.Ordinal)) score += 2;
            if (!string.IsNullOrWhiteSpace(easting.SourceLayer) && string.Equals(easting.SourceLayer, northing.SourceLayer, StringComparison.OrdinalIgnoreCase)) score++;
            return score;
        }
        private static string? InstantiatedRoot(string sourceHandle)
        {
            int separator = sourceHandle.IndexOf(':');
            return separator > 0 ? sourceHandle.Substring(0, separator) : null;
        }
        private static void SetResolved(CoordinateAssociation association, CoordinateCandidate candidate)
        {
            association.Status = "Resolved";
            association.Candidates = new List<CoordinateCandidate> { candidate };
            Select(association, candidate);
        }
        private static void SetAmbiguous(CoordinateAssociation association, IEnumerable<CoordinateCandidate> candidates)
        {
            association.Status = "Ambiguous";
            association.SelectedSourceHandle = null;
            association.SelectedValue = null;
            association.SelectedRawText = null;
            association.SelectedSourceLayer = null;
            association.SelectedDistance = null;
            association.Candidates = candidates.GroupBy(x => x.SourceHandle, StringComparer.Ordinal).Select(x => x.First()).OrderBy(x => x.Distance).ThenBy(x => x.SourceHandle, StringComparer.Ordinal).ToList();
        }
        private sealed class CoordinatePairCandidate
        {
            public CoordinateCandidate Easting { get; set; } = new CoordinateCandidate();
            public CoordinateCandidate Northing { get; set; } = new CoordinateCandidate();
            public double TotalDistance { get; set; }
            public int CoherenceScore { get; set; }
        }
        private static void Select(CoordinateAssociation association, CoordinateCandidate candidate) { association.SelectedSourceHandle = candidate.SourceHandle; association.SelectedValue = candidate.Value; association.SelectedRawText = candidate.RawText; association.SelectedSourceLayer = candidate.SourceLayer; association.SelectedDistance = candidate.Distance; }
        private static double Distance(double x1, double y1, double x2, double y2) { double dx = x1 - x2, dy = y1 - y2; return Math.Sqrt(dx * dx + dy * dy); }
        private static bool IsFinite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool IsPositiveFinite(double value) { return value > 0 && IsFinite(value); }
        private static double Median(List<double> values) { if (values.Count == 0) return 0; int middle = values.Count / 2; return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2.0; }
    }
}

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

        public static PlotPlanModel Build(EngineeringEntityIndex index, EngineeringDrawingModel model, DrawingSnapshot snapshot, PlotGeometryCapture? geometryCapture = null)
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
                List<CoordinateCandidate> eastingCandidates = FindCandidates("E", text.PositionX, text.PositionY, scale, compatibleCoordinates);
                List<CoordinateCandidate> northingCandidates = FindCandidates("N", text.PositionX, text.PositionY, scale, compatibleCoordinates);
                CoordinateAssociation easting = Associate("E", eastingCandidates, scale);
                CoordinateAssociation northing = Associate("N", northingCandidates, scale);
                PreferCoherentPair(easting, northing, eastingCandidates, northingCandidates, scale);
                var placement = new EquipmentPlacement { PlacementId = "placement:" + source.Item1.CanonicalId + ":" + handle, EquipmentEntityId = source.Item1.CanonicalId, NormalizedTag = source.Item2.NormalizedTag ?? source.Item1.NormalizedTag, TagSourceHandle = handle, TagSourceLayer = source.Item2.SourceLayer ?? text.Layer, TagPositionX = text.PositionX, TagPositionY = text.PositionY, TagPositionZ = text.PositionZ, TagTextHeight = text.Height, TagRotation = text.Rotation, AssociationScale = scale, Easting = easting, Northing = northing, EngineeringApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment };
                placement.PlacementStatus = placement.Easting.Status == "Ambiguous" || placement.Northing.Status == "Ambiguous" ? "Ambiguous" : placement.Easting.Status == "Resolved" && placement.Northing.Status == "Resolved" ? "Complete" : "Incomplete";
                placements.Add(placement);
            }
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
            return new PlotPlanModel { Drawing = model.Drawing, EquipmentPlacements = placements, Geometry = geometry, Measurements = measurements, Statistics = new PlotPlanStatistics { EquipmentPlacementCount = placements.Count, CompletePlacementCount = placements.Count(x => x.PlacementStatus == "Complete"), IncompletePlacementCount = placements.Count(x => x.PlacementStatus == "Incomplete"), AmbiguousPlacementCount = placements.Count(x => x.PlacementStatus == "Ambiguous"), ResolvedEastingCount = placements.Count(x => x.Easting.Status == "Resolved"), ResolvedNorthingCount = placements.Count(x => x.Northing.Status == "Resolved") } };
        }

        private static List<CoordinateCandidate> FindCandidates(string axis, double x, double y, double scale, IEnumerable<CoordinateEvidence> all)
        {
            double max = scale * MaxAssociationDistanceFactor;
            return all.Where(c => c.Axis == axis && !string.IsNullOrEmpty(c.SourceHandle)).Select(c => new CoordinateCandidate { SourceHandle = c.SourceHandle!, RawText = c.RawText, SourceLayer = c.SourceLayer, Value = c.Value, PositionX = c.PositionX, PositionY = c.PositionY, PositionZ = c.PositionZ, Distance = Distance(x, y, c.PositionX, c.PositionY) }).Where(c => !double.IsNaN(c.Distance) && !double.IsInfinity(c.Distance) && c.Distance <= max).OrderBy(c => c.Distance).ThenBy(c => c.SourceHandle, StringComparer.Ordinal).ToList();
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

            string? equipmentRoot = InstantiatedRoot(equipmentHandle);
            string? coordinateRoot = InstantiatedRoot(coordinate.SourceHandle ?? string.Empty);
            bool sharedOwner = equipmentRoot != null && coordinateRoot != null && string.Equals(equipmentRoot, coordinateRoot, StringComparison.Ordinal);

            // Generic text identifiers carry no equipment ownership by themselves.
            if (string.Equals(coordinateLayer, "TXT-ID", StringComparison.OrdinalIgnoreCase)) return sharedOwner;

            // This is the explicit equipment coordinate family present in the pilot.
            if (string.Equals(equipmentLayer, "EQ-TAG", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(coordinateLayer, "SLP-ID", StringComparison.OrdinalIgnoreCase)) return false;
                return string.Equals(coordinateLayer, "EQ-ID", StringComparison.OrdinalIgnoreCase) || sharedOwner;
            }

            return sharedOwner;
        }

        private static CoordinateAssociation Associate(string axis, List<CoordinateCandidate> candidates, double scale)
        {
            var result = new CoordinateAssociation { Axis = axis, Candidates = candidates };
            if (candidates.Count == 0) { result.Status = "Missing"; return result; }
            if (candidates.Count == 1) { result.Status = "Resolved"; Select(result, candidates[0]); return result; }
            if (candidates[1].Distance - candidates[0].Distance <= scale * AmbiguityMarginFactor) { result.Status = "Ambiguous"; result.Candidates = candidates.Where(c => c.Distance - candidates[0].Distance <= scale * AmbiguityMarginFactor).ToList(); return result; }
            result.Status = "Resolved"; result.Candidates = new List<CoordinateCandidate> { candidates[0] }; Select(result, candidates[0]); return result;
        }
        private static void PreferCoherentPair(CoordinateAssociation easting, CoordinateAssociation northing, List<CoordinateCandidate> eastingCandidates, List<CoordinateCandidate> northingCandidates, double scale)
        {
            if (easting.Status == "Resolved" && northing.Status == "Resolved") return;
            if (easting.Status == "Missing" || northing.Status == "Missing") return;
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
        private static bool IsPositiveFinite(double value) { return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value); }
        private static double Median(List<double> values) { if (values.Count == 0) return 0; int middle = values.Count / 2; return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2.0; }
    }
}

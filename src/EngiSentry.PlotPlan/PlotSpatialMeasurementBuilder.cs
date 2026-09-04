using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.PlotPlan
{
    /// <summary>Builds deterministic, non-evaluative XY measurement evidence from existing Plot Plan associations.</summary>
    public static class PlotSpatialMeasurementBuilder
    {
        public static PlotSpatialMeasurementModel Build(IEnumerable<EquipmentPlacement> placements, IEnumerable<SpatialCoordinateConstraint> constraints)
        {
            if (placements == null) throw new ArgumentNullException(nameof(placements));
            if (constraints == null) throw new ArgumentNullException(nameof(constraints));

            List<PlacementEvidence> eligiblePlacements = placements.Where(IsEligiblePlacement).Select(ToPlacementEvidence).OrderBy(x => x.PlacementId, StringComparer.Ordinal).ToList();
            EnsureUnique(eligiblePlacements.Select(x => x.PlacementId), "eligible PlacementId");
            List<ResolvedConstraintEvidence> resolvedConstraints = constraints.Where(x => string.Equals(x.Status, "Resolved", StringComparison.Ordinal)).Select(ToResolvedConstraintEvidence).OrderBy(x => x.Constraint.ConstraintId, StringComparer.Ordinal).ToList();
            EnsureUnique(resolvedConstraints.Select(x => x.Constraint.ConstraintId), "resolved ConstraintId");

            var pairs = new List<EquipmentPairMeasurement>();
            for (int i = 0; i < eligiblePlacements.Count; i++)
            for (int j = i + 1; j < eligiblePlacements.Count; j++)
                pairs.Add(BuildPair(eligiblePlacements[i], eligiblePlacements[j]));

            var spatial = new List<EquipmentSpatialMeasurement>();
            foreach (PlacementEvidence placement in eligiblePlacements)
            foreach (ResolvedConstraintEvidence constraint in resolvedConstraints)
                spatial.Add(BuildSpatial(placement, constraint));

            pairs = pairs.OrderBy(x => x.MeasurementId, StringComparer.Ordinal).ToList();
            spatial = spatial.OrderBy(x => x.MeasurementId, StringComparer.Ordinal).ToList();
            int expectedPairs = eligiblePlacements.Count * (eligiblePlacements.Count - 1) / 2;
            int expectedSpatial = eligiblePlacements.Count * resolvedConstraints.Count;
            if (pairs.Count != expectedPairs || spatial.Count != expectedSpatial) throw new InvalidOperationException("Spatial measurement count invariant failed.");

            return new PlotSpatialMeasurementModel
            {
                EquipmentPairMeasurements = pairs,
                EquipmentSpatialMeasurements = spatial,
                Statistics = new PlotSpatialMeasurementStatistics
                {
                    EligibleEquipmentPlacementCount = eligiblePlacements.Count,
                    EquipmentPairMeasurementCount = pairs.Count,
                    ResolvedSpatialConstraintSourceCount = resolvedConstraints.Count,
                    EquipmentSpatialMeasurementCount = spatial.Count,
                    RoadMeasurementCount = spatial.Count(x => string.Equals(x.SubjectKind, "Road", StringComparison.Ordinal)),
                    BoundaryMeasurementCount = spatial.Count(x => string.Equals(x.SubjectKind, "Boundary", StringComparison.Ordinal))
                }
            };
        }

        private static bool IsEligiblePlacement(EquipmentPlacement placement)
        {
            return placement != null
                && string.Equals(placement.PlacementStatus, "Complete", StringComparison.Ordinal)
                && placement.Easting != null && placement.Northing != null
                && string.Equals(placement.Easting.Status, "Resolved", StringComparison.Ordinal)
                && string.Equals(placement.Northing.Status, "Resolved", StringComparison.Ordinal)
                && placement.Easting.SelectedValue.HasValue
                && placement.Northing.SelectedValue.HasValue;
        }

        private static PlacementEvidence ToPlacementEvidence(EquipmentPlacement placement)
        {
            if (string.IsNullOrEmpty(placement.PlacementId)) throw new InvalidOperationException("Eligible equipment placement has no PlacementId.");
            decimal easting = placement.Easting.SelectedValue!.Value;
            decimal northing = placement.Northing.SelectedValue!.Value;
            EnsureFinite((double)easting, "equipment Easting");
            EnsureFinite((double)northing, "equipment Northing");
            return new PlacementEvidence(placement.PlacementId, placement.EquipmentEntityId, placement.NormalizedTag, easting, northing);
        }

        private static ResolvedConstraintEvidence ToResolvedConstraintEvidence(SpatialCoordinateConstraint constraint)
        {
            if (constraint == null || string.IsNullOrEmpty(constraint.ConstraintId)) throw new InvalidOperationException("Resolved spatial constraint has no ConstraintId.");
            if (string.IsNullOrEmpty(constraint.SelectedGeometryHandle) || string.IsNullOrEmpty(constraint.SelectedGeometryLayer) || string.IsNullOrEmpty(constraint.SelectedGeometryKind) || !constraint.SelectedSegmentIndex.HasValue)
                throw new InvalidOperationException("Resolved spatial constraint is missing selected geometry identity: " + constraint.ConstraintId);

            List<SpatialGeometryCandidate> selected = constraint.Candidates.Where(candidate => candidate != null
                && string.Equals(candidate.SourceHandle, constraint.SelectedGeometryHandle, StringComparison.Ordinal)
                && string.Equals(candidate.SourceLayer, constraint.SelectedGeometryLayer, StringComparison.Ordinal)
                && string.Equals(candidate.GeometryKind, constraint.SelectedGeometryKind, StringComparison.Ordinal)
                && candidate.SegmentIndex == constraint.SelectedSegmentIndex.Value).ToList();
            if (selected.Count != 1) throw new InvalidOperationException("Resolved spatial constraint cannot reproduce exactly one selected segment: " + constraint.ConstraintId);

            SpatialGeometryCandidate candidate = selected[0];
            EnsureFinite(candidate.SegmentStartX, "selected segment start X");
            EnsureFinite(candidate.SegmentStartY, "selected segment start Y");
            EnsureFinite(candidate.SegmentEndX, "selected segment end X");
            EnsureFinite(candidate.SegmentEndY, "selected segment end Y");
            double dx = candidate.SegmentEndX - candidate.SegmentStartX;
            double dy = candidate.SegmentEndY - candidate.SegmentStartY;
            double lengthSquared = dx * dx + dy * dy;
            if (!IsFinite(lengthSquared) || lengthSquared <= 0) throw new InvalidOperationException("Resolved spatial constraint has a zero-length selected segment: " + constraint.ConstraintId);
            return new ResolvedConstraintEvidence(constraint, candidate, lengthSquared);
        }

        private static EquipmentPairMeasurement BuildPair(PlacementEvidence a, PlacementEvidence b)
        {
            double distance = Distance((double)a.Easting, (double)a.Northing, (double)b.Easting, (double)b.Northing);
            return new EquipmentPairMeasurement
            {
                MeasurementId = "distance:equipment-pair:" + a.PlacementId + ":" + b.PlacementId,
                PlacementIdA = a.PlacementId, EquipmentEntityIdA = a.EquipmentEntityId, NormalizedTagA = a.NormalizedTag, EastingA = a.Easting, NorthingA = a.Northing,
                PlacementIdB = b.PlacementId, EquipmentEntityIdB = b.EquipmentEntityId, NormalizedTagB = b.NormalizedTag, EastingB = b.Easting, NorthingB = b.Northing,
                Distance2D = distance
            };
        }

        private static EquipmentSpatialMeasurement BuildSpatial(PlacementEvidence placement, ResolvedConstraintEvidence constraint)
        {
            double x = (double)placement.Easting;
            double y = (double)placement.Northing;
            double t = ((x - constraint.StartX) * (constraint.EndX - constraint.StartX) + (y - constraint.StartY) * (constraint.EndY - constraint.StartY)) / constraint.LengthSquared;
            t = Math.Max(0, Math.Min(1, t));
            double closestX = constraint.StartX + t * (constraint.EndX - constraint.StartX);
            double closestY = constraint.StartY + t * (constraint.EndY - constraint.StartY);
            EnsureFinite(closestX, "calculated closest point X");
            EnsureFinite(closestY, "calculated closest point Y");
            double distance = Distance(x, y, closestX, closestY);
            return new EquipmentSpatialMeasurement
            {
                MeasurementId = "distance:equipment-spatial:" + placement.PlacementId + ":" + constraint.Constraint.ConstraintId,
                PlacementId = placement.PlacementId, EquipmentEntityId = placement.EquipmentEntityId, NormalizedTag = placement.NormalizedTag, EquipmentEasting = placement.Easting, EquipmentNorthing = placement.Northing,
                ConstraintId = constraint.Constraint.ConstraintId, SubjectKind = constraint.Constraint.SubjectKind, Axis = constraint.Constraint.Axis, CoordinateValue = constraint.Constraint.CoordinateValue, LabelSourceHandle = constraint.Constraint.LabelSourceHandle,
                GeometrySourceHandle = constraint.Candidate.SourceHandle, GeometrySourceLayer = constraint.Candidate.SourceLayer, GeometryKind = constraint.Candidate.GeometryKind, SegmentIndex = constraint.Candidate.SegmentIndex,
                SegmentStartX = constraint.StartX, SegmentStartY = constraint.StartY, SegmentEndX = constraint.EndX, SegmentEndY = constraint.EndY,
                ClosestPointX = closestX, ClosestPointY = closestY, Distance2D = distance
            };
        }

        private static double Distance(double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            double result = Math.Sqrt(dx * dx + dy * dy);
            EnsureFinite(result, "calculated distance");
            return result;
        }

        private static void EnsureUnique(IEnumerable<string> values, string label)
        {
            if (values.GroupBy(x => x, StringComparer.Ordinal).Any(group => group.Count() != 1)) throw new InvalidOperationException("Duplicate " + label + " evidence.");
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static void EnsureFinite(double value, string label) { if (!IsFinite(value)) throw new InvalidOperationException("Non-finite " + label + " evidence."); }

        private sealed class PlacementEvidence
        {
            internal PlacementEvidence(string placementId, string equipmentEntityId, string normalizedTag, decimal easting, decimal northing) { PlacementId = placementId; EquipmentEntityId = equipmentEntityId; NormalizedTag = normalizedTag; Easting = easting; Northing = northing; }
            internal string PlacementId { get; } internal string EquipmentEntityId { get; } internal string NormalizedTag { get; } internal decimal Easting { get; } internal decimal Northing { get; }
        }

        private sealed class ResolvedConstraintEvidence
        {
            internal ResolvedConstraintEvidence(SpatialCoordinateConstraint constraint, SpatialGeometryCandidate candidate, double lengthSquared) { Constraint = constraint; Candidate = candidate; LengthSquared = lengthSquared; }
            internal SpatialCoordinateConstraint Constraint { get; } internal SpatialGeometryCandidate Candidate { get; } internal double LengthSquared { get; }
            internal double StartX => Candidate.SegmentStartX; internal double StartY => Candidate.SegmentStartY; internal double EndX => Candidate.SegmentEndX; internal double EndY => Candidate.SegmentEndY;
        }
    }
}

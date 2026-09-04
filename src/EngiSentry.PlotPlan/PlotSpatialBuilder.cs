using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using EngiSentry.Contracts;

namespace EngiSentry.PlotPlan
{
    /// <summary>Builds M2.2 explicit coordinate-to-segment evidence from normalized, in-memory inputs only.</summary>
    public static class PlotSpatialBuilder
    {
        private static readonly Regex BoundaryPattern = new Regex(@"^\s*UNIT\s+LIMIT\s+([EN])\.?\s+([+-]?\d+(?:\.\d+)?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex RoadPattern = new Regex(@"^\s*ROAD\s+C\.?\s*L\.?\s+([EN])\.?\s+([+-]?\d+(?:\.\d+)?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static List<SpatialCoordinateConstraint> Build(EngineeringDrawingModel model, PlotGeometryCapture capture)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (capture == null) throw new ArgumentNullException(nameof(capture));

            List<Label> labels = Labels(model).GroupBy(x => x.SubjectKind + "|" + x.SourceHandle, StringComparer.Ordinal).Select(x => SingleLabel(x.ToList())).OrderBy(x => x.ConstraintId, StringComparer.Ordinal).ToList();
            var text = capture.ModelSpaceTextSources.Where(x => !string.IsNullOrEmpty(x.SourceHandle)).GroupBy(x => x.SourceHandle, StringComparer.Ordinal).ToDictionary(x => x.Key, x => SingleText(x.ToList()), StringComparer.Ordinal);
            var primitives = capture.Primitives.Where(ValidPrimitive).GroupBy(x => x.SourceHandle, StringComparer.Ordinal).Select(x => SinglePrimitive(x.ToList())).OrderBy(x => x.SourceHandle, StringComparer.Ordinal).ToList();
            List<double> heights = labels.Where(x => text.ContainsKey(x.SourceHandle)).Select(x => text[x.SourceHandle].Height).Where(PositiveFinite).OrderBy(x => x).ToList();
            double fallback = Median(heights);
            if (labels.Count > 0 && !PositiveFinite(fallback) && labels.Any(x => !text.ContainsKey(x.SourceHandle) || !PositiveFinite(text[x.SourceHandle].Height))) throw new InvalidOperationException("No positive finite parsed Road/Boundary label height is available for spatial association scale.");

            var constraints = new List<SpatialCoordinateConstraint>();
            foreach (Label label in labels)
            {
                PlotTextSourceRecord source;
                bool mapped = text.TryGetValue(label.SourceHandle, out source);
                double scale = mapped && PositiveFinite(source.Height) ? source.Height : fallback;
                if (!PositiveFinite(scale)) throw new InvalidOperationException("No positive finite parsed Road/Boundary label height is available for spatial association scale.");
                double x = mapped ? source.PositionX : label.X;
                double y = mapped ? source.PositionY : label.Y;
                double z = mapped ? source.PositionZ : label.Z;
                var constraint = New(label, x, y, z, scale);
                List<SpatialGeometryCandidate> candidates = primitives.Select(p => Candidate(p, constraint)).Where(x => x != null).Cast<SpatialGeometryCandidate>().Where(x => x.Distance <= scale * PlotPlanBuilder.MaxAssociationDistanceFactor).OrderBy(x => x.Distance).ThenBy(x => x.SourceHandle, StringComparer.Ordinal).ThenBy(x => x.SegmentIndex).ToList();
                if (candidates.Count == 0) { constraint.Status = "Missing"; constraint.Reason = "NoGeometryMatchingExplicitCoordinate"; }
                else if (candidates.Count == 1) Resolve(constraint, candidates[0]);
                else if (candidates[1].Distance - candidates[0].Distance <= scale * PlotPlanBuilder.AmbiguityMarginFactor)
                {
                    constraint.Status = "Ambiguous";
                    constraint.Reason = "MultipleExplicitCoordinateGeometryMatches";
                    constraint.Candidates = candidates.Where(x => x.Distance - candidates[0].Distance <= scale * PlotPlanBuilder.AmbiguityMarginFactor).ToList();
                }
                else Resolve(constraint, candidates[0]);
                constraints.Add(constraint);
            }
            return constraints.OrderBy(x => x.ConstraintId, StringComparer.Ordinal).ToList();
        }

        private static IEnumerable<Label> Labels(EngineeringDrawingModel model)
        {
            return model.RoadLabels.Select(x => TryLabel("Road", x.RawText, x.SourceHandle, x.SourceLayer, x.PositionX, x.PositionY, x.PositionZ, RoadPattern))
                .Concat(model.BoundaryLabels.Select(x => TryLabel("Boundary", x.RawText, x.SourceHandle, x.SourceLayer, x.PositionX, x.PositionY, x.PositionZ, BoundaryPattern)))
                .Where(x => x != null).Cast<Label>();
        }

        private static Label? TryLabel(string kind, string? raw, string? handle, string? layer, double x, double y, double z, Regex pattern)
        {
            if (string.IsNullOrEmpty(handle) || raw == null) return null;
            Match match = pattern.Match(raw);
            if (!match.Success) return null;
            decimal value;
            if (!decimal.TryParse(match.Groups[2].Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)) return null;
            return new Label { SubjectKind = kind, SourceHandle = handle!, SourceLayer = layer, RawText = raw, X = x, Y = y, Z = z, Axis = match.Groups[1].Value.ToUpperInvariant(), Value = value };
        }

        private static SpatialCoordinateConstraint New(Label label, double x, double y, double z, double scale) => new SpatialCoordinateConstraint { ConstraintId = label.ConstraintId, SubjectKind = label.SubjectKind, LabelRawText = label.RawText, LabelSourceHandle = label.SourceHandle, LabelSourceLayer = label.SourceLayer, LabelPositionX = x, LabelPositionY = y, LabelPositionZ = z, Axis = label.Axis, CoordinateValue = label.Value, AssociationScale = scale };

        private static void Resolve(SpatialCoordinateConstraint constraint, SpatialGeometryCandidate candidate)
        {
            constraint.Status = "Resolved"; constraint.Reason = "UniqueExplicitCoordinateGeometryMatch"; constraint.SelectedGeometryHandle = candidate.SourceHandle; constraint.SelectedGeometryLayer = candidate.SourceLayer; constraint.SelectedGeometryKind = candidate.GeometryKind; constraint.SelectedSegmentIndex = candidate.SegmentIndex; constraint.SelectedDistance = candidate.Distance; constraint.Candidates = new List<SpatialGeometryCandidate> { candidate };
        }

        private static SpatialGeometryCandidate? Candidate(PlotGeometryPrimitive primitive, SpatialCoordinateConstraint constraint)
        {
            List<SpatialGeometryCandidate> matches = Segments(primitive).Where(x => Matches(x.Start, x.End, constraint.Axis, (double)constraint.CoordinateValue)).Select(x => new SpatialGeometryCandidate { SourceHandle = primitive.SourceHandle, SourceLayer = primitive.SourceLayer, GeometryKind = primitive.GeometryKind, SegmentIndex = x.Index, SegmentStartX = x.Start.X, SegmentStartY = x.Start.Y, SegmentStartZ = x.Start.Z, SegmentEndX = x.End.X, SegmentEndY = x.End.Y, SegmentEndZ = x.End.Z, Distance = Distance(constraint.LabelPositionX, constraint.LabelPositionY, x.Start, x.End), CoordinateDeviation = Deviation(x.Start, x.End, constraint.Axis, (double)constraint.CoordinateValue) }).OrderBy(x => x.Distance).ThenBy(x => x.SegmentIndex).ToList();
            return matches.Count == 0 ? null : matches[0];
        }

        private static IEnumerable<Segment> Segments(PlotGeometryPrimitive primitive)
        {
            int count = primitive.Vertices.Count; int segments = primitive.Closed ? count : count - 1;
            for (int i = 0; i < segments; i++) { PlotGeometryVertex a = primitive.Vertices[i]; PlotGeometryVertex b = primitive.Vertices[(i + 1) % count]; if (Finite(a.X) && Finite(a.Y) && Finite(b.X) && Finite(b.Y) && LengthSquared(a, b) > 0) yield return new Segment(i, a, b); }
        }

        private static bool Matches(PlotGeometryVertex a, PlotGeometryVertex b, string axis, double value) => axis == "E" ? Math.Abs(b.X - a.X) <= PlotPlanSpatialSettings.AxisAlignmentTolerance && Math.Abs(a.X - value) <= PlotPlanSpatialSettings.CoordinateMatchTolerance && Math.Abs(b.X - value) <= PlotPlanSpatialSettings.CoordinateMatchTolerance : Math.Abs(b.Y - a.Y) <= PlotPlanSpatialSettings.AxisAlignmentTolerance && Math.Abs(a.Y - value) <= PlotPlanSpatialSettings.CoordinateMatchTolerance && Math.Abs(b.Y - value) <= PlotPlanSpatialSettings.CoordinateMatchTolerance;
        private static double Deviation(PlotGeometryVertex a, PlotGeometryVertex b, string axis, double value) => axis == "E" ? Math.Max(Math.Abs(a.X - value), Math.Abs(b.X - value)) : Math.Max(Math.Abs(a.Y - value), Math.Abs(b.Y - value));
        private static double Distance(double x, double y, PlotGeometryVertex a, PlotGeometryVertex b) { double dx = b.X - a.X, dy = b.Y - a.Y, px = x - a.X, py = y - a.Y, length = dx * dx + dy * dy; double t = Math.Max(0, Math.Min(1, (px * dx + py * dy) / length)); double ex = x - (a.X + t * dx), ey = y - (a.Y + t * dy); return Math.Sqrt(ex * ex + ey * ey); }
        private static double LengthSquared(PlotGeometryVertex a, PlotGeometryVertex b) { double x = b.X - a.X, y = b.Y - a.Y; return x * x + y * y; }
        private static bool ValidPrimitive(PlotGeometryPrimitive p) => !string.IsNullOrEmpty(p.SourceHandle) && !string.IsNullOrEmpty(p.SourceLayer) && p.Vertices.Count >= 2 && p.Vertices.All(v => Finite(v.X) && Finite(v.Y) && Finite(v.Z));
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool PositiveFinite(double value) => Finite(value) && value > 0;
        private static double Median(List<double> values) { if (values.Count == 0) return 0; int middle = values.Count / 2; return values.Count % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle]; }
        private static PlotTextSourceRecord SingleText(List<PlotTextSourceRecord> values) { PlotTextSourceRecord first = values[0]; if (values.Any(x => x.SourceLayer != first.SourceLayer || x.PositionX != first.PositionX || x.PositionY != first.PositionY || x.PositionZ != first.PositionZ || x.Height != first.Height)) throw new InvalidOperationException("Conflicting duplicate ModelSpace text source: " + first.SourceHandle); return first; }
        private static PlotGeometryPrimitive SinglePrimitive(List<PlotGeometryPrimitive> values) { PlotGeometryPrimitive first = values[0]; if (values.Any(x => !SamePrimitive(x, first))) throw new InvalidOperationException("Conflicting duplicate geometry source: " + first.SourceHandle); return first; }
        private static bool SamePrimitive(PlotGeometryPrimitive a, PlotGeometryPrimitive b) => a.SourceLayer == b.SourceLayer && a.GeometryKind == b.GeometryKind && a.Closed == b.Closed && a.Vertices.Count == b.Vertices.Count && !a.Vertices.Where((v, i) => v.X != b.Vertices[i].X || v.Y != b.Vertices[i].Y || v.Z != b.Vertices[i].Z).Any();
        private static Label SingleLabel(List<Label> values) { Label first = values[0]; if (values.Any(x => !x.Same(first))) throw new InvalidOperationException("Conflicting duplicate spatial constraint evidence: " + first.SourceHandle); return first; }

        private sealed class Label
        {
            public string SubjectKind = string.Empty, SourceHandle = string.Empty, Axis = string.Empty; public string? SourceLayer, RawText; public double X, Y, Z; public decimal Value;
            public string ConstraintId => "spatial-constraint:" + SubjectKind.ToLowerInvariant() + ":" + SourceHandle;
            public bool Same(Label other) => RawText == other.RawText && Axis == other.Axis && Value == other.Value && X == other.X && Y == other.Y && Z == other.Z && SourceLayer == other.SourceLayer;
        }
        private sealed class Segment { public Segment(int index, PlotGeometryVertex start, PlotGeometryVertex end) { Index = index; Start = start; End = end; } public int Index { get; } public PlotGeometryVertex Start { get; } public PlotGeometryVertex End { get; } }
    }

    public static class PlotPlanSpatialSettings
    {
        // Floating-point geometry comparison tolerances in drawing units; not engineering criteria.
        public const double CoordinateMatchTolerance = 0.001;
        public const double AxisAlignmentTolerance = 0.001;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.PlotPlan
{
    public static class PlotGeometryBuilder
    {
        public static PlotGeometryModel Build(EngineeringDrawingModel model, PlotGeometryCapture capture)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (capture == null) throw new ArgumentNullException(nameof(capture));
            var labels = Labels(model).OrderBy(x => x.SubjectKind, StringComparer.Ordinal).ThenBy(x => x.SourceHandle, StringComparer.Ordinal).ToList();
            var text = capture.ModelSpaceTextSources.Where(x => !string.IsNullOrEmpty(x.SourceHandle)).GroupBy(x => x.SourceHandle, StringComparer.Ordinal).ToDictionary(x => x.Key, x => SingleText(x.ToList()), StringComparer.Ordinal);
            var validLayers = new HashSet<string>(labels.Select(x => x.SourceLayer ?? string.Empty).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
            var primitives = capture.Primitives.Where(ValidPrimitive).GroupBy(x => x.SourceHandle, StringComparer.Ordinal).Select(x => SinglePrimitive(x.ToList())).Where(x => validLayers.Contains(x.SourceLayer) || IsPlotGeometryLayer(x.SourceLayer)).OrderBy(x => x.SourceHandle, StringComparer.Ordinal).ToList();
            var heights = labels.Where(x => text.ContainsKey(x.SourceHandle)).Select(x => text[x.SourceHandle].Height).Where(PositiveFinite).OrderBy(x => x).ToList();
            var associations = new List<GeometryAssociation>();
            foreach (Label label in labels)
            {
                PlotTextSourceRecord source;
                if (!text.TryGetValue(label.SourceHandle, out source)) { associations.Add(Missing(label, "LabelOutsideModelSpaceScope")); continue; }
                double scale = PositiveFinite(source.Height) ? source.Height : Median(heights);
                if (!PositiveFinite(scale)) throw new InvalidOperationException("No positive finite ModelSpace Road/Boundary label height is available for association scale.");
                var ranked = primitives
                    .Select(p => new { Primitive = p, Tier = EvidenceTier(label, p) })
                    .Where(x => x.Tier >= 0)
                    .Select(x => new { x.Primitive, x.Tier, Distance = PrimitiveDistance(x.Primitive, source.PositionX, source.PositionY) })
                    .OrderBy(x => x.Tier)
                    .ThenBy(x => x.Distance)
                    .ThenBy(x => x.Primitive.SourceHandle, StringComparer.Ordinal)
                    .ToList();
                if (ranked.Count == 0) { associations.Add(Missing(label, "NoSupportedSameLayerGeometry", scale, source)); continue; }
                double max = scale * PlotPlanBuilder.MaxAssociationDistanceFactor;
                var distanceEligible = ranked.Where(x => x.Distance <= max).ToList();
                if (distanceEligible.Count == 0) { associations.Add(Missing(label, "NoSupportedGeometryWithinAssociationDistance", scale, source)); continue; }
                int bestTier = distanceEligible[0].Tier;
                List<GeometryCandidate> within = distanceEligible.Where(x => x.Tier == bestTier).Select(x => Candidate(x.Primitive, source.PositionX, source.PositionY)).ToList();
                var a = New(label, scale, source);
                if (within.Count == 1) { Resolve(a, within[0]); }
                else if (within[1].Distance - within[0].Distance <= scale * PlotPlanBuilder.AmbiguityMarginFactor) { a.Status = "Ambiguous"; a.Reason = "MultipleGeometryCandidatesWithinAmbiguityMargin"; a.Candidates = within.Where(x => x.Distance - within[0].Distance <= scale * PlotPlanBuilder.AmbiguityMarginFactor).ToList(); }
                else Resolve(a, within[0]);
                associations.Add(a);
            }
            associations = associations.OrderBy(x => x.AssociationId, StringComparer.Ordinal).ToList();
            return new PlotGeometryModel { Primitives = primitives, Associations = associations, Statistics = new PlotGeometryStatistics { SupportedPrimitiveCount = primitives.Count, GeometryAssociationCount = associations.Count, RoadAssociationCount = associations.Count(x => x.SubjectKind == "Road"), BoundaryAssociationCount = associations.Count(x => x.SubjectKind == "Boundary"), ResolvedAssociationCount = associations.Count(x => x.Status == "Resolved"), MissingAssociationCount = associations.Count(x => x.Status == "Missing"), AmbiguousAssociationCount = associations.Count(x => x.Status == "Ambiguous") } };
        }
        private static IEnumerable<Label> Labels(EngineeringDrawingModel model) => model.RoadLabels.Select(x => Label.From("Road", x)).Concat(model.BoundaryLabels.Select(x => Label.From("Boundary", x))).Where(x => !string.IsNullOrEmpty(x.SourceHandle)).GroupBy(x => x.SubjectKind + "|" + x.SourceHandle, StringComparer.Ordinal).Select(x => SingleLabel(x.ToList()));
        private static Label SingleLabel(List<Label> values) { Label first = values[0]; if (values.Any(x => !x.Same(first))) throw new InvalidOperationException("Conflicting duplicate geometry label evidence: " + first.SourceHandle); return first; }
        private static PlotTextSourceRecord SingleText(List<PlotTextSourceRecord> values) { PlotTextSourceRecord first = values[0]; if (values.Any(x => x.SourceLayer != first.SourceLayer || x.PositionX != first.PositionX || x.PositionY != first.PositionY || x.PositionZ != first.PositionZ || x.Height != first.Height)) throw new InvalidOperationException("Conflicting duplicate ModelSpace text source: " + first.SourceHandle); return first; }
        private static PlotGeometryPrimitive SinglePrimitive(List<PlotGeometryPrimitive> values) { PlotGeometryPrimitive first = values[0]; if (values.Any(x => !SamePrimitive(x, first))) throw new InvalidOperationException("Conflicting duplicate geometry source: " + first.SourceHandle); return first; }
        private static bool SamePrimitive(PlotGeometryPrimitive a, PlotGeometryPrimitive b) => a.SourceLayer == b.SourceLayer && a.GeometryKind == b.GeometryKind && a.Closed == b.Closed && a.Vertices.Count == b.Vertices.Count && !a.Vertices.Where((v, i) => v.X != b.Vertices[i].X || v.Y != b.Vertices[i].Y || v.Z != b.Vertices[i].Z).Any();
        private static bool ValidPrimitive(PlotGeometryPrimitive p) => !string.IsNullOrEmpty(p.SourceHandle) && !string.IsNullOrEmpty(p.SourceLayer) && p.Vertices.Count >= 2 && p.Vertices.All(v => Finite(v.X) && Finite(v.Y) && Finite(v.Z));
        private static bool IsPlotGeometryLayer(string? layer)
        {
            if (string.IsNullOrWhiteSpace(layer)) return false;
            string value = layer!.ToUpperInvariant();
            return value.Contains("ROAD") || value.Contains("FENCE") || value.Contains("BOUNDARY") || value.Contains("UNIT-LIMIT") || value.Contains("UNIT LIMIT") || value.Contains("BATTERY-LIMIT") || value.Contains("BATTERY LIMIT");
        }
        private static GeometryCandidate Candidate(PlotGeometryPrimitive p, double x, double y) => new GeometryCandidate { SourceHandle = p.SourceHandle, SourceLayer = p.SourceLayer, GeometryKind = p.GeometryKind, Closed = p.Closed, VertexCount = p.Vertices.Count, Distance = PrimitiveDistance(p, x, y) };
        private static int EvidenceTier(Label label, PlotGeometryPrimitive primitive)
        {
            if (string.Equals(primitive.SourceLayer, label.SourceLayer, StringComparison.OrdinalIgnoreCase)) return 0;
            return CompatibleCrossLayer(label) && CompatibleGeometryLayer(label, primitive.SourceLayer) ? 1 : -1;
        }
        private static bool CompatibleCrossLayer(Label label) => label.SubjectKind == "Road" || label.SubjectKind == "Boundary";
        private static bool CompatibleGeometryLayer(Label label, string layer)
        {
            string value = (layer ?? string.Empty).ToUpperInvariant();
            if (label.SubjectKind == "Road") return value.Contains("ROAD") || value.Contains("CENTERLINE") || value.Contains("CENTRELINE");
            string raw = (label.RawText ?? string.Empty).ToUpperInvariant();
            if (raw.Contains("FENCE")) return value.Contains("FENCE");
            if (raw.Contains("UNIT LIMIT")) return value.Contains("UNIT-LIMIT") || value.Contains("UNIT LIMIT") || value.Contains("UNITLIMIT");
            if (raw.Contains("BATTERY LIMIT")) return value.Contains("BATTERY-LIMIT") || value.Contains("BATTERY LIMIT") || value.Contains("BATTERYLIMIT");
            return value.Contains("BOUNDARY");
        }
        private static double PrimitiveDistance(PlotGeometryPrimitive p, double x, double y) { int n = p.Vertices.Count; int segments = p.Closed ? n : n - 1; double min = double.MaxValue; for (int i = 0; i < segments; i++) min = Math.Min(min, SegmentDistance(x, y, p.Vertices[i], p.Vertices[(i + 1) % n])); return min; }
        private static double SegmentDistance(double x, double y, PlotGeometryVertex a, PlotGeometryVertex b) { double dx = b.X - a.X, dy = b.Y - a.Y, px = x - a.X, py = y - a.Y, d = dx * dx + dy * dy; if (d == 0) return Math.Sqrt(px * px + py * py); double t = Math.Max(0, Math.Min(1, (px * dx + py * dy) / d)); double cx = a.X + t * dx, cy = a.Y + t * dy, ex = x - cx, ey = y - cy; return Math.Sqrt(ex * ex + ey * ey); }
        private static GeometryAssociation New(Label l, double scale, PlotTextSourceRecord s) => new GeometryAssociation { AssociationId = "geometry-association:" + l.SubjectKind.ToLowerInvariant() + ":" + l.SourceHandle, SubjectKind = l.SubjectKind, LabelRawText = l.RawText, LabelSourceHandle = l.SourceHandle, LabelSourceLayer = l.SourceLayer, LabelPositionX = s.PositionX, LabelPositionY = s.PositionY, LabelPositionZ = s.PositionZ, AssociationScale = scale };
        private static GeometryAssociation Missing(Label l, string reason, double scale = 0, PlotTextSourceRecord? s = null) => s == null ? new GeometryAssociation { AssociationId = "geometry-association:" + l.SubjectKind.ToLowerInvariant() + ":" + l.SourceHandle, SubjectKind = l.SubjectKind, LabelRawText = l.RawText, LabelSourceHandle = l.SourceHandle, LabelSourceLayer = l.SourceLayer, LabelPositionX = l.X, LabelPositionY = l.Y, LabelPositionZ = l.Z, Status = "Missing", Reason = reason } : new GeometryAssociation { AssociationId = "geometry-association:" + l.SubjectKind.ToLowerInvariant() + ":" + l.SourceHandle, SubjectKind = l.SubjectKind, LabelRawText = l.RawText, LabelSourceHandle = l.SourceHandle, LabelSourceLayer = l.SourceLayer, LabelPositionX = s.PositionX, LabelPositionY = s.PositionY, LabelPositionZ = s.PositionZ, AssociationScale = scale, Status = "Missing", Reason = reason };
        private static void Resolve(GeometryAssociation a, GeometryCandidate c) { a.Status = "Resolved"; a.Reason = "UniqueNearestGeometry"; a.SelectedGeometryHandle = c.SourceHandle; a.SelectedDistance = c.Distance; a.Candidates = new List<GeometryCandidate> { c }; }
        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v); private static bool PositiveFinite(double v) => Finite(v) && v > 0; private static double Median(List<double> xs) { if (xs.Count == 0) return 0; int i = xs.Count / 2; return xs.Count % 2 == 0 ? (xs[i - 1] + xs[i]) / 2 : xs[i]; }
        private sealed class Label { public string SubjectKind = string.Empty, SourceHandle = string.Empty; public string? SourceLayer, RawText; public double X,Y,Z; public static Label From(string kind, TextEvidence x) => new Label { SubjectKind=kind, SourceHandle=x.SourceHandle ?? string.Empty, SourceLayer=x.SourceLayer, RawText=x.RawText, X=x.PositionX,Y=x.PositionY,Z=x.PositionZ}; public static Label From(string kind, BoundaryEvidence x) => new Label { SubjectKind=kind, SourceHandle=x.SourceHandle ?? string.Empty, SourceLayer=x.SourceLayer, RawText=x.RawText, X=x.PositionX,Y=x.PositionY,Z=x.PositionZ}; public bool Same(Label x) => SourceLayer==x.SourceLayer && RawText==x.RawText && X==x.X && Y==x.Y && Z==x.Z; }
    }
}

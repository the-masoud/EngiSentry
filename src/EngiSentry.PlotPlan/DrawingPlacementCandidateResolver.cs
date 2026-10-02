using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using EngiSentry.Contracts;

namespace EngiSentry.PlotPlan
{
    /// <summary>
    /// Interprets a structured annotation cluster as a review suggestion only. It never selects
    /// a trusted CoordinateAssociation. All predicates are symmetric and all ties fail closed.
    /// </summary>
    internal static class DrawingPlacementCandidateResolver
    {
        private static readonly Regex XLabel = new Regex(@"^\s*X\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex YLabel = new Regex(@"^\s*Y\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static void Apply(IReadOnlyList<EquipmentPlacement> placements, IReadOnlyList<TextSnapshot> texts, SemanticDrawing? drawing)
        {
            var byHandle = texts.Where(x => !string.IsNullOrWhiteSpace(x.Handle))
                .GroupBy(x => x.Handle!, StringComparer.Ordinal)
                .Where(x => x.Count() == 1)
                .ToDictionary(x => x.Key, x => x.Single(), StringComparer.Ordinal);
            bool comparable = drawing != null
                && string.Equals(drawing.InsertionUnits, "Millimeters", StringComparison.Ordinal)
                && !(drawing.ExplicitUnitDeclarations ?? new List<DrawingUnitDeclaration>()).Any(x =>
                    !string.Equals(x.Unit, drawing.InsertionUnits, StringComparison.OrdinalIgnoreCase));

            var possible = new Dictionary<string, List<DrawingPlacementCandidate>>(StringComparer.Ordinal);
            var eligible = new List<GraphNode>();
            foreach (EquipmentPlacement placement in placements)
            {
                placement.DrawingCandidate = null;
                placement.DrawingDiagnostic = new DrawingPlacementDiagnostic();
                placement.ResolutionState = placement.PlacementStatus == "Complete"
                    ? PlacementResolutionState.Resolved : PlacementResolutionState.Unresolved;
                placement.ResolutionReason = placement.PlacementStatus == "Complete"
                    ? "Trusted coordinate evidence" : "No unique structured drawing placement candidate";
                if (placement.ResolutionState == PlacementResolutionState.Resolved) continue;
                if (placement.AuthoritativeCoordinate != null && placement.AuthoritativeCoordinate.PlacementStatus != AuthoritativePlacementStatuses.Resolved)
                {
                    placement.ResolutionReason = "Conflicting authoritative coordinate evidence";
                    continue;
                }
                if (placement.CenterlineEvidence?.Status == "Conflict" || placement.CenterlineEvidence?.Status == "Ambiguous")
                {
                    placement.ResolutionReason = "Conflicting or ambiguous centerline evidence";
                    continue;
                }
                if (!string.Equals(placement.TagSourceLayer, "EQ-TAG", StringComparison.OrdinalIgnoreCase)
                    || !byHandle.TryGetValue(placement.TagSourceHandle, out TextSnapshot? tag)
                    || (tag.Text ?? string.Empty).Contains("/"))
                {
                    placement.ResolutionReason = "Combined or non-EQ-TAG annotation needs individual review";
                    continue;
                }
                double scale = placement.AssociationScale;
                if (!Finite(scale) || scale <= 0 || !Finite(tag.PositionX) || !Finite(tag.PositionY)) continue;
                var xLabels = ValidLabels(placement.Easting.Candidates, byHandle, XLabel);
                var yLabels = ValidLabels(placement.Northing.Candidates, byHandle, YLabel);
                placement.DrawingDiagnostic.XCandidateHandles = xLabels.Select(x => x.Item1.SourceHandle).Take(16).ToList();
                placement.DrawingDiagnostic.YCandidateHandles = yLabels.Select(x => x.Item1.SourceHandle).Take(16).ToList();
                eligible.Add(new GraphNode(placement, tag, xLabels, yLabels));
                var pairs = new List<DrawingPlacementCandidate>();
                foreach (var x in xLabels)
                foreach (var y in yLabels)
                {
                    if (x.Item1.SourceHandle == y.Item1.SourceHandle) continue;
                    TextSnapshot xt = x.Item2, yt = y.Item2;
                    double labelScale = Math.Max(scale, Math.Max(xt.Height, yt.Height));
                    if (!Finite(labelScale) || labelScale <= 0) continue;
                    if (!SameOrientation(tag.Rotation, xt.Rotation) || !SameOrientation(xt.Rotation, yt.Rotation)) continue;
                    if (!CompatibleHeight(xt.Height, yt.Height)) continue;
                    double dx = Math.Abs(xt.PositionX - yt.PositionX);
                    double dy = Math.Abs(xt.PositionY - yt.PositionY);
                    if (!(dx <= labelScale * 0.75 || dy <= labelScale * 0.75)) continue;
                    if (Distance(xt.PositionX, xt.PositionY, yt.PositionX, yt.PositionY) > labelScale * 8) continue;
                    double middleX = (xt.PositionX + yt.PositionX) / 2;
                    double middleY = (xt.PositionY + yt.PositionY) / 2;
                    if (Distance(tag.PositionX, tag.PositionY, middleX, middleY) > scale * 20) continue;
                    if (!(Math.Abs(tag.PositionX - middleX) <= scale * 4 || Math.Abs(tag.PositionY - middleY) <= scale * 4)) continue;
                    pairs.Add(new DrawingPlacementCandidate
                    {
                        OccurrenceId = placement.PlacementId, EquipmentTag = placement.NormalizedTag,
                        CandidateX = x.Item1.Value, CandidateY = y.Item1.Value,
                        TagEntityHandle = placement.TagSourceHandle, XEntityHandle = x.Item1.SourceHandle,
                        YEntityHandle = y.Item1.SourceHandle, CoordinatesInDrawingUnits = comparable,
                        EvidenceSummary = "EQ-TAG and literal EQ-ID X/Y labels share orientation, aligned annotation layout, and a single local cluster; inferred only"
                    });
                }
                possible[placement.PlacementId] = pairs.OrderBy(x => x.XEntityHandle, StringComparer.Ordinal)
                    .ThenBy(x => x.YEntityHandle, StringComparer.Ordinal).ToList();
            }

            // Construct all group interpretations before selecting any occurrence. A repeated
            // annotation displacement and coordinate step is required; a nearby axis alone is not.
            var groups = SharedAxisGroups(eligible, comparable);

            // Exclusivity is global to drawing occurrences. One annotation pair cannot silently
            // become the placement of two tags, including repeated canonical identities.
            foreach (EquipmentPlacement placement in placements)
            {
                if (!possible.TryGetValue(placement.PlacementId, out List<DrawingPlacementCandidate>? pairs)) continue;
                var involving = groups.Where(g => g.First.OccurrenceId == placement.PlacementId ||
                    g.Second.OccurrenceId == placement.PlacementId).ToList();
                placement.DrawingDiagnostic.SharedAxisCandidateHandles = involving.Select(g => g.SharedHandle)
                    .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).Take(16).ToList();
                placement.DrawingDiagnostic.CompetingCandidateCount = involving.Count > 0 ? involving.Count : pairs.Count;
                if (involving.Count > 0)
                {
                    if (involving.Count != 1)
                    {
                        placement.ResolutionReason = "Competing shared-axis group assignments";
                        placement.DrawingDiagnostic.EvidenceClass = "SharedAxisAmbiguous";
                        placement.DrawingDiagnostic.RejectionReason = placement.ResolutionReason;
                        continue;
                    }
                    var group = involving[0];
                    bool conflict = groups.Any(g => !ReferenceEquals(g, group) &&
                        (g.UsesExclusiveHandle(group.First) || g.UsesExclusiveHandle(group.Second) ||
                         g.SharedHandle == group.SharedHandle));
                    if (conflict)
                    {
                        placement.ResolutionReason = "Shared-axis annotation competes across equipment groups";
                        placement.DrawingDiagnostic.EvidenceClass = "SharedAxisAmbiguous";
                        placement.DrawingDiagnostic.RejectionReason = placement.ResolutionReason;
                        continue;
                    }
                    DrawingPlacementCandidate sharedCandidate = group.First.OccurrenceId == placement.PlacementId
                        ? group.First : group.Second;
                    placement.DrawingCandidate = sharedCandidate;
                    placement.ResolutionState = PlacementResolutionState.ReviewRequired;
                    placement.ResolutionReason = comparable
                        ? "Unique structured shared-axis graph; engineer review required"
                        : "Unique structured shared-axis graph; coordinate-to-WCS unit contract unresolved";
                    placement.DrawingDiagnostic.EvidenceClass = sharedCandidate.EvidenceType;
                    placement.DrawingDiagnostic.CandidateIntersection = sharedCandidate.CandidateX.ToString(CultureInfo.InvariantCulture)
                        + "," + sharedCandidate.CandidateY.ToString(CultureInfo.InvariantCulture);
                    continue;
                }
                if (pairs.Count != 1)
                {
                    if (pairs.Count > 1) placement.ResolutionReason = "Multiple structurally plausible EQ-ID coordinate clusters";
                    placement.DrawingDiagnostic.EvidenceClass = pairs.Count > 1 ? "LocalClusterAmbiguous" : "NoStructuredGraph";
                    placement.DrawingDiagnostic.RejectionReason = placement.ResolutionReason;
                    continue;
                }
                DrawingPlacementCandidate candidate = pairs[0];
                bool shared = possible.Any(entry => entry.Key != placement.PlacementId && entry.Value.Any(other =>
                    other.XEntityHandle == candidate.XEntityHandle || other.YEntityHandle == candidate.YEntityHandle));
                if (shared)
                {
                    placement.ResolutionReason = "EQ-ID annotation cluster is plausible for multiple equipment occurrences";
                    placement.DrawingDiagnostic.EvidenceClass = "LocalClusterAmbiguous";
                    placement.DrawingDiagnostic.RejectionReason = placement.ResolutionReason;
                    continue;
                }
                placement.DrawingCandidate = candidate;
                placement.ResolutionState = PlacementResolutionState.ReviewRequired;
                placement.ResolutionReason = comparable
                    ? "Unique inferred drawing annotation cluster; engineer review required"
                    : "Unique inferred drawing annotation cluster; coordinate-to-WCS unit contract unresolved";
                placement.DrawingDiagnostic.EvidenceClass = "LocalCluster";
                placement.DrawingDiagnostic.CandidateIntersection = candidate.CandidateX.ToString(CultureInfo.InvariantCulture)
                    + "," + candidate.CandidateY.ToString(CultureInfo.InvariantCulture);
            }
            foreach (EquipmentPlacement placement in placements.Where(x => x.ResolutionState != PlacementResolutionState.ReviewRequired))
                if (string.IsNullOrEmpty(placement.DrawingDiagnostic.RejectionReason))
                    placement.DrawingDiagnostic.RejectionReason = placement.ResolutionReason;
        }

        private sealed class GraphNode
        {
            internal readonly EquipmentPlacement Placement;
            internal readonly TextSnapshot Tag;
            internal readonly List<Tuple<CoordinateCandidate, TextSnapshot>> X;
            internal readonly List<Tuple<CoordinateCandidate, TextSnapshot>> Y;
            internal GraphNode(EquipmentPlacement placement, TextSnapshot tag,
                List<Tuple<CoordinateCandidate, TextSnapshot>> x, List<Tuple<CoordinateCandidate, TextSnapshot>> y)
            { Placement = placement; Tag = tag; X = x; Y = y; }
        }

        private sealed class SharedGroup
        {
            internal DrawingPlacementCandidate First = new DrawingPlacementCandidate();
            internal DrawingPlacementCandidate Second = new DrawingPlacementCandidate();
            internal string SharedHandle = string.Empty;
            internal bool UsesExclusiveHandle(DrawingPlacementCandidate candidate) =>
                First.TagEntityHandle == candidate.TagEntityHandle || Second.TagEntityHandle == candidate.TagEntityHandle ||
                First.XEntityHandle == candidate.XEntityHandle && First.XEntityHandle != SharedHandle ||
                First.YEntityHandle == candidate.YEntityHandle && First.YEntityHandle != SharedHandle ||
                Second.XEntityHandle == candidate.XEntityHandle && Second.XEntityHandle != SharedHandle ||
                Second.YEntityHandle == candidate.YEntityHandle && Second.YEntityHandle != SharedHandle;
        }

        private static List<SharedGroup> SharedAxisGroups(List<GraphNode> nodes, bool comparable)
        {
            var result = new List<SharedGroup>();
            var sorted = nodes.OrderBy(n => n.Placement.PlacementId, StringComparer.Ordinal).ToList();
            for (int i = 0; i < sorted.Count; i++)
            for (int j = i + 1; j < sorted.Count; j++)
            {
                GraphNode a = sorted[i], b = sorted[j];
                double scale = Math.Max(a.Placement.AssociationScale, b.Placement.AssociationScale);
                double dx = b.Tag.PositionX - a.Tag.PositionX, dy = b.Tag.PositionY - a.Tag.PositionY;
                double spacing = Distance(a.Tag.PositionX, a.Tag.PositionY, b.Tag.PositionX, b.Tag.PositionY);
                if (!Finite(scale) || scale <= 0 || spacing < scale * 2 || spacing > scale * 12 ||
                    Math.Min(Math.Abs(dx), Math.Abs(dy)) > scale * 0.35 ||
                    !SameOrientation(a.Tag.Rotation, b.Tag.Rotation) || !CompatibleHeight(a.Tag.Height, b.Tag.Height)) continue;
                bool vertical = Math.Abs(dy) > Math.Abs(dx);
                AddGroups(result, a, b, a.Y, b.Y, a.X, b.X, true, vertical, spacing, scale, comparable);
                AddGroups(result, a, b, a.X, b.X, a.Y, b.Y, false, vertical, spacing, scale, comparable);
            }
            return result;
        }

        private static void AddGroups(List<SharedGroup> result, GraphNode a, GraphNode b,
            List<Tuple<CoordinateCandidate, TextSnapshot>> uniqueA,
            List<Tuple<CoordinateCandidate, TextSnapshot>> uniqueB,
            List<Tuple<CoordinateCandidate, TextSnapshot>> sharedA,
            List<Tuple<CoordinateCandidate, TextSnapshot>> sharedB,
            bool sharedX, bool vertical, double spacing, double scale, bool comparable)
        {
            foreach (var first in uniqueA)
            foreach (var second in uniqueB)
            {
                if (first.Item1.SourceHandle == second.Item1.SourceHandle ||
                    !NearTag(a.Tag, first.Item2, scale) || !NearTag(b.Tag, second.Item2, scale) ||
                    !SameOrientation(a.Tag.Rotation, first.Item2.Rotation) ||
                    !SameOrientation(b.Tag.Rotation, second.Item2.Rotation) ||
                    !CompatibleHeight(first.Item2.Height, second.Item2.Height)) continue;
                double tagDx = b.Tag.PositionX - a.Tag.PositionX, tagDy = b.Tag.PositionY - a.Tag.PositionY;
                double labelDx = second.Item2.PositionX - first.Item2.PositionX;
                double labelDy = second.Item2.PositionY - first.Item2.PositionY;
                if (Distance(tagDx, tagDy, labelDx, labelDy) > scale * 0.35 ||
                    Math.Abs(Math.Abs((double)(second.Item1.Value - first.Item1.Value)) - spacing) > scale * 0.35) continue;
                foreach (var common in sharedA.Join(sharedB, x => x.Item1.SourceHandle,
                    x => x.Item1.SourceHandle, (x, y) => x, StringComparer.Ordinal))
                {
                    if (common.Item1.SourceHandle == first.Item1.SourceHandle ||
                        common.Item1.SourceHandle == second.Item1.SourceHandle ||
                        !SameOrientation(a.Tag.Rotation, common.Item2.Rotation) ||
                        !CompatibleHeight(first.Item2.Height, common.Item2.Height) ||
                        !GroupPeriphery(a.Tag, b.Tag, common.Item2, vertical, spacing, scale)) continue;
                    string evidence = "Two aligned EQ-TAG occurrences; distinct EQ-ID " + (sharedX ? "Y" : "X") +
                        " labels repeat tag-to-label displacement and coordinate step; one group-level EQ-ID " +
                        (sharedX ? "X" : "Y") + " label at bounded group periphery. Inferred intersection; engineer review required.";
                    var group = new SharedGroup { SharedHandle = common.Item1.SourceHandle };
                    group.First = MakeCandidate(a.Placement, sharedX ? common.Item1 : first.Item1,
                        sharedX ? first.Item1 : common.Item1, sharedX, comparable, evidence);
                    group.Second = MakeCandidate(b.Placement, sharedX ? common.Item1 : second.Item1,
                        sharedX ? second.Item1 : common.Item1, sharedX, comparable, evidence);
                    result.Add(group);
                }
            }
        }

        private static DrawingPlacementCandidate MakeCandidate(EquipmentPlacement placement, CoordinateCandidate x,
            CoordinateCandidate y, bool sharedX, bool comparable, string evidence) => new DrawingPlacementCandidate
            {
                OccurrenceId = placement.PlacementId, EquipmentTag = placement.NormalizedTag,
                CandidateX = x.Value, CandidateY = y.Value, TagEntityHandle = placement.TagSourceHandle,
                XEntityHandle = x.SourceHandle, YEntityHandle = y.SourceHandle,
                EvidenceType = sharedX ? "SHARED_X_ORTHOGONAL_GRAPH" : "SHARED_Y_ORTHOGONAL_GRAPH",
                CoordinatesInDrawingUnits = comparable, EvidenceSummary = evidence
            };

        private static bool NearTag(TextSnapshot tag, TextSnapshot label, double scale) =>
            Distance(tag.PositionX, tag.PositionY, label.PositionX, label.PositionY) <= scale * 2 &&
            (Math.Abs(tag.PositionX - label.PositionX) <= scale || Math.Abs(tag.PositionY - label.PositionY) <= scale);

        private static bool GroupPeriphery(TextSnapshot a, TextSnapshot b, TextSnapshot label,
            bool vertical, double spacing, double scale)
        {
            double along = vertical ? label.PositionY : label.PositionX;
            double cross = vertical ? label.PositionX : label.PositionY;
            double min = Math.Min(vertical ? a.PositionY : a.PositionX, vertical ? b.PositionY : b.PositionX);
            double max = Math.Max(vertical ? a.PositionY : a.PositionX, vertical ? b.PositionY : b.PositionX);
            double centerCross = vertical ? (a.PositionX + b.PositionX) / 2 : (a.PositionY + b.PositionY) / 2;
            return along >= min - spacing * 1.35 && along <= max + spacing * 1.35 &&
                Math.Abs(cross - centerCross) <= Math.Max(scale * 2, spacing * 5);
        }

        private static List<Tuple<CoordinateCandidate, TextSnapshot>> ValidLabels(
            IEnumerable<CoordinateCandidate> candidates, Dictionary<string, TextSnapshot> byHandle, Regex pattern)
        {
            return candidates.Where(x => x != null && !x.DeterministicallyOwned
                    && string.Equals(x.SourceLayer, "EQ-ID", StringComparison.OrdinalIgnoreCase)
                    && pattern.IsMatch(x.RawText ?? string.Empty)
                    && Finite(x.PositionX) && Finite(x.PositionY)
                    && byHandle.ContainsKey(x.SourceHandle)
                    && string.Equals(byHandle[x.SourceHandle].SpaceKind, "Model", StringComparison.OrdinalIgnoreCase))
                .Select(x => Tuple.Create(x, byHandle[x.SourceHandle]))
                .OrderBy(x => x.Item1.SourceHandle, StringComparer.Ordinal).ToList();
        }

        private static bool CompatibleHeight(double x, double y) => Finite(x) && Finite(y) && x > 0 && y > 0 &&
            Math.Max(x, y) <= Math.Min(x, y) * 1.5;
        private static bool SameOrientation(double x, double y)
        {
            if (!Finite(x) || !Finite(y)) return false;
            double difference = Math.Abs((x - y) % (2 * Math.PI));
            return Math.Min(difference, 2 * Math.PI - difference) <= 0.02;
        }
        private static double Distance(double ax, double ay, double bx, double by)
        {
            double dx = ax - bx, dy = ay - by;
            return Math.Sqrt(dx * dx + dy * dy);
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

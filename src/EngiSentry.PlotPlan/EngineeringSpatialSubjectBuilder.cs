using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EngiSentry.Contracts;

namespace EngiSentry.PlotPlan
{
    public static class EngineeringSpatialSubjectBuilder
    {
        public static IReadOnlyList<EngineeringSpatialSubject> Build(EngineeringDrawingModel model, PlotGeometryModel geometry)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));

            var candidates = new List<Candidate>();
            foreach (BoundaryEvidence boundary in model.BoundaryLabels.Where(x => x != null && string.Equals(x.BoundaryType, "Fence", StringComparison.Ordinal)))
                AddCandidate(candidates, geometry, boundary.SourceHandle, "Boundary", boundary.SourceHandle, EngineeringDistanceEvidenceKeys.SecurityFence);

            foreach (BoundaryEvidence boundary in model.BoundaryLabels.Where(x => x != null && string.Equals(x.BoundaryType, "UnitLimit", StringComparison.Ordinal)))
                AddCandidate(candidates, geometry, boundary.SourceHandle, "Boundary", boundary.SourceHandle, EngineeringDistanceEvidenceKeys.UnitBoundary);

            foreach (TextEvidence road in model.RoadLabels.Where(x => x != null && IsExplicitRoadEdge(x.RawText)))
                AddCandidate(candidates, geometry, road.SourceHandle, "Road", road.SourceHandle, EngineeringDistanceEvidenceKeys.AccessRoadEdge);

            var result = new List<EngineeringSpatialSubject>();
            foreach (IGrouping<string, Candidate> group in candidates.GroupBy(x => x.Basis + "\u001f" + x.Primitive.SourceHandle, StringComparer.Ordinal))
            {
                Candidate first = group.First();
                result.Add(new EngineeringSpatialSubject
                {
                    SubjectId = "engineering-spatial-subject:" + first.Basis.ToLowerInvariant() + ":" + first.Primitive.SourceHandle,
                    SubjectKind = first.SubjectKind,
                    EngineeringMeasurementBasisKey = first.Basis,
                    GeometrySourceHandle = first.Primitive.SourceHandle,
                    GeometrySourceLayer = first.Primitive.SourceLayer,
                    GeometryKind = first.Primitive.GeometryKind,
                    AssociationIds = group.Select(x => x.Association.AssociationId).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    LabelSourceHandles = group.Select(x => x.LabelSourceHandle).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList()
                });
            }

            foreach (IGrouping<string, EngineeringSpatialSubject> handleGroup in result.GroupBy(x => x.GeometrySourceHandle, StringComparer.Ordinal))
                if (handleGroup.Select(x => x.EngineeringMeasurementBasisKey).Distinct(StringComparer.Ordinal).Count() != 1)
                    throw new InvalidOperationException("Geometry handle has incompatible engineering measurement bases: " + handleGroup.Key);

            return result.OrderBy(x => x.SubjectId, StringComparer.Ordinal).ToList();
        }

        private static void AddCandidate(List<Candidate> candidates, PlotGeometryModel geometry, string? evidenceHandle, string subjectKind, string? labelHandle, string basis)
        {
            if (string.IsNullOrEmpty(evidenceHandle)) return;
            List<GeometryAssociation> associations = geometry.Associations.Where(x => x != null && string.Equals(x.LabelSourceHandle, evidenceHandle, StringComparison.Ordinal) && string.Equals(x.SubjectKind, subjectKind, StringComparison.Ordinal)).ToList();
            if (associations.Count == 0) return;
            if (associations.Count != 1) throw new InvalidOperationException("Semantic geometry evidence maps to multiple associations: " + evidenceHandle);
            GeometryAssociation association = associations[0];
            if (!string.Equals(association.Status, "Resolved", StringComparison.Ordinal)) return;
            if (string.IsNullOrEmpty(association.AssociationId) || string.IsNullOrEmpty(association.SelectedGeometryHandle)) throw new InvalidOperationException("Resolved geometry association is missing identity: " + evidenceHandle);
            List<PlotGeometryPrimitive> primitives = geometry.Primitives.Where(x => x != null && string.Equals(x.SourceHandle, association.SelectedGeometryHandle, StringComparison.Ordinal)).ToList();
            if (primitives.Count != 1) throw new InvalidOperationException("Resolved geometry association cannot resolve exactly one primitive: " + association.SelectedGeometryHandle);
            ValidatePrimitive(primitives[0]);
            candidates.Add(new Candidate(subjectKind, basis, labelHandle ?? string.Empty, association, primitives[0]));
        }

        private static bool IsExplicitRoadEdge(string? rawText)
        {
            if (rawText == null) return false;
            var normalized = new StringBuilder();
            bool separator = false;
            foreach (char character in rawText.Trim().ToUpperInvariant())
            {
                if ((character >= 'A' && character <= 'Z') || (character >= '0' && character <= '9')) { if (separator && normalized.Length > 0) normalized.Append(' '); normalized.Append(character); separator = false; }
                else separator = true;
            }
            string value = normalized.ToString().Trim();
            return value == "ACCESS ROAD EDGE" || value == "ROAD EDGE" || value == "EDGE OF ROAD" || value == "EDGE OF ACCESS ROAD";
        }

        internal static void ValidatePrimitive(PlotGeometryPrimitive primitive)
        {
            if (primitive == null || string.IsNullOrEmpty(primitive.SourceHandle) || string.IsNullOrEmpty(primitive.SourceLayer) || string.IsNullOrEmpty(primitive.GeometryKind) || primitive.Vertices == null || primitive.Vertices.Count < 2)
                throw new InvalidOperationException("Qualified engineering geometry primitive is invalid.");
            if (primitive.Vertices.Any(x => x == null || !Finite(x.X) || !Finite(x.Y) || !Finite(x.Z))) throw new InvalidOperationException("Qualified engineering geometry primitive contains non-finite vertices: " + primitive.SourceHandle);
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private sealed class Candidate
        {
            internal Candidate(string subjectKind, string basis, string labelSourceHandle, GeometryAssociation association, PlotGeometryPrimitive primitive) { SubjectKind = subjectKind; Basis = basis; LabelSourceHandle = labelSourceHandle; Association = association; Primitive = primitive; }
            internal string SubjectKind { get; } internal string Basis { get; } internal string LabelSourceHandle { get; } internal GeometryAssociation Association { get; } internal PlotGeometryPrimitive Primitive { get; }
        }
    }
}

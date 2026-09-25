using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.PlotPlan
{
    /// <summary>
    /// Builds <see cref="PlantModelV1"/> from existing accepted host-independent Plot Plan contracts only.
    /// It never parses drawing entities again, never duplicates <c>PlotPlanBuilder</c>, and never creates a
    /// second extraction pipeline: the flow is existing accepted extraction → existing accepted PlotPlan
    /// representation/evidence → this builder → PlantModelV1.
    ///
    /// Equipment location is a resolved coordinate point only, taken from the existing resolved coordinate
    /// evidence exactly as the accepted pipeline already selects it. No footprint, orientation, block
    /// insertion point, block extents, centroid, or text bounding box is ever derived here. Road / Plant
    /// Fence / Unit Boundary elements reuse the existing accepted reference geometry and evidence without
    /// reinterpretation, and are never collapsed into anonymous polylines.
    ///
    /// Classification is evidence-backed and deterministic: where existing evidence does not prove a
    /// classification, that fact is represented explicitly through the approved confidence/diagnostic model
    /// and ambiguity is never promoted to high confidence.
    /// </summary>
    public static class FacilityElementBuilder
    {
        /// <summary>Existing accepted placement status vocabulary.</summary>
        private const string PlacementStatusComplete = "Complete";
        private const string PlacementStatusAmbiguous = "Ambiguous";
        private const string PlacementStatusIncomplete = "Incomplete";

        /// <summary>Existing accepted association status vocabulary.</summary>
        private const string AssociationStatusResolved = "Resolved";
        private const string AssociationStatusAmbiguous = "Ambiguous";
        private const string AssociationStatusMissing = "Missing";

        /// <summary>Existing accepted subject kinds produced by EngineeringSpatialSubjectBuilder.</summary>
        private const string SubjectKindRoad = "Road";
        private const string SubjectKindBoundary = "Boundary";

        /// <summary>
        /// Projects an accepted <see cref="PlotPlanModel"/> into a bounded PlantModel v1. The source model
        /// is held by reference and is never mutated; a null source yields an empty model with diagnostics.
        /// </summary>
        public static PlantModelV1 Build(PlotPlanModel? source)
        {
            PlotGeometryModel geometry = source?.Geometry ?? new PlotGeometryModel();
            List<EquipmentPlacement> placements = source?.EquipmentPlacements ?? new List<EquipmentPlacement>();

            var elements = new List<FacilityElement>();
            var reasons = new List<string>();

            BuildEquipment(placements, elements, reasons);
            BuildReferenceGeometrySubjects(geometry, elements, reasons);

            elements.Sort(CompareFacilityElement);

            return new PlantModelV1
            {
                Source = source,
                FacilityElements = elements,
                Diagnostics = BuildDiagnostics(source, elements, reasons)
            };
        }

        /// <summary>Deterministic ordering: kind (ordinal), then ElementId (ordinal). Same input always
        /// yields the same element sequence.</summary>
        private static int CompareFacilityElement(FacilityElement a, FacilityElement b)
        {
            int kindCompare = string.Compare(a.FacilityElementKind, b.FacilityElementKind, StringComparison.Ordinal);
            if (kindCompare != 0) return kindCompare;
            return string.Compare(a.ElementId, b.ElementId, StringComparison.Ordinal);
        }

        private static void BuildEquipment(List<EquipmentPlacement> placements, List<FacilityElement> elements, List<string> reasons)
        {
            foreach (EquipmentPlacement placement in placements.OrderBy(x => x.PlacementId, StringComparer.Ordinal))
            {
                if (placement == null) continue;

                string confidence;
                string basis;
                double? positionX = null;
                double? positionY = null;

                bool eastingResolved = placement.Easting != null && string.Equals(placement.Easting.Status, AssociationStatusResolved, StringComparison.Ordinal) && placement.Easting.SelectedValue.HasValue;
                bool northingResolved = placement.Northing != null && string.Equals(placement.Northing.Status, AssociationStatusResolved, StringComparison.Ordinal) && placement.Northing.SelectedValue.HasValue;

                if (string.Equals(placement.PlacementStatus, PlacementStatusComplete, StringComparison.Ordinal) && eastingResolved && northingResolved)
                {
                    // Only the exact evidence the accepted pipeline already uses: the resolved selected
                    // coordinate values. Never a block insertion point, extents, centroid or text box.
                    confidence = ClassificationConfidence.High;
                    basis = "PlacementStatus=Complete with E/N Resolved selected values (existing accepted evidence)";
                    positionX = (double)placement.Easting!.SelectedValue!.Value;
                    positionY = (double)placement.Northing!.SelectedValue!.Value;
                }
                else if (string.Equals(placement.PlacementStatus, PlacementStatusAmbiguous, StringComparison.Ordinal)
                         || (placement.Easting != null && string.Equals(placement.Easting.Status, AssociationStatusAmbiguous, StringComparison.Ordinal))
                         || (placement.Northing != null && string.Equals(placement.Northing.Status, AssociationStatusAmbiguous, StringComparison.Ordinal)))
                {
                    confidence = ClassificationConfidence.Low;
                    basis = "PlacementStatus=" + placement.PlacementStatus + " with ambiguous coordinate association (existing accepted evidence)";
                    reasons.Add("ambiguous-equipment-coordinate:" + placement.PlacementId + ": coordinate association is Ambiguous");
                }
                else
                {
                    confidence = ClassificationConfidence.None;
                    string eastingStatus = placement.Easting?.Status ?? "null";
                    string northingStatus = placement.Northing?.Status ?? "null";
                    string reason = "PlacementStatus=" + placement.PlacementStatus + " with Easting=" + eastingStatus + " Northing=" + northingStatus + " (existing accepted statuses)";
                    basis = reason;
                    reasons.Add("missing-equipment-coordinate:" + placement.PlacementId + ": " + reason);
                }

                var evidenceHandles = new List<string>();
                if (!string.IsNullOrEmpty(placement.TagSourceHandle)) evidenceHandles.Add(placement.TagSourceHandle);
                if (placement.Easting?.SelectedSourceHandle != null) evidenceHandles.Add(placement.Easting.SelectedSourceHandle);
                if (placement.Northing?.SelectedSourceHandle != null) evidenceHandles.Add(placement.Northing.SelectedSourceHandle);
                evidenceHandles = evidenceHandles.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

                elements.Add(new FacilityElement
                {
                    ElementId = "facility-element:equipment:placement:" + placement.PlacementId,
                    FacilityElementKind = FacilityElementKind.Equipment,
                    SourceHandle = placement.TagSourceHandle,
                    SourceLayer = placement.TagSourceLayer,
                    Classification = FacilityElementKind.Equipment,
                    ClassificationConfidence = confidence,
                    ClassificationBasis = basis,
                    EvidenceHandles = evidenceHandles,
                    PositionX = positionX,
                    PositionY = positionY,
                    EngineeringApplicabilityKey = placement.EngineeringApplicabilityKey,
                    ResolutionStatus = placement.PlacementStatus,
                    ResolutionReason = null
                });
            }
        }

        private static void BuildReferenceGeometrySubjects(PlotGeometryModel geometry, List<FacilityElement> elements, List<string> reasons)
        {
            List<EngineeringSpatialSubject> subjects = (geometry.EngineeringSpatialSubjects ?? new List<EngineeringSpatialSubject>())
                .OrderBy(x => x.SubjectId, StringComparer.Ordinal).ToList();

            foreach (EngineeringSpatialSubject subject in subjects)
            {
                if (subject == null) continue;

                string? kind = TryGetFacilityElementKind(subject.SubjectKind, subject.EngineeringMeasurementBasisKey);
                if (kind == null)
                {
                    // Deliberate, visible exclusion: no basis key means no engineering measurement basis, so
                    // this subject is not a PlantModel v1 facility element (SiteBoundary / BatteryLimit).
                    reasons.Add("unsupported-facility-subject:" + subject.SubjectId + ": subject kind '" + subject.SubjectKind + "' with basis '" + subject.EngineeringMeasurementBasisKey + "' is outside the bounded PlantModel v1 taxonomy");
                    continue;
                }

                PlotGeometryPrimitive? primitive = TryResolvePrimitive(geometry, subject);
                if (primitive == null)
                {
                    // The existing ResolvePrimitive invariant requires exactly one primitive; when that
                    // cannot be satisfied the element is reported rather than guessed.
                    elements.Add(new FacilityElement
                    {
                        ElementId = "facility-element:" + kind.ToLowerInvariant() + ":" + subject.SubjectId,
                        FacilityElementKind = kind,
                        SourceHandle = subject.GeometrySourceHandle,
                        SourceLayer = subject.GeometrySourceLayer,
                        Classification = kind,
                        ClassificationConfidence = ClassificationConfidence.None,
                        ClassificationBasis = "reference geometry could not be resolved to exactly one primitive (existing accepted invariant)",
                        EvidenceHandles = LabelHandles(subject),
                        GeometryKind = subject.GeometryKind,
                        GeometrySourceHandle = subject.GeometrySourceHandle,
                        GeometrySourceLayer = subject.GeometrySourceLayer,
                        EngineeringMeasurementBasisKey = subject.EngineeringMeasurementBasisKey,
                        ResolutionStatus = AssociationStatusMissing,
                        ResolutionReason = "reference geometry unresolved"
                    });
                    reasons.Add("missing-reference-geometry:" + subject.SubjectId + ": cannot resolve exactly one primitive for handle " + subject.GeometrySourceHandle);
                    continue;
                }

                string confidence = ClassifyReferenceSubject(geometry, subject, primitive, out string basis);

                elements.Add(new FacilityElement
                {
                    ElementId = "facility-element:" + kind.ToLowerInvariant() + ":" + subject.SubjectId,
                    FacilityElementKind = kind,
                    SourceHandle = subject.GeometrySourceHandle,
                    SourceLayer = subject.GeometrySourceLayer,
                    Classification = kind,
                    ClassificationConfidence = confidence,
                    ClassificationBasis = basis,
                    EvidenceHandles = LabelHandles(subject),
                    GeometryKind = primitive.GeometryKind,
                    GeometrySourceHandle = primitive.SourceHandle,
                    GeometrySourceLayer = primitive.SourceLayer,
                    Vertices = new List<PlotGeometryVertex>(primitive.Vertices),
                    EngineeringMeasurementBasisKey = subject.EngineeringMeasurementBasisKey,
                    ResolutionStatus = AssociationStatusResolved,
                    ResolutionReason = null
                });
            }
        }

        /// <summary>Portable label evidence handles for a subject, never null, deduplicated and Ordinal-ordered.</summary>
        private static List<string> LabelHandles(EngineeringSpatialSubject subject)
        {
            if (subject.EvidenceSourceKind == "ModelLayer") return new List<string> { subject.GeometrySourceHandle };
            return (subject.LabelSourceHandles ?? new List<string>())
                .Where(x => !string.IsNullOrEmpty(x))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Maps an existing accepted subject kind + measurement basis onto a PlantModel v1 facility kind.
        /// Returns null for anything outside the bounded taxonomy, so unsupported subjects are visible in
        /// diagnostics rather than silently guessed.
        /// </summary>
        private static string? TryGetFacilityElementKind(string subjectKind, string basisKey)
        {
            if (string.Equals(subjectKind, SubjectKindRoad, StringComparison.Ordinal)
                && (basisKey == EngineeringDistanceEvidenceKeys.AccessRoadEdge || basisKey == EngineeringDistanceEvidenceKeys.UnitPeripheralRoadEdge))
                return FacilityElementKind.Road;

            if (string.Equals(subjectKind, SubjectKindBoundary, StringComparison.Ordinal))
            {
                if (basisKey == EngineeringDistanceEvidenceKeys.SecurityFence) return FacilityElementKind.PlantFence;
                if (basisKey == EngineeringDistanceEvidenceKeys.UnitBoundary) return FacilityElementKind.UnitBoundary;
            }

            return null;
        }

        /// <summary>
        /// Resolves the subject's reference geometry using the same exactly-one-primitive invariant the
        /// accepted pipeline enforces, but reports failure instead of throwing so extraction stays
        /// non-fatal and diagnosable.
        /// </summary>
        private static PlotGeometryPrimitive? TryResolvePrimitive(PlotGeometryModel geometry, EngineeringSpatialSubject subject)
        {
            if (string.IsNullOrEmpty(subject.SubjectId) || string.IsNullOrEmpty(subject.GeometrySourceHandle)
                || string.IsNullOrEmpty(subject.GeometrySourceLayer) || string.IsNullOrEmpty(subject.GeometryKind)
                || subject.LabelSourceHandles == null || (subject.LabelSourceHandles.Count == 0 && subject.EvidenceSourceKind != "ModelLayer"))
                return null;

            List<PlotGeometryPrimitive> primitives = (geometry.Primitives ?? new List<PlotGeometryPrimitive>())
                .Where(x => x != null && string.Equals(x.SourceHandle, subject.GeometrySourceHandle, StringComparison.Ordinal)).ToList();
            if (primitives.Count != 1) return null;

            PlotGeometryPrimitive primitive = primitives[0];
            if (primitive.Vertices == null || primitive.Vertices.Count < 2) return null;
            return primitive;
        }

        /// <summary>
        /// Classifies a reference-geometry subject from existing evidence. HIGH when the accepted association
        /// resolved on a same-layer match (EvidenceTier 0); MEDIUM when it resolved via the accepted
        /// cross-tier compatibility heuristic (EvidenceTier 1); LOW when the association is ambiguous.
        /// </summary>
        private static string ClassifyReferenceSubject(PlotGeometryModel geometry, EngineeringSpatialSubject subject, PlotGeometryPrimitive primitive, out string basis)
        {
            if (subject.EvidenceSourceKind == "ModelLayer")
            {
                basis = "Explicit Model-space layer " + primitive.SourceLayer + " on geometry handle " + primitive.SourceHandle;
                return ClassificationConfidence.Medium;
            }
            List<GeometryAssociation> associations = (geometry.Associations ?? new List<GeometryAssociation>())
                .Where(x => x != null && subject.AssociationIds != null && subject.AssociationIds.Contains(x.AssociationId, StringComparer.Ordinal))
                .ToList();

            GeometryAssociation? ambiguous = associations.FirstOrDefault(x => string.Equals(x.Status, AssociationStatusAmbiguous, StringComparison.Ordinal));
            if (ambiguous != null)
            {
                basis = "GeometryAssociation " + ambiguous.AssociationId + " is Ambiguous with " + ambiguous.Candidates.Count + " retained candidates (existing accepted evidence)";
                return ClassificationConfidence.Low;
            }

            GeometryAssociation? resolved = associations.FirstOrDefault(x => string.Equals(x.Status, AssociationStatusResolved, StringComparison.Ordinal));
            if (resolved != null)
            {
                // Same-layer match is the accepted EvidenceTier 0 (strongest); cross-tier is tier 1.
                bool sameLayer = !string.IsNullOrEmpty(resolved.LabelSourceLayer) && string.Equals(resolved.LabelSourceLayer, primitive.SourceLayer, StringComparison.OrdinalIgnoreCase);
                if (sameLayer)
                {
                    basis = "GeometryAssociation " + resolved.AssociationId + " Resolved on same-layer geometry (existing accepted EvidenceTier 0)";
                    return ClassificationConfidence.High;
                }
                basis = "GeometryAssociation " + resolved.AssociationId + " Resolved via accepted cross-layer compatibility (existing accepted EvidenceTier 1)";
                return ClassificationConfidence.Medium;
            }

            basis = "GeometryAssociation unresolved for subject " + subject.SubjectId;
            return ClassificationConfidence.None;
        }

        private static PlantModelDiagnostics BuildDiagnostics(PlotPlanModel? source, List<FacilityElement> elements, List<string> reasons)
        {
            int ByKind(string kind) => elements.Count(x => string.Equals(x.FacilityElementKind, kind, StringComparison.Ordinal));
            int ByConfidence(string confidence) => elements.Count(x => string.Equals(x.ClassificationConfidence, confidence, StringComparison.Ordinal));

            return new PlantModelDiagnostics
            {
                EquipmentElementCount = ByKind(FacilityElementKind.Equipment),
                RoadElementCount = ByKind(FacilityElementKind.Road),
                PlantFenceElementCount = ByKind(FacilityElementKind.PlantFence),
                UnitBoundaryElementCount = ByKind(FacilityElementKind.UnitBoundary),
                UnsupportedElementCount = reasons.Count(x => x.StartsWith("unsupported-facility-subject:", StringComparison.Ordinal)),
                HighConfidenceCount = ByConfidence(ClassificationConfidence.High),
                MediumConfidenceCount = ByConfidence(ClassificationConfidence.Medium),
                LowConfidenceCount = ByConfidence(ClassificationConfidence.Low),
                NoneConfidenceCount = ByConfidence(ClassificationConfidence.None),
                EquipmentWithUnresolvedCoordinateCount = elements.Count(x => string.Equals(x.FacilityElementKind, FacilityElementKind.Equipment, StringComparison.Ordinal)
                    && (!x.PositionX.HasValue || !x.PositionY.HasValue)),
                ReferenceGeometryUnresolvedCount = reasons.Count(x => x.StartsWith("missing-reference-geometry:", StringComparison.Ordinal)),
                AmbiguousClassificationCount = reasons.Count(x => x.StartsWith("ambiguous-equipment-coordinate:", StringComparison.Ordinal)),
                ResolvedMeasurementEvidenceCount = source?.Measurements?.Statistics?.EquipmentSpatialMeasurementCount ?? 0,
                DiagnosticReasons = reasons.OrderBy(x => x, StringComparer.Ordinal).ToList()
            };
        }
    }
}

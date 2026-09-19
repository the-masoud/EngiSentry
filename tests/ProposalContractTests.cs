using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EngiSentry.Contracts;
using EngiSentry.PlotPlan;

namespace EngiSentry.Tests
{
    /// <summary>
    /// Phase B tests: host-independent PlantModel extraction contracts, pure point-to-polyline geometry,
    /// and non-regression safety. All fixtures are deterministic synthetic Plot Plan models; no AutoCAD
    /// runtime is required. Follows the existing harness convention (internal static Run + Assert).
    /// </summary>
    internal static class ProposalContractTests
    {
        private const string PlacementStatusComplete = "Complete";
        private const string PlacementStatusAmbiguous = "Ambiguous";
        private const string AssociationStatusResolved = "Resolved";
        private const string AssociationStatusAmbiguous = "Ambiguous";

        internal static void Run()
        {
            ContractSafety();
            PlantModelExtraction();
            GeometrySemantics();
            NonRegressionSafety();
        }

        // ---- 1. Contract safety -----------------------------------------------------------------------------

        private static void ContractSafety()
        {
            // The design-engine boundary: neither the Contracts assembly nor the PlotPlan assembly may
            // reference any Autodesk / host assembly.
            Assert(NoHostReference(typeof(FacilityElement).Assembly), "EngiSentry.Contracts references a host/Autodesk assembly.");
            Assert(NoHostReference(typeof(PointToPolylineDistance).Assembly), "EngiSentry.PlotPlan references a host/Autodesk assembly.");

            // New contract types carry no host-typed members at all.
            Assert(NoHostTypedMembers(typeof(FacilityElement)), "FacilityElement exposes a host-typed member.");
            Assert(NoHostTypedMembers(typeof(PlantModelV1)), "PlantModelV1 exposes a host-typed member.");
            Assert(NoHostTypedMembers(typeof(PlacementProposalCandidate)), "PlacementProposalCandidate exposes a host-typed member.");
            Assert(NoHostTypedMembers(typeof(StandardsPackRule)), "StandardsPackRule exposes a host-typed member.");

            // Deterministic initialization: defaults are populated and never null.
            var freshModel = new PlantModelV1();
            Assert(freshModel.FacilityElements != null && freshModel.FacilityElements.Count == 0, "PlantModelV1 elements were not initialized empty.");
            Assert(freshModel.Diagnostics != null, "PlantModelDiagnostics was not initialized.");

            var freshCandidate = new PlacementProposalCandidate();
            Assert(freshCandidate.EngineeringReviewRequired, "A fresh candidate must require engineering review.");
            Assert(freshCandidate.CheckedConstraints != null && freshCandidate.UncheckedConstraints != null, "Candidate constraint lists were not initialized.");

            // Terminology separation: PlacementProposalCandidate is a proposed POSITION and must remain
            // distinct from EngineeringDistanceCandidateDefinition, a proposed CRITERION awaiting approval.
            Assert(typeof(PlacementProposalCandidate) != typeof(EngineeringDistanceCandidateDefinition), "Spatial proposal candidate collided with the engineering distance candidate criterion type.");
            Assert(typeof(PlacementProposalCandidate).GetProperty(nameof(PlacementProposalCandidate.ProposedEasting)) != null, "PlacementProposalCandidate lacks proposed position evidence.");
            Assert(typeof(EngineeringDistanceCandidateDefinition).GetProperty(nameof(EngineeringDistanceCandidateDefinition.ProposedMinimumDistanceMeters)) != null, "EngineeringDistanceCandidateDefinition lost its criterion semantics.");
            Assert(typeof(PlacementProposalCandidate).GetProperty("ProposedMinimumDistanceMeters") == null, "Spatial proposal candidate wrongly reuses criterion semantics.");
            Assert(typeof(EngineeringDistanceCandidateDefinition).GetProperty("ProposedEasting") == null, "Candidate criterion wrongly carries position semantics.");

            // No executable invented standard data: an unpopulated StandardsPackRule cannot execute and
            // cannot present itself as an engineering verdict.
            var unpopulated = new StandardsPackRule();
            Assert(string.IsNullOrEmpty(unpopulated.RuleId), "An unpopulated rule must not claim a RuleId.");
            Assert(unpopulated.MinimumDistanceMeters == 0m, "An unpopulated rule must not carry a distance.");
            Assert(!unpopulated.AutoExecutionAllowed, "An unpopulated rule must not be auto-executable.");
            Assert(unpopulated.EngineeringReviewRequired, "An unpopulated rule must require engineering review.");
            Assert(unpopulated.Clauses != null && unpopulated.Clauses.Count == 0, "An unpopulated rule must cite no clauses.");
            Assert(string.IsNullOrEmpty(unpopulated.RequirementText), "An unpopulated rule must not carry requirement text.");

            // Mandatory v1 lists are non-empty by construction.
            Assert(ProposalAssumptions.Defaults.Length > 0, "Default assumptions list is empty.");
            Assert(ProposalUncheckedConstraints.Defaults.Length > 0, "Default unchecked-constraints list is empty.");
            Assert(ProposalKnownLimitations.Defaults.Length > 0, "Default known-limitations list is empty.");
            Assert(ProposalKnownLimitations.Defaults.Contains(ProposalKnownLimitations.PointBasedLimitation), "The point-based limitation is not carried by default.");
        }

        private static bool NoHostReference(Assembly assembly)
        {
            AssemblyName[] references = assembly.GetReferencedAssemblies();
            foreach (AssemblyName reference in references)
            {
                string name = reference.Name ?? string.Empty;
                if (name.StartsWith("Autodesk", StringComparison.OrdinalIgnoreCase) || name.IndexOf("acdbmgd", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("acmgd", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }
            return true;
        }

        private static bool NoHostTypedMembers(Type type)
        {
            foreach (PropertyInfo property in type.GetProperties())
            {
                string propertyTypeName = property.PropertyType.Namespace ?? string.Empty;
                if (propertyTypeName.StartsWith("Autodesk", StringComparison.OrdinalIgnoreCase)) return false;
                string name = property.Name;
                if (name == "ObjectId" || name == "Document" || name == "Database" || name == "Transaction" || name == "Handle" || name == "Polyline") return false;
            }
            return true;
        }

        // ---- 2. PlantModel extraction --------------------------------------------------------------------------------

        private static void PlantModelExtraction()
        {
            PlotPlanModel source = BuildSyntheticPlotPlan();

            PlantModelV1 first = FacilityElementBuilder.Build(source);
            PlantModelV1 second = FacilityElementBuilder.Build(source);

            // Same input must yield the same result, in the same order.
            Assert(first.FacilityElements.Count == second.FacilityElements.Count, "PlantModel element count was not deterministic.");
            Assert(first.FacilityElements.Select(x => x.ElementId).SequenceEqual(second.FacilityElements.Select(x => x.ElementId)), "PlantModel element ordering was not deterministic.");

            // Equipment extraction from valid existing resolved coordinate evidence.
            FacilityElement equipment = first.FacilityElements.Single(x => x.ElementId == "facility-element:equipment:placement:placement:A");
            Assert(equipment.ClassificationConfidence == ClassificationConfidence.High, "Resolved equipment was not classified HIGH.");
            Assert(equipment.PositionX.HasValue && equipment.PositionY.HasValue, "Resolved equipment has no placement point.");
            Assert(equipment.PositionX!.Value == 100d && equipment.PositionY!.Value == 50d, "Equipment placement point does not match the existing resolved selected values.");
            Assert(equipment.SourceHandle == "TAG-H-A", "Equipment source handle was not preserved.");
            Assert(equipment.EvidenceHandles.Count > 0, "Equipment evidence handles were not preserved.");

            // Ambiguous equipment is retained but never promoted above LOW.
            FacilityElement ambiguous = first.FacilityElements.Single(x => x.ElementId == "facility-element:equipment:placement:placement:B");
            Assert(ambiguous.ClassificationConfidence == ClassificationConfidence.Low, "Ambiguous equipment was not classified LOW.");
            Assert(!ambiguous.PositionX.HasValue && !ambiguous.PositionY.HasValue, "An ambiguous placement must not expose a placement point.");

            // Road extraction.
            FacilityElement road = first.FacilityElements.Single(x => x.FacilityElementKind == FacilityElementKind.Road);
            Assert(road.GeometryKind == "Polyline", "Road geometry kind was not preserved.");
            Assert(road.Vertices != null && road.Vertices.Count == 3, "Road reference geometry vertices were not preserved.");
            Assert(road.EngineeringMeasurementBasisKey == EngineeringDistanceEvidenceKeys.AccessRoadEdge, "Road measurement basis was not preserved.");
            Assert(road.ClassificationConfidence == ClassificationConfidence.High, "Same-layer road was not classified HIGH.");

            // Plant Fence extraction, kept distinct from Unit Boundary.
            FacilityElement fence = first.FacilityElements.Single(x => x.FacilityElementKind == FacilityElementKind.PlantFence && x.ClassificationConfidence == ClassificationConfidence.Medium);
            Assert(fence.EngineeringMeasurementBasisKey == EngineeringDistanceEvidenceKeys.SecurityFence, "Plant fence measurement basis was not preserved.");
            Assert(fence.ClassificationConfidence == ClassificationConfidence.Medium, "Cross-layer fence should be classified MEDIUM.");

            // Unit Boundary extraction.
            FacilityElement unit = first.FacilityElements.Single(x => x.FacilityElementKind == FacilityElementKind.UnitBoundary);
            Assert(unit.EngineeringMeasurementBasisKey == EngineeringDistanceEvidenceKeys.UnitBoundary, "Unit boundary measurement basis was not preserved.");
            Assert(unit.ClassificationConfidence == ClassificationConfidence.High, "Same-layer unit boundary was not classified HIGH.");

            // The three reference-geometry kinds must never collapse into one another: exactly one
            // resolved element each for Road, Plant Fence and Unit Boundary.
            Assert(first.FacilityElements.Count(x => x.FacilityElementKind == FacilityElementKind.Road) == 1, "Exactly one road element was expected.");
            Assert(first.FacilityElements.Count(x => x.FacilityElementKind == FacilityElementKind.PlantFence && x.ClassificationConfidence != ClassificationConfidence.None) == 1, "Exactly one resolved plant fence element was expected.");
            Assert(first.FacilityElements.Count(x => x.FacilityElementKind == FacilityElementKind.UnitBoundary) == 1, "Exactly one unit boundary element was expected.");

            // The unresolved fence subject is retained at NONE with a recorded reason, never dropped.
            FacilityElement unresolvedFence = first.FacilityElements.Single(x => x.FacilityElementKind == FacilityElementKind.PlantFence && x.ClassificationConfidence == ClassificationConfidence.None);
            Assert(unresolvedFence.Vertices == null, "An unresolved subject must not carry reference geometry.");
            Assert(!string.IsNullOrEmpty(unresolvedFence.ResolutionReason), "An unresolved subject must record its reason.");

            // Diagnostics report unresolved / unsupported cases explicitly rather than dropping them.
            Assert(first.Diagnostics.DiagnosticReasons.Count > 0, "Diagnostics were not recorded for unresolved evidence.");
            Assert(first.Diagnostics.EquipmentWithUnresolvedCoordinateCount == 1, "The unresolved equipment placement was not counted.");
            Assert(first.Diagnostics.ReferenceGeometryUnresolvedCount == 1, "The unresolved reference geometry was not counted.");
            Assert(first.Diagnostics.AmbiguousClassificationCount == 1, "The ambiguous equipment placement was not counted.");

            // Unresolved equipment stays unresolved and is never promoted.
            PlantModelV1 unresolved = FacilityElementBuilder.Build(BuildUnresolvedPlotPlan());
            FacilityElement unresolvedEquipment = unresolved.FacilityElements.Single(x => x.FacilityElementKind == FacilityElementKind.Equipment);
            Assert(!unresolvedEquipment.PositionX.HasValue && !unresolvedEquipment.PositionY.HasValue, "An unresolved placement must not expose a placement point.");
            Assert(unresolvedEquipment.ClassificationConfidence == ClassificationConfidence.None, "An unresolved placement must not be promoted above NONE.");

            // A null source yields an empty model with diagnostics, never an exception.
            PlantModelV1 empty = FacilityElementBuilder.Build(null);
            Assert(empty.FacilityElements.Count == 0, "A null source must produce an empty model.");
            Assert(empty.Diagnostics != null, "A null source must still produce diagnostics.");
        }

        // ---- 3. Geometry semantics --------------------------------------------------------------------------------

        private static void GeometrySemantics()
        {
            // Perpendicular projection lands inside the segment.
            Assert(ApproxEqual(PointToPolylineDistance.PointToSegment(5, 2, 0, 0, 10, 0), 2d), "Perpendicular projection distance was wrong.");

            // Projection clamps to the nearer endpoint.
            Assert(ApproxEqual(PointToPolylineDistance.PointToSegment(12, 1, 0, 0, 10, 0), Math.Sqrt(5d)), "Closest-endpoint clamping was wrong.");

            // Multi-segment minimum selects the governing segment.
            var polyline = new PlotGeometryPrimitive { SourceHandle = "P1", SourceLayer = "ROAD", GeometryKind = "Polyline", Closed = false, Vertices = new List<PlotGeometryVertex> { V(0, 0), V(10, 0), V(10, 10) } };
            Assert(ApproxEqual(PointToPolylineDistance.PointToPolyline(5, 2, polyline), 2d), "Multi-segment minimum was wrong for the first segment.");
            // A point whose nearest geometry is the second segment proves the minimum spans all segments.
            Assert(ApproxEqual(PointToPolylineDistance.PointToPolyline(12, 5, polyline), 2d), "Multi-segment minimum did not select the governing segment.");
            PointToPolylineDistance.SegmentEvidence secondEvidence = PointToPolylineDistance.PointToPolylineEvidence(12, 5, polyline);
            Assert(secondEvidence.Index == 1, "Multi-segment minimum selected the wrong governing segment index.");

            // Closed polyline semantics: the closing segment is evaluated. (6,4) is nearest to the closing
            // segment from (10,10) back to (0,0), at distance sqrt(2).
            var closed = new PlotGeometryPrimitive { SourceHandle = "P2", SourceLayer = "FENCE", GeometryKind = "Polyline", Closed = true, Vertices = new List<PlotGeometryVertex> { V(0, 0), V(10, 0), V(10, 10) } };
            Assert(ApproxEqual(PointToPolylineDistance.PointToPolyline(6, 4, closed), Math.Sqrt(2d)), "Closed-polyline closing segment was not evaluated.");
            Assert(PointToPolylineDistance.PointToPolylineEvidence(6, 4, closed).Index == 2, "Closed-polyline governing segment index was wrong.");

            // Degenerate (zero-length) segments are skipped deterministically, not treated as hits.
            var withDegenerate = new PlotGeometryPrimitive { SourceHandle = "P3", SourceLayer = "ROAD", GeometryKind = "Polyline", Closed = false, Vertices = new List<PlotGeometryVertex> { V(4, 0), V(4, 0), V(4, 10) } };
            Assert(ApproxEqual(PointToPolylineDistance.PointToPolyline(0, 5, withDegenerate), 4d), "Degenerate segment handling was wrong.");

            // A primitive with only degenerate segments reports the invariant, matching accepted behaviour.
            bool threw = false;
            try { PointToPolylineDistance.PointToPolyline(0, 0, new PlotGeometryPrimitive { SourceHandle = "P4", SourceLayer = "ROAD", GeometryKind = "Polyline", Vertices = new List<PlotGeometryVertex> { V(1, 1), V(1, 1) } }); }
            catch (InvalidOperationException) { threw = true; }
            Assert(threw, "A primitive with no positive-length segment must report the accepted invariant.");

            // Equivalence with the accepted measurement pipeline: the helper must reproduce
            // PlotSpatialMeasurementBuilder.BuildEngineeringSpatial exactly, on the same evidence.
            PlotGeometryModel cleanGeometry = BuildBoundedGeometry();
            var placement = new EquipmentPlacement
            {
                PlacementId = "placement:probe",
                EquipmentEntityId = "equipment:probe",
                NormalizedTag = "PROBE",
                TagSourceHandle = "TAG-H-PROBE",
                PlacementStatus = PlacementStatusComplete,
                Easting = new CoordinateAssociation { Axis = "E", Status = AssociationStatusResolved, SelectedValue = 3m },
                Northing = new CoordinateAssociation { Axis = "N", Status = AssociationStatusResolved, SelectedValue = 7m }
            };
            PlotSpatialMeasurementModel accepted = PlotSpatialMeasurementBuilder.Build(new[] { placement }, cleanGeometry);
            List<EquipmentSpatialMeasurement> probeMeasurements = accepted.EquipmentSpatialMeasurements.Where(x => x.PlacementId == "placement:probe").ToList();
            Assert(probeMeasurements.Count == 3, "The accepted pipeline did not produce the expected bounded measurements.");
            foreach (EquipmentSpatialMeasurement measurement in probeMeasurements)
            {
                PlotGeometryPrimitive primitive = cleanGeometry.Primitives.Single(x => string.Equals(x.SourceHandle, measurement.GeometrySourceHandle, StringComparison.Ordinal));
                double computed = PointToPolylineDistance.PointToPolyline((double)measurement.EquipmentEasting, (double)measurement.EquipmentNorthing, primitive);
                Assert(ApproxEqual(computed, measurement.Distance2D), "Pure geometry does not match accepted measurement for " + measurement.GeometrySourceHandle + ".");
                PointToPolylineDistance.SegmentEvidence evidence = PointToPolylineDistance.PointToPolylineEvidence((double)measurement.EquipmentEasting, (double)measurement.EquipmentNorthing, primitive);
                Assert(evidence.Index == measurement.SegmentIndex, "Governing segment index does not match accepted evidence for " + measurement.GeometrySourceHandle + ".");
                Assert(ApproxEqual(evidence.ClosestPointX, measurement.ClosestPointX) && ApproxEqual(evidence.ClosestPointY, measurement.ClosestPointY), "Closest point does not match accepted evidence for " + measurement.GeometrySourceHandle + ".");
            }
        }

        // ---- 4. Non-regression safety ------------------------------------------------------------------------------

        private static void NonRegressionSafety()
        {
            PlotPlanModel plan = BuildSyntheticPlotPlan();
            int placementCountBefore = plan.EquipmentPlacements.Count;
            int measurementCountBefore = plan.Measurements.EquipmentSpatialMeasurements.Count;
            var snapshotEasting = plan.EquipmentPlacements.Select(x => x.Easting?.SelectedValue).ToList();

            PlantModelV1 model = FacilityElementBuilder.Build(plan);

            // The accepted source model is held by reference and never mutated.
            Assert(ReferenceEquals(model.Source, plan), "PlantModelV1 does not reference the accepted source model.");
            Assert(plan.EquipmentPlacements.Count == placementCountBefore, "Extraction mutated the source placements.");
            Assert(plan.Measurements.EquipmentSpatialMeasurements.Count == measurementCountBefore, "Extraction mutated the source measurements.");
            Assert(plan.EquipmentPlacements.Select(x => x.Easting?.SelectedValue).SequenceEqual(snapshotEasting), "Extraction mutated resolved coordinate evidence.");

            // Diagnostics are counts and reasons only: structurally incapable of carrying a PASS/FAIL or a
            // standards finding, so no new automatic verdict can be introduced.
            foreach (PropertyInfo property in typeof(PlantModelDiagnostics).GetProperties())
            {
                Type propertyType = property.PropertyType;
                Assert(propertyType == typeof(int) || propertyType == typeof(List<string>), "PlantModelDiagnostics must expose only counts and reasons: " + property.Name + ".");
            }

            // PlantModelV1 exposes only property accessors: no real methods, so no mutation or write
            // capability for drawings can be reached through it.
            Assert(typeof(PlantModelV1).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).All(x => x.IsSpecialName), "PlantModelV1 must not expose declared instance methods beyond property accessors.");
            Assert(typeof(PlantModelV1).GetFields(BindingFlags.Public | BindingFlags.Instance).Length == 0, "PlantModelV1 must not expose public fields.");

            // FacilityElementBuilder exposes exactly one public entry point, the read-only projection.
            MethodInfo[] builderMethods = typeof(FacilityElementBuilder).GetMethods(BindingFlags.Public | BindingFlags.Static);
            Assert(builderMethods.Length == 1 && builderMethods[0].Name == "Build", "FacilityElementBuilder must expose only the Build projection.");
        }

        // ---- Fixtures ----------------------------------------------------------------------------------------------

        private static PlotGeometryVertex V(double x, double y) => new PlotGeometryVertex { X = x, Y = y, Z = 0 };

        /// <summary>The bounded reference geometry (Road / Plant Fence / Unit Boundary) with only the
        /// resolved subjects the accepted pipeline produces. Safe to feed to the accepted measurement
        /// builder.</summary>
        private static PlotGeometryModel BuildBoundedGeometry()
        {
            var model = new EngineeringDrawingModel();
            model.RoadLabels.Add(new TextEvidence { SourceHandle = "R1", SourceLayer = "ROAD", RawText = "EDGE OF ROAD", PositionX = 5, PositionY = 2 });
            model.BoundaryLabels.Add(new BoundaryEvidence { SourceHandle = "F1", SourceLayer = "TEXT", RawText = "FENCE", BoundaryType = "Fence", PositionX = 5, PositionY = 2 });
            model.BoundaryLabels.Add(new BoundaryEvidence { SourceHandle = "U1", SourceLayer = "UNIT-LIMIT", RawText = "UNIT LIMIT", BoundaryType = "UnitLimit", PositionX = 5, PositionY = 12 });

            var capture = new PlotGeometryCapture();
            capture.ModelSpaceTextSources.Add(new PlotTextSourceRecord { SourceHandle = "R1", SourceLayer = "ROAD", PositionX = 5, PositionY = 2, Height = 1 });
            capture.ModelSpaceTextSources.Add(new PlotTextSourceRecord { SourceHandle = "F1", SourceLayer = "TEXT", PositionX = 5, PositionY = 2, Height = 1 });
            capture.ModelSpaceTextSources.Add(new PlotTextSourceRecord { SourceHandle = "U1", SourceLayer = "UNIT-LIMIT", PositionX = 5, PositionY = 12, Height = 1 });
            capture.Primitives.Add(new PlotGeometryPrimitive { SourceHandle = "G-ROAD", SourceLayer = "road", GeometryKind = "Polyline", Closed = true, Vertices = new List<PlotGeometryVertex> { V(0, 0), V(10, 0), V(10, 10) } });
            capture.Primitives.Add(new PlotGeometryPrimitive { SourceHandle = "G-FENCE", SourceLayer = "FENCE-GEOMETRY", GeometryKind = "Polyline", Vertices = new List<PlotGeometryVertex> { V(0, 0), V(10, 0) } });
            capture.Primitives.Add(new PlotGeometryPrimitive { SourceHandle = "G-UNIT", SourceLayer = "UNIT-LIMIT", GeometryKind = "Polyline", Vertices = new List<PlotGeometryVertex> { V(0, 10), V(10, 10) } });

            PlotGeometryModel geometry = PlotGeometryBuilder.Build(model, capture);
            geometry.EngineeringSpatialSubjects = EngineeringSpatialSubjectBuilder.Build(model, geometry).ToList();
            return geometry;
        }

        private static PlotPlanModel BuildSyntheticPlotPlan()
        {
            PlotGeometryModel geometry = BuildBoundedGeometry();

            var placements = new List<EquipmentPlacement>
            {
                // Resolved equipment: the only evidence the accepted pipeline uses.
                new EquipmentPlacement
                {
                    PlacementId = "placement:A",
                    EquipmentEntityId = "equipment:A",
                    NormalizedTag = "TAG-A",
                    TagSourceHandle = "TAG-H-A",
                    TagSourceLayer = "EQUIP",
                    PlacementStatus = PlacementStatusComplete,
                    Easting = new CoordinateAssociation { Axis = "E", Status = AssociationStatusResolved, SelectedValue = 100m, SelectedSourceHandle = "E-H-A", SelectedSourceLayer = "ELEV" },
                    Northing = new CoordinateAssociation { Axis = "N", Status = AssociationStatusResolved, SelectedValue = 50m, SelectedSourceHandle = "N-H-A", SelectedSourceLayer = "ELEV" }
                },
                // Ambiguous equipment: never promoted above LOW.
                new EquipmentPlacement
                {
                    PlacementId = "placement:B",
                    EquipmentEntityId = "equipment:B",
                    NormalizedTag = "TAG-B",
                    TagSourceHandle = "TAG-H-B",
                    PlacementStatus = PlacementStatusAmbiguous,
                    Easting = new CoordinateAssociation { Axis = "E", Status = AssociationStatusAmbiguous, SelectedValue = 1m },
                    Northing = new CoordinateAssociation { Axis = "N", Status = AssociationStatusResolved, SelectedValue = 2m }
                }
            };

            // Measurements are built from the clean, fully-resolved geometry only.
            PlotSpatialMeasurementModel measurements = PlotSpatialMeasurementBuilder.Build(placements, geometry);

            // Now inject an unresolved reference-geometry subject: valid identity, but its primitive is
            // absent, so the exactly-one invariant the accepted pipeline enforces cannot be satisfied.
            // FacilityElementBuilder must report it rather than guess or throw.
            geometry.EngineeringSpatialSubjects.Add(new EngineeringSpatialSubject
            {
                SubjectId = "engineering-spatial-subject:securityfence:G-MISSING",
                SubjectKind = "Boundary",
                EngineeringMeasurementBasisKey = EngineeringDistanceEvidenceKeys.SecurityFence,
                GeometrySourceHandle = "G-MISSING",
                GeometrySourceLayer = "FENCE",
                GeometryKind = "Polyline",
                AssociationIds = new List<string> { "geometry-association:boundary:F2" },
                LabelSourceHandles = new List<string> { "F2" }
            });

            return new PlotPlanModel
            {
                EquipmentPlacements = placements,
                Geometry = geometry,
                Measurements = measurements
            };
        }

        private static PlotPlanModel BuildUnresolvedPlotPlan()
        {
            return new PlotPlanModel
            {
                EquipmentPlacements = new List<EquipmentPlacement>
                {
                    new EquipmentPlacement
                    {
                        PlacementId = "placement:missing",
                        EquipmentEntityId = "equipment:missing",
                        NormalizedTag = "TAG-M",
                        TagSourceHandle = "TAG-H-M",
                        PlacementStatus = "Incomplete",
                        Easting = new CoordinateAssociation { Axis = "E", Status = "Missing" },
                        Northing = new CoordinateAssociation { Axis = "N", Status = "Missing" }
                    }
                }
            };
        }

        private static bool ApproxEqual(double a, double b) => Math.Abs(a - b) < 1e-9;

        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}

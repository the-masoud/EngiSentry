using System;
using System.Linq;
using EngiSentry.Contracts;
using EngiSentry.Revision;

namespace EngiSentry.Tests
{
    internal static class RevisionTests
    {
        private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        public static void Run()
        {
            string samplePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "samples", "revision", "example-revision-baseline.json");
            string sampleSha; RevisionSnapshotFile.Read(System.IO.Path.GetFullPath(samplePath), out sampleSha);
            var index = new EngineeringEntityIndex { EquipmentEntities = { new EquipmentEntity { CanonicalId = "equipment:p-1-1", NormalizedTag = "p-1-1", HasDrawingEvidence = true, EquipmentListLinks = { new EquipmentListLink { Description = " Desc ", Installation = "INST" } } }, new EquipmentEntity { CanonicalId = "equipment:LIST-1", NormalizedTag = "LIST-1", HasEquipmentListEvidence = true } } };
            var plot = new PlotPlanModel { EquipmentPlacements = { new EquipmentPlacement { EquipmentEntityId = "equipment:p-1-1", PlacementStatus = "Complete", Easting = new CoordinateAssociation { Axis = "E", Status = "Resolved", SelectedValue = 10m }, Northing = new CoordinateAssociation { Axis = "N", Status = "Resolved", SelectedValue = 20m } } } };
            var snapshot = new DrawingSnapshot { FileName = "a.dwg", InsertionUnits = "Unitless" };
            RevisionSnapshot current = RevisionSnapshotBuilder.Build(snapshot, index, plot);
            Assert(current.Equipment.Count == 1 && current.Equipment[0].EquipmentCanonicalId == "equipment:P-1-1", "Revision identity or list-only exclusion failed.");
            Assert(current.Equipment[0].DescriptionValues.Single() == "Desc", "Revision attribute normalization failed.");
            var changed = new RevisionSnapshot { Drawing = new RevisionDrawing { InsertionUnits = "Unitless" }, Equipment = { new RevisionEquipmentRecord { EquipmentCanonicalId = "equipment:P-1-1", NormalizedTag = "P-1-1", CompleteCoordinatePairs = { new RevisionCoordinatePair { Easting = 11m, Northing = 20m } }, DescriptionValues = { "Other" } }, new RevisionEquipmentRecord { EquipmentCanonicalId = "equipment:NEW-1-1", NormalizedTag = "NEW-1-1" } } };
            RevisionComparisonReport report = RevisionComparer.Compare(current, changed, "base.json", "base.json", "ABC");
            Assert(report.Statistics.AddedEquipmentCount == 1 && report.Statistics.RemovedEquipmentCount == 0 && report.Statistics.CoordinateChangedEquipmentCount == 1 && report.Statistics.AttributeChangedEquipmentCount == 1, "Revision comparison changes failed.");
            Assert(report.Changes.Any(x => x.ChangeKind == "EquipmentAdded") && report.Changes.Any(x => x.ChangeKind == "EquipmentCoordinateEvidenceChanged") && report.Changes.Any(x => x.ChangeKind == "EquipmentAttributeEvidenceChanged"), "Revision change kinds failed.");
            RevisionLineageAssessment mediumLineage = RevisionLineageAssessor.Assess(current, changed);
            Assert(mediumLineage.Confidence == "MEDIUM" && mediumLineage.CommonIdentityCount == 1 && mediumLineage.UnionIdentityCount == 2 && mediumLineage.IdentityOverlapPercent == 50m, "Revision lineage MEDIUM assessment failed.");
            RevisionLineageAssessment highLineage = RevisionLineageAssessor.Assess(current, current);
            Assert(highLineage.Confidence == "HIGH" && highLineage.IdentityOverlapPercent == 100m, "Revision lineage HIGH assessment failed.");
            var unrelated = new RevisionSnapshot { Drawing = new RevisionDrawing(), Equipment = { new RevisionEquipmentRecord { EquipmentCanonicalId = "equipment:OTHER-1", NormalizedTag = "OTHER-1" } } };
            RevisionLineageAssessment lowLineage = RevisionLineageAssessor.Assess(current, unrelated);
            Assert(lowLineage.Confidence == "LOW" && lowLineage.CommonIdentityCount == 0 && lowLineage.IdentityOverlapPercent == 0m, "Revision lineage LOW assessment failed.");
            RevisionLineageAssessment insufficientLineage = RevisionLineageAssessor.Assess(new RevisionSnapshot(), new RevisionSnapshot());
            Assert(insufficientLineage.Confidence == "INSUFFICIENT_EVIDENCE", "Revision lineage insufficient-evidence assessment failed.");
            var differentUnits = new RevisionSnapshot { Drawing = new RevisionDrawing { InsertionUnits = "Other" } };
            RevisionComparisonReport differentUnitReport = RevisionComparer.Compare(current, differentUnits, "", "", "");
            Assert(differentUnitReport != null, "Insertion unit metadata unexpectedly blocked revision comparison.");
        }
    }
}

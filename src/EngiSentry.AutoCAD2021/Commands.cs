using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using EngiSentry.Core;
using EngiSentry.Rules;
using EngiSentry.AutoCAD2021.Scanning;
using EngiSentry.Contracts;
using EngiSentry.Semantics;
using EngiSentry.PlotPlan;
using EngiSentry.Standards;
using EngiSentry.CrossDocument;
using EngiSentry.Revision;
using EngiSentry.Intelligence;

namespace EngiSentry.AutoCAD2021
{
    public sealed class Commands
    {
        [CommandMethod("ES_HELLO")]
        public void Hello()
        {
            WriteToActiveEditor("\nEngiSentry M0.1\nAutoCAD connection: OK\n");
            Document? helloDocument = Application.DocumentManager?.MdiActiveDocument;
            VisualResultsPalette.ShowHome("ES_HELLO", helloDocument, helloDocument == null ? "<none>" : System.IO.Path.GetFileName(helloDocument.Name), "READY", "Plugin loaded and AutoCAD connection is OK.");
        }

        [CommandMethod("ENGISENTRY")]
        public void EngiSentry()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null)
                {
                    WriteToActiveEditor("\nEngiSentry\nAutoCAD connection: OK\nDrawing: <none>\nStatus: NO ACTIVE DRAWING\n");
                    VisualResultsPalette.ShowHome("ENGISENTRY", null, "<none>", "NO ACTIVE DRAWING", "AutoCAD connection is OK.");
                    return;
                }

                string drawingName = string.IsNullOrWhiteSpace(document.Name)
                    ? "<unnamed>"
                    : System.IO.Path.GetFileName(document.Name);
                document.Editor.WriteMessage(
                    "\nEngiSentry\nAutoCAD connection: OK\nDrawing: {0}\nStatus: READY\n",
                    drawingName);
                    VisualResultsPalette.ShowHome("ENGISENTRY", document, drawingName, "READY", "AutoCAD connection is OK.");
            }
            catch (System.Exception exception)
            {
                WriteToActiveEditor("\nEngiSentry error: {0}\n", exception.Message);
                VisualResultsPalette.ShowFailure("ENGISENTRY", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message);
            }
        }

        [CommandMethod("ES_SCAN")]
        public void Scan()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null)
                {
                    WriteToActiveEditor("\nEngiSentry M0.2 Scan Failed\nReason: no active drawing\n");
                    VisualResultsPalette.ShowFailure("ES_SCAN", null, "<none>", "No active drawing.");
                    return;
                }

                DrawingScanner.Scan(document, out EngiSentry.Contracts.DrawingSnapshot snapshot);
                string path = SnapshotFileWriter.Write(snapshot);
                int unresolved = 0;
                foreach (EngiSentry.Contracts.XrefSnapshot xref in snapshot.Xrefs) if (!xref.IsResolved) unresolved++;
                document.Editor.WriteMessage("\nEngiSentry M0.2 Scan Complete\nDrawing: {0}\nModel entities: {1}\nPaper entities: {2}\nBlocks: {3}\nBlock references: {4}\nXrefs: {5}\nUnresolved Xrefs: {6}\nSnapshot: {7}\nDBMOD unchanged: {8}\nStatus: READ-ONLY\n", snapshot.FileName ?? "<unnamed>", snapshot.ModelSpaceEntityCount, snapshot.PaperSpaceEntityCount, snapshot.BlockDefinitions.Count, snapshot.BlockReferenceCount, snapshot.Xrefs.Count, unresolved, path, snapshot.DrawingModifiedByScan ? "NO" : "YES");
                VisualResultsPalette.ShowScan(document, snapshot, path);
            }
            catch (System.Exception exception)
            {
                WriteToActiveEditor("\nEngiSentry M0.2 Scan Failed\nReason: {0}\n", exception.Message);
                VisualResultsPalette.ShowFailure("ES_SCAN", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message);
            }
        }

        [CommandMethod("ES_SEMANTIC")]
        public void Semantic()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nEngiSentry M0.3 Semantic Extraction Failed\nReason: no active drawing\n"); VisualResultsPalette.ShowFailure("ES_SEMANTIC", null, "<none>", "No active drawing."); return; }
                DrawingScanner.Scan(document, out DrawingSnapshot snapshot);
                EngineeringDrawingModel model = SemanticExtractor.Extract(snapshot);
                int afterExtraction = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterExtraction) throw new InvalidOperationException("DBMOD changed during semantic processing.");
                string path = SnapshotFileWriter.WriteSemantic(model, snapshot.FileName, snapshot.ScanTimestampUtc);
                int afterArtifact = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterArtifact) throw new InvalidOperationException("DBMOD changed during semantic artifact creation.");
                SemanticStatistics statistics = model.Statistics;
                document.Editor.WriteMessage("\nEngiSentry M0.3 Semantic Extraction Complete\nDrawing: {0}\nEquipment tag occurrences: {1}\nUnique equipment tags: {2}\nStructure tag occurrences: {3}\nCoordinates: {4}\nElevations: {5}\nRoad labels: {6}\nBoundary labels: {7}\nEquipment list rows: {8}\nSemantic model: {9}\nDBMOD unchanged: YES\nStatus: READ-ONLY\n", snapshot.FileName ?? "<unnamed>", statistics.EquipmentTagOccurrences, statistics.UniqueEquipmentTags, statistics.StructureTagOccurrences, statistics.CoordinateCount, statistics.ElevationCount, statistics.RoadLabelCount, statistics.BoundaryLabelCount, statistics.EquipmentListEntryCount, path);
                VisualResultsPalette.ShowSemantic(document, snapshot, model, path);
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nEngiSentry M0.3 Semantic Extraction Failed\nReason: {0}\n", exception.Message);  VisualResultsPalette.ShowFailure("ES_SEMANTIC", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message); }
        }

        [CommandMethod("ES_INDEX")]
        public void Index()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nEngiSentry M0.4 Entity Index Failed\nReason: no active drawing\n"); VisualResultsPalette.ShowFailure("ES_INDEX", null, "<none>", "No active drawing."); return; }
                DrawingScanner.Scan(document, out DrawingSnapshot snapshot);
                EngineeringDrawingModel semantic = SemanticExtractor.Extract(snapshot);
                EngineeringEntityIndex index = EntityIndexer.Build(semantic);
                int afterProcessing = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterProcessing) throw new InvalidOperationException("DBMOD changed during entity-index processing.");
                string path = SnapshotFileWriter.WriteEntityIndex(index, snapshot.FileName, snapshot.ScanTimestampUtc);
                int afterArtifact = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterArtifact) throw new InvalidOperationException("DBMOD changed during entity-index artifact creation.");
                EntityIndexStatistics s = index.Statistics;
                document.Editor.WriteMessage("\nEngiSentry M0.4 Entity Index Complete\nDrawing: {0}\nEquipment entities: {1}\nStructure entities: {2}\nEquipment with drawing evidence: {3}\nEquipment with list evidence: {4}\nEquipment with both: {5}\nGrouped tags: {6}\nGrouped member links: {7}\nUnresolved links: {8}\nAmbiguous links: {9}\nEntity index: {10}\nDBMOD unchanged: YES\nStatus: READ-ONLY\n", snapshot.FileName ?? "<unnamed>", s.EquipmentEntityCount, s.StructureEntityCount, s.EquipmentEntitiesWithDrawingEvidence, s.EquipmentEntitiesWithEquipmentListEvidence, s.EquipmentEntitiesWithBoth, s.GroupedTagCount, s.GroupedMemberLinkCount, s.UnresolvedLinkCount, s.AmbiguousLinkCount, path);
                VisualResultsPalette.ShowIndex(document, snapshot, index, path);
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nEngiSentry M0.4 Entity Index Failed\nReason: {0}\n", exception.Message);  VisualResultsPalette.ShowFailure("ES_INDEX", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message); }
        }

        [CommandMethod("ES_QA")]
        public void Qa()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nEngiSentry M1.0 QA Failed\nReason: no active drawing\n"); VisualResultsPalette.ShowFailure("ES_QA", null, "<none>", "No active drawing."); return; }
                DrawingScanner.Scan(document, out DrawingSnapshot snapshot);
                EngineeringDrawingModel semantic = SemanticExtractor.Extract(snapshot);
                EngineeringEntityIndex index = EntityIndexer.Build(semantic);
                QaReport report = QaEngine.Evaluate(index, BuiltInQaRules.All);
                int afterProcessing = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterProcessing) throw new InvalidOperationException("DBMOD changed during QA processing.");
                string path = SnapshotFileWriter.WriteQaReport(report, snapshot.FileName, snapshot.ScanTimestampUtc);
                int afterArtifact = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterArtifact) throw new InvalidOperationException("DBMOD changed during QA artifact creation.");
                QaStatistics s = report.Statistics;
                document.Editor.WriteMessage("\nEngiSentry M1.0 QA Complete\nDrawing: {0}\nRules executed: {1}\nFindings: {2}\nWarnings: {3}\nErrors: {4}\nOverall status: {5}\nQA report: {6}\nDBMOD unchanged: YES\nStatus: READ-ONLY\n", snapshot.FileName ?? "<unnamed>", s.RuleCount, s.TotalFindings, s.WarningFindings, s.ErrorFindings, report.OverallStatus, path);
                VisualResultsPalette.ShowQa(document, snapshot, report, path, "ES_QA", "QA Results", "General deterministic drawing QA.");
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nEngiSentry M1.0 QA Failed\nReason: {0}\n", exception.Message);  VisualResultsPalette.ShowFailure("ES_QA", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message); }
        }

        [CommandMethod("ES_PLOTQA")]
        public void PlotPlanQa()
        {
            try
            {
                WriteToActiveEditor("\n[ES_PLOTQA] START\n");
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nEngiSentry M2.2 Plot Plan QA Failed\nReason: no active drawing\n"); VisualResultsPalette.ShowFailure("ES_PLOTQA", null, "<none>", "No active drawing."); return; }
                ProjectDistanceCriteria? criteria = ProjectDistanceCriteriaStore.ResolveOrPrompt(document.Name);
                if (criteria == null) { WriteToActiveEditor("\nES_PLOTQA cancelled: Project Distance Criteria were not saved.\n"); VisualResultsPalette.ShowCancelled("ES_PLOTQA", document, System.IO.Path.GetFileName(document.Name), "Project Distance Criteria were not saved."); return; }
                DrawingScanner.Scan(document, true, out DrawingSnapshot snapshot, out PlotGeometryCapture geometryCapture);
                WriteToActiveEditor("[ES_PLOTQA] Snapshot built\n");
                EngineeringDrawingModel semantic = SemanticExtractor.Extract(snapshot);
                WriteToActiveEditor("[ES_PLOTQA] Semantic model built\n");
                EngineeringEntityIndex index = EntityIndexer.Build(semantic);
                WriteToActiveEditor("[ES_PLOTQA] Entity index built\n");
                PlotPlanModel plotPlan = PlotPlanBuilder.Build(index, semantic, snapshot, geometryCapture);
                WriteToActiveEditor("[ES_PLOTQA] PlotPlan model built\n");
                QaReport baseQa = QaEngine.Evaluate(index, BuiltInQaRules.All, plotPlan, BuiltInPlotPlanQaRules.All);
                string approvalDirectory = System.IO.Path.Combine(SnapshotFileWriter.ResolveArtifactsRoot(), "qa", "plotplan");
                ProjectDistanceGovernanceAggregateResult governance = ProjectDistanceGovernanceRuntimeLoader.LoadCanonical(criteria, approvalDirectory);
                QaReport qa = ProjectEngineeringDistanceRuntime.Evaluate(criteria, plotPlan, baseQa, governance);
                EngineeringDistanceRunSummary summary = qa.EngineeringDistanceSummary ?? throw new InvalidOperationException("ES_PLOTQA engineering distance summary is required.");
                var evaluations = summary.Evaluations ?? throw new InvalidOperationException("ES_PLOTQA engineering distance evaluations are required.");
                EngineeringDistanceRuleEvaluation roadEvaluation = evaluations.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId);
                EngineeringDistanceRuleEvaluation plantEvaluation = evaluations.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToPlantRuleId);
                EngineeringDistanceRuleEvaluation unitEvaluation = evaluations.Single(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToUnitBoundaryRuleId);
                WriteToActiveEditor("Project criteria source: ProjectDistanceCriteria\nEquipment to Road minimum: {0} m\nEquipment to Plant minimum: {1} m\nEquipment to Unit Boundary minimum: {2} m\nEquipment to Road rule status: {3}\nEquipment to Plant rule status: {4}\nEquipment to Unit Boundary rule status: {5}\n", summary.AccessRoadEdgeMinimumMeters, summary.SecurityFenceMinimumMeters, summary.UnitBoundaryMinimumMeters, roadEvaluation.Status, plantEvaluation.Status, unitEvaluation.Status);
                foreach (ProjectDistanceGovernanceAggregateItem item in governance.Items.OrderBy(x => x.CandidateId, StringComparer.Ordinal)) WriteToActiveEditor("Governance: {0} | {1} | {2}\n", item.CandidateId, item.AuthorityState ?? "<none>", item.Resolution.State);
                WriteToActiveEditor("[ES_PLOTQA] QA complete\n");
                int afterProcessing = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterProcessing) throw new InvalidOperationException("DBMOD changed during plot-plan QA processing.");
                PlotPlanQaReport plotPlanQaReport = PlotPlanReportCompactor.CreatePlotPlanQaReport(plotPlan, qa);
                string path = SnapshotFileWriter.WritePlotPlanQaReport(plotPlanQaReport, snapshot.FileName, snapshot.ScanTimestampUtc);
                StandardAuthorityReviewPackage reviewPackage = StandardAuthorityReviewPackageBuilder.Create(plotPlanQaReport, snapshot.FileName ?? "unnamed", snapshot.ScanTimestampUtc);
                string reviewPackagePath = SnapshotFileWriter.WriteStandardsReviewPackage(reviewPackage);
                string reviewPackageSha256 = HumanEngineeringReviewDecisionFileStore.ComputeSha256(reviewPackagePath);
                WriteToActiveEditor("[ES_PLOTQA] Artifacts written\nQA report: {0}\nStandards review package: {1}\n", path, reviewPackagePath);
                int afterArtifact = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterArtifact) throw new InvalidOperationException("DBMOD changed during plot-plan QA artifact creation.");
                PlotPlanStatistics s = plotPlan.Statistics;
                PlotGeometryStatistics g = plotPlan.Geometry.Statistics;
                document.Editor.WriteMessage("\nEngiSentry M2.2 Plot Plan QA Complete\nDrawing: {0}\nEquipment placements: {1}\nComplete placements: {2}\nIncomplete placements: {3}\nAmbiguous placements: {4}\nGeometry primitives: {5}\nGeometry associations: {6}\nResolved geometry associations: {7}\nMissing geometry associations: {8}\nAmbiguous geometry associations: {9}\nSpatial constraints: {10}\nResolved spatial constraints: {11}\nMissing spatial constraints: {12}\nAmbiguous spatial constraints: {13}\nRules executed: {14}\nFindings: {15}\nInfo: {16}\nWarnings: {17}\nErrors: {18}\nOverall status: {19}\nQA report: {20}\nDBMOD unchanged: YES\nStatus: READ-ONLY\n", snapshot.FileName ?? "<unnamed>", s.EquipmentPlacementCount, s.CompletePlacementCount, s.IncompletePlacementCount, s.AmbiguousPlacementCount, g.SupportedPrimitiveCount, g.GeometryAssociationCount, g.ResolvedAssociationCount, g.MissingAssociationCount, g.AmbiguousAssociationCount, g.SpatialConstraintCount, g.ResolvedSpatialConstraintCount, g.MissingSpatialConstraintCount, g.AmbiguousSpatialConstraintCount, qa.Statistics.RuleCount, qa.Statistics.TotalFindings, qa.Statistics.InfoFindings, qa.Statistics.WarningFindings, qa.Statistics.ErrorFindings, qa.OverallStatus, path);
                VisualResultsPalette.ShowPlotQa(document, snapshot, plotPlanQaReport, path, reviewPackage, reviewPackagePath, reviewPackageSha256);
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nEngiSentry M2.2 Plot Plan QA Failed\nReason: {0}: {1}\n", exception.GetType().FullName, exception.Message);  VisualResultsPalette.ShowFailure("ES_PLOTQA", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message); }
        }

        [CommandMethod("ES_PROFILEQA")]
        public void ProfileQa()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nEngiSentry M3.0 Profile QA Failed\nReason: no active drawing\n"); VisualResultsPalette.ShowFailure("ES_PROFILEQA", null, "<none>", "No active drawing."); return; }
                string profileSource;
                ProfileResolution resolution = ResolveAutomaticProfile(out profileSource);
                DrawingScanner.Scan(document, true, out DrawingSnapshot snapshot, out PlotGeometryCapture geometryCapture);
                EngineeringDrawingModel semantic = SemanticExtractor.Extract(snapshot);
                EngineeringEntityIndex index = EntityIndexer.Build(semantic);
                PlotPlanModel plotPlan = PlotPlanBuilder.Build(index, semantic, snapshot, geometryCapture);
                QaReport qa = QaEngine.EvaluateWithProfile(index, BuiltInQaRules.All, plotPlan, ProfilePlotPlanQaRules.Create(resolution), resolution);
                int afterProcessing = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterProcessing) throw new InvalidOperationException("DBMOD changed during profile QA processing.");
                string path = SnapshotFileWriter.WriteProfilePlotPlanQaReport(new PlotPlanQaReport { PlotPlan = plotPlan, Qa = qa }, snapshot.FileName, snapshot.ScanTimestampUtc);
                int afterArtifact = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterArtifact) throw new InvalidOperationException("DBMOD changed during profile QA artifact creation.");
                document.Editor.WriteMessage("\nEngiSentry profile source: {0}\n", profileSource);
                document.Editor.WriteMessage("\nEngiSentry M3.0 Profile QA Complete\nDrawing: {0}\nEffective profiles: {1}\nRules executed: {2}\nFindings: {3}\nInfo: {4}\nWarnings: {5}\nErrors: {6}\nOverall status: {7}\nQA report: {8}\nDBMOD unchanged: YES\nStatus: READ-ONLY\n", snapshot.FileName ?? "<unnamed>", string.Join(" -> ", resolution.AppliedProfileIds), qa.Statistics.RuleCount, qa.Statistics.TotalFindings, qa.Statistics.InfoFindings, qa.Statistics.WarningFindings, qa.Statistics.ErrorFindings, qa.OverallStatus, path);
                VisualResultsPalette.ShowProfileQa(document, snapshot, plotPlan, qa, resolution, path);
            }
            catch (ProfileValidationException exception) { WriteToActiveEditor("\nEngiSentry M3.0 Profile QA Failed\nReason: {0}\n", exception.Message);  VisualResultsPalette.ShowFailure("ES_PROFILEQA", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message); }
            catch (System.Exception exception) { WriteToActiveEditor("\nEngiSentry M3.0 Profile QA Failed\nReason: {0}: {1}\n", exception.GetType().FullName, exception.Message);  VisualResultsPalette.ShowFailure("ES_PROFILEQA", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message); }
        }

        [CommandMethod("ES_CROSSQA")]
        public void CrossQa()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nEngiSentry M6.0 Cross-Document QA Failed\nReason: no active drawing\n"); VisualResultsPalette.ShowFailure("ES_CROSSQA", null, "<none>", "No active drawing."); return; }
                string registerPath = ResolveAutomaticEquipmentRegister();
                ExternalEquipmentRegister register = ExternalEquipmentRegisterLoader.ReadFile(registerPath);
                DrawingScanner.Scan(document, true, out DrawingSnapshot snapshot, out PlotGeometryCapture geometryCapture);
                EngineeringDrawingModel semantic = SemanticExtractor.Extract(snapshot);
                EngineeringEntityIndex index = EntityIndexer.Build(semantic);
                PlotPlanModel plotPlan = PlotPlanBuilder.Build(index, semantic, snapshot, geometryCapture);
                QaReport qa = QaEngine.EvaluateWithExternalRegister(index, BuiltInQaRules.All, plotPlan, BuiltInPlotPlanQaRules.All, register, BuiltInCrossDocumentQaRules.All);
                int afterProcessing = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterProcessing) throw new InvalidOperationException("DBMOD changed during cross-document QA processing.");
                string path = SnapshotFileWriter.WriteCrossDocumentQaReport(new CrossDocumentQaReport { PlotPlan = plotPlan, ExternalEquipmentRegister = register, Qa = qa }, snapshot.FileName, snapshot.ScanTimestampUtc);
                int afterArtifact = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterArtifact) throw new InvalidOperationException("DBMOD changed during cross-document QA artifact creation.");
                QaStatistics s = qa.Statistics;
                document.Editor.WriteMessage("\nEngiSentry M6.0 Cross-Document QA Complete\nExternal file: {0}\nExternal SHA256: {1}\nSource rows: {2}\nExpanded equipment identities: {3}\nRules executed: {4}\nFindings: {5}\nInfo: {6}\nWarnings: {7}\nErrors: {8}\nOverall status: {9}\nReport: {10}\nDBMOD unchanged: YES\nStatus: READ-ONLY\n", register.SourceFullPath, register.SourceSha256, register.Statistics.SourceRowCount, register.Statistics.ExpandedEquipmentCount, s.RuleCount, s.TotalFindings, s.InfoFindings, s.WarningFindings, s.ErrorFindings, qa.OverallStatus, path);
                VisualResultsPalette.ShowCrossQa(document, snapshot, register, qa, path);
            }
            catch (ExternalRegisterValidationException exception) { WriteToActiveEditor("\nEngiSentry M6.0 Cross-Document QA Failed\nReason: {0}\n", exception.Message);  VisualResultsPalette.ShowFailure("ES_CROSSQA", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message); }
            catch (System.Exception exception) { WriteToActiveEditor("\nEngiSentry M6.0 Cross-Document QA Failed\nReason: {0}: {1}\n", exception.GetType().FullName, exception.Message);  VisualResultsPalette.ShowFailure("ES_CROSSQA", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message); }
        }

        [CommandMethod("ES_REVBASELINE")]
        public void RevisionBaseline()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nEngiSentry M7.0 Revision Baseline Failed\nReason: no active drawing\n"); VisualResultsPalette.ShowFailure("ES_REVBASELINE", null, "<none>", "No active drawing."); return; }
                DrawingScanner.Scan(document, true, out DrawingSnapshot snapshot, out PlotGeometryCapture capture);
                EngineeringDrawingModel semantic = SemanticExtractor.Extract(snapshot); EngineeringEntityIndex index = EntityIndexer.Build(semantic); PlotPlanModel plot = PlotPlanBuilder.Build(index, semantic, snapshot, capture);
                RevisionSnapshot revision = RevisionSnapshotBuilder.Build(snapshot, index, plot);
                if (revision.Statistics.EquipmentCount == 0) throw new RevisionValidationException("INSUFFICIENT_EVIDENCE: no drawing-backed equipment identities were detected, so a trustworthy revision baseline cannot be created.");
                int after = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture); if (snapshot.DbModBefore != after) throw new InvalidOperationException("DBMOD changed during revision baseline processing.");
                string path = SnapshotFileWriter.WriteRevisionBaseline(revision); SetActiveRevisionBaseline(path); int afterArtifact = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture); if (snapshot.DbModBefore != afterArtifact) throw new InvalidOperationException("DBMOD changed during revision baseline artifact creation.");
                document.Editor.WriteMessage("\nEngiSentry M7.0 Revision Baseline Complete\nEquipment count: {0}\nBaseline: {1}\nDBMOD unchanged: YES\nStatus: READ-ONLY\n", revision.Statistics.EquipmentCount, path);
                VisualResultsPalette.ShowRevisionBaseline(document, snapshot, revision, path);
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nEngiSentry M7.0 Revision Baseline Failed\nReason: {0}: {1}\n", exception.GetType().FullName, exception.Message);  VisualResultsPalette.ShowFailure("ES_REVBASELINE", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message); }
        }

        [CommandMethod("ES_REVISION")]
        public void Revision()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nEngiSentry M7.0 Revision Failed\nReason: no active drawing\n"); VisualResultsPalette.ShowFailure("ES_REVISION", null, "<none>", "No active drawing."); return; }
                string baselinePath = ResolveAutomaticRevisionBaseline();
                string sha256; RevisionSnapshot baseline = RevisionSnapshotFile.Read(baselinePath, out sha256);
                DrawingScanner.Scan(document, true, out DrawingSnapshot snapshot, out PlotGeometryCapture capture);
                EngineeringDrawingModel semantic = SemanticExtractor.Extract(snapshot); EngineeringEntityIndex index = EntityIndexer.Build(semantic); PlotPlanModel plot = PlotPlanBuilder.Build(index, semantic, snapshot, capture); RevisionSnapshot current = RevisionSnapshotBuilder.Build(snapshot, index, plot);
                if (baseline.Statistics.EquipmentCount == 0 || current.Statistics.EquipmentCount == 0) throw new RevisionValidationException("INSUFFICIENT_EVIDENCE: baseline and current drawings must both contain drawing-backed equipment identities before revision changes can be trusted.");
                RevisionComparisonReport report = RevisionComparer.Compare(baseline, current, System.IO.Path.GetFileName(baselinePath), baselinePath, sha256);
                int after = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture); if (snapshot.DbModBefore != after) throw new InvalidOperationException("DBMOD changed during revision processing.");
                string path = SnapshotFileWriter.WriteRevisionComparison(report, snapshot.FileName, snapshot.ScanTimestampUtc); int afterArtifact = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture); if (snapshot.DbModBefore != afterArtifact) throw new InvalidOperationException("DBMOD changed during revision artifact creation.");
                RevisionComparisonStatistics s = report.Statistics; RevisionLineageAssessment lineage = RevisionLineageAssessor.Assess(report.Baseline, report.Current); document.Editor.WriteMessage("\nEngiSentry M7.0 Revision Complete\nBaseline file: {0}\nBaseline SHA256: {1}\nBaseline equipment: {2}\nCurrent equipment: {3}\nAdded: {4}\nRemoved: {5}\nCoordinate changed: {6}\nAttribute changed: {7}\nChanged equipment: {8}\nUnchanged equipment: {9}\nTotal changes: {10}\nRevision lineage: {11}\nIdentity overlap: {12}% ({13}/{14})\nReport: {15}\nDBMOD unchanged: YES\nStatus: READ-ONLY\n", baselinePath, sha256, s.BaselineEquipmentCount, s.CurrentEquipmentCount, s.AddedEquipmentCount, s.RemovedEquipmentCount, s.CoordinateChangedEquipmentCount, s.AttributeChangedEquipmentCount, s.ChangedEquipmentCount, s.UnchangedEquipmentCount, s.TotalChangeCount, lineage.Confidence, lineage.IdentityOverlapPercent, lineage.CommonIdentityCount, lineage.UnionIdentityCount, path);
                VisualResultsPalette.ShowRevision(document, snapshot, report, path);
            }
            catch (RevisionValidationException exception) { WriteToActiveEditor("\nEngiSentry M7.0 Revision Failed\nReason: {0}\n", exception.Message);  VisualResultsPalette.ShowFailure("ES_REVISION", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message); }
            catch (System.Exception exception) { WriteToActiveEditor("\nEngiSentry M7.0 Revision Failed\nReason: {0}: {1}\n", exception.GetType().FullName, exception.Message);  VisualResultsPalette.ShowFailure("ES_REVISION", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message); }
        }

        [CommandMethod("ES_DISTANCESETTINGS")]
        public void DistanceSettings()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nProject Distance Criteria require an active saved drawing.\n"); return; }
                string projectDirectory = ProjectDistanceCriteriaStore.ResolveProjectDirectory(document.Name);
                ProjectDistanceCriteria criteria;
                if (!ProjectDistanceCriteriaStore.TryLoad(projectDirectory, ProjectDistanceCriteriaStore.ProductionStorageRoot, out ProjectDistanceCriteria? saved)) criteria = ProjectDistanceCriteriaStore.CreateDefaults(projectDirectory);
                else criteria = ProjectDistanceCriteriaStore.CreateCurrentDraft(saved!);
                ProjectDistanceCriteria? selected = ProjectDistanceCriteriaDialog.Show(projectDirectory, criteria);
                if (selected == null) { document.Editor.WriteMessage("\nEngiSentry Project Distance Criteria cancelled.\n"); return; }
                ProjectDistanceCriteriaStore.Save(selected, ProjectDistanceCriteriaStore.ProductionStorageRoot);
                document.Editor.WriteMessage("\nEngiSentry Project Distance Criteria saved\nProject directory: {0}\nEquipment to Road minimum: {1} m\nEquipment to Plant minimum: {2} m\nEquipment to Unit Boundary minimum: {3} m\n", selected.ProjectDirectory, selected.AccessRoadEdgeMinimumMeters, selected.SecurityFenceMinimumMeters, selected.UnitBoundaryMinimumMeters);
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nEngiSentry Project Distance Criteria Failed\nReason: {0}\n", exception.Message); }
        }

        private static string ResolveAutomaticEquipmentRegister()
        {
            Document? document = Application.DocumentManager?.MdiActiveDocument;
            string drawingPath = document == null ? string.Empty : document.Name;
            return ExternalEquipmentRegisterLoader.ResolveAutomaticPath(drawingPath);
        }

        private static void SetActiveRevisionBaseline(string baselinePath)
        {
            string stateDirectory = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "EngiSentry", "revision");
            System.IO.Directory.CreateDirectory(stateDirectory);
            System.IO.File.WriteAllText(System.IO.Path.Combine(stateDirectory, "active-baseline.path"), System.IO.Path.GetFullPath(baselinePath));
        }

        private static string ResolveAutomaticRevisionBaseline()
        {
            string localRoot = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "EngiSentry");
            string pointerPath = System.IO.Path.Combine(localRoot, "revision", "active-baseline.path");
            if (System.IO.File.Exists(pointerPath))
            {
                string pointed = System.IO.File.ReadAllText(pointerPath).Trim();
                if (!string.IsNullOrWhiteSpace(pointed) && System.IO.File.Exists(pointed)) return pointed;
            }
            throw new RevisionValidationException("No active revision baseline is available. Open the reference revision and run ES_REVBASELINE first.");
        }

        private static ProfileResolution ResolveAutomaticProfile(out string profileSource)
        {
            string localProfile = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "EngiSentry", "profiles", "active-profiles.json");
            string assemblyDirectory = System.IO.Path.GetDirectoryName(typeof(Commands).Assembly.Location) ?? string.Empty;
            string bundledProfile = System.IO.Path.GetFullPath(System.IO.Path.Combine(assemblyDirectory, "..", "Profiles", "active-profiles.json"));
            string[] candidates = new[] { localProfile, bundledProfile };
            foreach (string candidate in candidates)
            {
                if (!System.IO.File.Exists(candidate)) continue;
                profileSource = candidate;
                return ProfileResolver.Resolve(KnownPlotPlanRules(), ProfileJson.ReadFile(candidate));
            }
            profileSource = "BuiltIn only (no active external profile installed)";
            return ProfileResolver.Resolve(KnownPlotPlanRules(), null);
        }

        [CommandMethod("ES_SUGGEST")]
        public void Suggest()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nEngiSentry M2.3 Placement Suggestion Failed\nReason: no active drawing\n"); VisualResultsPalette.ShowFailure("ES_SUGGEST", null, "<none>", "No active drawing."); return; }
                ProjectDistanceCriteria? criteria = ProjectDistanceCriteriaStore.ResolveOrPrompt(document.Name);
                if (criteria == null) { WriteToActiveEditor("\nES_SUGGEST cancelled: Project Distance Criteria were not saved.\n"); VisualResultsPalette.ShowCancelled("ES_SUGGEST", document, System.IO.Path.GetFileName(document.Name), "Project Distance Criteria were not saved."); return; }
                DrawingScanner.Scan(document, true, out DrawingSnapshot snapshot, out PlotGeometryCapture geometryCapture);
                EngineeringDrawingModel semantic = SemanticExtractor.Extract(snapshot);
                EngineeringEntityIndex index = EntityIndexer.Build(semantic);
                PlotPlanModel plotPlan = PlotPlanBuilder.Build(index, semantic, snapshot, geometryCapture);
                string approvalDirectory = System.IO.Path.Combine(SnapshotFileWriter.ResolveArtifactsRoot(), "qa", "plotplan");
                ProjectDistanceGovernanceAggregateResult governance = ProjectDistanceGovernanceRuntimeLoader.LoadCanonical(criteria, approvalDirectory);
                PlotPlanStandardsAuthorityRuntimeContext authorityContext = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create();
                System.Collections.Generic.IReadOnlyList<StandardsPackRule> governedRules = StandardsPackProjectionBuilder.Build(authorityContext, ProjectDistanceRuleBinder.Create(criteria), BuildGovernedResolutions(governance));
                PlacementProposalRunResult result = PlacementProposalRuntime.Build(plotPlan, governedRules, governance, out PlacementProposalRunDiagnostics diagnostics);
                int afterProcessing = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterProcessing) throw new InvalidOperationException("DBMOD changed during placement suggestion processing.");
                string path = PlacementProposalSnapshotWriter.Write(result.Proposals, snapshot.FileName, snapshot.ScanTimestampUtc);
                int afterArtifact = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterArtifact) throw new InvalidOperationException("DBMOD changed during placement suggestion artifact creation.");
                document.Editor.WriteMessage("\nEngiSentry M2.3 Placement Suggestion Complete\nDrawing: {0}\nGoverned rules: {1}\nBounded constraints: {2}\nViolated constraints: {3}\nReview-required constraints: {4}\nInsufficient-evidence constraints: {5}\nExecutable violations: {6}\nPlacement proposals: {7}\nPlacement proposals artifact: {8}\nDBMOD unchanged: YES\nStatus: READ-ONLY\n", snapshot.FileName ?? "<unnamed>", diagnostics.GovernedRuleCount, diagnostics.BoundedConstraintCount, diagnostics.ViolatedCount, diagnostics.ReviewRequiredCount, diagnostics.InsufficientEvidenceCount, diagnostics.ExecutableViolatedCount, diagnostics.ProposalCount, path);
                VisualResultsPalette.ShowPlacementProposals(document, snapshot, result, diagnostics, path);
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nEngiSentry M2.3 Placement Suggestion Failed\nReason: {0}: {1}\n", exception.GetType().FullName, exception.Message); VisualResultsPalette.ShowFailure("ES_SUGGEST", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message); }
        }

        [CommandMethod("ES_APPROVALPREP")]
        public void ApprovalPreparation()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nEngiSentry approval preparation failed\nReason: no active drawing\n"); VisualResultsPalette.ShowFailure("ES_APPROVALPREP", null, "<none>", "No active drawing."); return; }
                ProjectDistanceCriteria? criteria = ProjectDistanceCriteriaStore.ResolveOrPrompt(document.Name);
                if (criteria == null) { WriteToActiveEditor("\nES_APPROVALPREP cancelled: Project Distance Criteria were not saved.\n"); VisualResultsPalette.ShowCancelled("ES_APPROVALPREP", document, System.IO.Path.GetFileName(document.Name), "Project Distance Criteria were not saved."); return; }
                DrawingScanner.Scan(document, true, out DrawingSnapshot snapshot, out _);
                ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create();
                string approvalDirectory = System.IO.Path.Combine(SnapshotFileWriter.ResolveArtifactsRoot(), "qa", "plotplan");
                string policyDirectory = EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.CanonicalDirectory;
                string trustPath = EngineeringDistanceApprovalAuthorityTrustStore.CanonicalPath;
                IReadOnlyList<EngineeringDistanceApprovalReadinessItem> readiness = EngineeringDistanceApprovalReadinessService.Inspect(criteria, approvalDirectory, trustPath, policyDirectory);
                string requestDirectory = System.IO.Path.Combine(SnapshotFileWriter.ResolveArtifactsRoot(), "qa", "approval-prep");
                var approvalPaths = new System.Collections.Generic.List<string>();
                var policyPaths = new System.Collections.Generic.List<string>();
                foreach (EngineeringDistanceCandidateDefinition candidate in EngineeringDistanceCandidateCatalog.BuiltInCandidates.OrderBy(x => x.CandidateId, System.StringComparer.Ordinal))
                {
                    EngineeringDistanceApprovalRequest request = EngineeringDistanceApprovalPreparation.BuildApprovalRequest(criteria.ProjectDirectory, candidate, context.ClauseRegistry, context.SourceRegistry, System.IO.Path.GetFileName(document.Name), snapshot.ScanTimestampUtc);
                    EngineeringDistanceAuthorityPolicyRequest policy = EngineeringDistanceApprovalPreparation.BuildPolicyRequest(criteria.ProjectDirectory, candidate);
                    approvalPaths.Add(ApprovalPreparationArtifactWriter.WriteApprovalRequest(request, requestDirectory));
                    policyPaths.Add(ApprovalPreparationArtifactWriter.WritePolicyRequest(policy, requestDirectory));
                }
                int after = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != after) throw new InvalidOperationException("DBMOD changed during approval preparation.");
                document.Editor.WriteMessage("\nEngiSentry Approval Preparation Complete\nRoad/Fence request artifacts: {0}\nExecutable candidates: {1}\nDBMOD unchanged: YES\nStatus: PREPARATION ONLY | NO APPROVAL GRANTED | READ-ONLY\n", approvalPaths.Count, readiness.Count(x => x.AutoExecutionAllowed));
                VisualResultsPalette.ShowApprovalPreparation(document, snapshot, readiness, approvalPaths, policyPaths);
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nEngiSentry approval preparation failed\nReason: {0}: {1}\n", exception.GetType().FullName, exception.Message); VisualResultsPalette.ShowFailure("ES_APPROVALPREP", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message); }
        }

        [CommandMethod("ES_APPROVALPACKAGE")]
        public void ApprovalPackage()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nEngiSentry approval handoff failed\nReason: no active drawing\n"); VisualResultsPalette.ShowFailure("ES_APPROVALPACKAGE", null, "<none>", "No active drawing."); return; }
                ProjectDistanceCriteria? criteria = ProjectDistanceCriteriaStore.ResolveOrPrompt(document.Name);
                if (criteria == null) { WriteToActiveEditor("\nES_APPROVALPACKAGE cancelled: Project Distance Criteria were not saved.\n"); VisualResultsPalette.ShowCancelled("ES_APPROVALPACKAGE", document, System.IO.Path.GetFileName(document.Name), "Project Distance Criteria were not saved."); return; }
                DrawingScanner.Scan(document, true, out DrawingSnapshot snapshot, out _);
                ProjectEngineeringDistanceReferenceContext context = ProjectEngineeringDistanceReferenceCatalog.Create();
                string approvalDirectory = System.IO.Path.Combine(SnapshotFileWriter.ResolveArtifactsRoot(), "qa", "plotplan");
                IReadOnlyList<EngineeringDistanceApprovalReadinessItem> readiness = EngineeringDistanceApprovalReadinessService.Inspect(criteria, approvalDirectory, EngineeringDistanceApprovalAuthorityTrustStore.CanonicalPath, EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.CanonicalDirectory);
                EngineeringDistanceCandidateDefinition road = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.AccessRoadEdge);
                EngineeringDistanceCandidateDefinition fence = EngineeringDistanceCandidateCatalog.BuiltInCandidates.Single(x => x.RequiredMeasurementBasisKey == EngineeringDistanceEvidenceKeys.SecurityFence);
                string drawingName = System.IO.Path.GetFileName(document.Name) ?? string.Empty;
                EngineeringDistanceApprovalRequest roadApproval = EngineeringDistanceApprovalPreparation.BuildApprovalRequest(criteria.ProjectDirectory, road, context.ClauseRegistry, context.SourceRegistry, drawingName, snapshot.ScanTimestampUtc);
                EngineeringDistanceApprovalRequest fenceApproval = EngineeringDistanceApprovalPreparation.BuildApprovalRequest(criteria.ProjectDirectory, fence, context.ClauseRegistry, context.SourceRegistry, drawingName, snapshot.ScanTimestampUtc);
                EngineeringDistanceAuthorityPolicyRequest roadPolicy = EngineeringDistanceApprovalPreparation.BuildPolicyRequest(criteria.ProjectDirectory, road);
                EngineeringDistanceAuthorityPolicyRequest fencePolicy = EngineeringDistanceApprovalPreparation.BuildPolicyRequest(criteria.ProjectDirectory, fence);
                string packageRoot = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EngiSentry", "artifacts", "qa", "approval-handoff");
                EngineeringDistanceApprovalHandoffPackage package = EngineeringDistanceApprovalHandoffPackageBuilder.Write(packageRoot, criteria.ProjectDirectory, drawingName, snapshot.FullPath, snapshot.ScanTimestampUtc, roadApproval, roadPolicy, fenceApproval, fencePolicy, readiness);
                EngineeringDistanceApprovalHandoffVerificationResult verification = EngineeringDistanceApprovalHandoffVerifier.Verify(package.DirectoryPath);
                if (verification.Status != "VALID_HANDOFF_PACKAGE") throw new InvalidOperationException("H-B handoff package verification failed: " + string.Join(" | ", verification.Diagnostics));
                int after = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != after) throw new InvalidOperationException("DBMOD changed during approval handoff package creation.");
                document.Editor.WriteMessage("\nEngiSentry Approval Handoff Package Complete\nPackageId: {0}\nPackage directory: {1}\nAutoExecutionAllowed: {2}\nProduction authority material: NO\nPackage verification: PASS\nDBMOD unchanged: YES\nStatus: PREPARATION ONLY | NO APPROVAL GRANTED | READ-ONLY\n", package.PackageId, package.DirectoryPath, readiness.Count(x => x.AutoExecutionAllowed));
                VisualResultsPalette.ShowApprovalHandoff(document, snapshot, package, verification);
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nEngiSentry approval handoff failed\nReason: {0}: {1}\n", exception.GetType().FullName, exception.Message); VisualResultsPalette.ShowFailure("ES_APPROVALPACKAGE", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message); }
        }

        [CommandMethod("ES_APPROVALINTAKE")]
        public void ApprovalIntake()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nEngiSentry returned approval intake failed\nReason: no active drawing\n"); VisualResultsPalette.ShowFailure("ES_APPROVALINTAKE", null, "<none>", "No active drawing."); return; }
                ProjectDistanceCriteria? criteria = ProjectDistanceCriteriaStore.ResolveOrPrompt(document.Name);
                if (criteria == null) { WriteToActiveEditor("\nES_APPROVALINTAKE cancelled: Project Distance Criteria were not saved.\n"); VisualResultsPalette.ShowCancelled("ES_APPROVALINTAKE", document, System.IO.Path.GetFileName(document.Name), "Project Distance Criteria were not saved."); return; }
                DrawingScanner.Scan(document, true, out DrawingSnapshot snapshot, out _);
                string intakeRoot = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EngiSentry", "artifacts", "qa", "approval-intake");
                string incomingRoot = System.IO.Path.Combine(intakeRoot, "incoming");
                string quarantineRoot = System.IO.Path.Combine(intakeRoot, "quarantine");
                string handoffRoot = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EngiSentry", "artifacts", "qa", "approval-handoff");
                EngineeringDistanceApprovalQuarantineResult result = EngineeringDistanceApprovalQuarantineService.Intake(incomingRoot, quarantineRoot, handoffRoot, criteria.ProjectDirectory, EngineeringDistanceApprovalAuthorityTrustStore.CanonicalPath);
                int after = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != after) throw new InvalidOperationException("DBMOD changed during returned approval intake.");
                document.Editor.WriteMessage("\nEngiSentry Returned Approval Intake Complete\nStatus: {0}\nReturned files: {1}\nQuarantined files: {2}\nProduction unchanged: YES\nAutoExecutionAllowed: 0\nREAD-ONLY\n", result.Status, result.ReturnedFileCount, result.QuarantinedFileCount);
                VisualResultsPalette.ShowApprovalIntake(document, snapshot, result, incomingRoot);
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nEngiSentry returned approval intake failed\nReason: {0}: {1}\n", exception.GetType().FullName, exception.Message); VisualResultsPalette.ShowFailure("ES_APPROVALINTAKE", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message); }
        }

        [CommandMethod("ES_JEVASSIST")]
        public void JevAssist()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { VisualResultsPalette.ShowFailure("ES_JEVASSIST", null, "<none>", "No active drawing."); return; }
                if (!AutoCADHostBoundary.IsJevNetworkAllowed(AutoCADHostBoundary.IsCoreConsole()))
                {
                    document.Editor.WriteMessage("\nJEV SEMANTIC ASSIST\nStatus: JEV_DISABLED\nREAD-ONLY\n");
                    return;
                }
                DrawingScanner.Scan(document, true, out DrawingSnapshot snapshot, out PlotGeometryCapture capture);
                EngineeringDrawingModel semantic = SemanticExtractor.Extract(snapshot);
                EngineeringEntityIndex index = EntityIndexer.Build(semantic);
                PlotPlanModel plotPlan = PlotPlanBuilder.Build(index, semantic, snapshot, capture);
                List<SemanticAmbiguityCase> cases = JevSemanticAmbiguityCaseBuilder.Build(plotPlan);
                var classifier = new JevSemanticAmbiguityClassifier(new TypeSafeJevClient());
                JevSemanticAdvisorySidecar advisory = classifier.Classify(cases, JevSemanticAssistPolicy.FromEnvironment());
                int afterProcessing = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterProcessing) throw new InvalidOperationException("DBMOD changed during Jev advisory processing.");
                string path = SnapshotFileWriter.WriteJevSemanticAdvisory(advisory, snapshot.FileName, snapshot.ScanTimestampUtc);
                int afterArtifact = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterArtifact) throw new InvalidOperationException("DBMOD changed during Jev advisory sidecar creation.");
                document.Editor.WriteMessage("\nJEV SEMANTIC ASSIST\nADVISORY ONLY\nNO ENGINEERING DECISION\nAmbiguities: {0}\nClassified: {1}\nStatus: {2}\nDBMOD unchanged: YES\nREAD-ONLY\n", cases.Count, advisory.Results.Count(x => x.AdvisoryStatus == JevSemanticAdvisoryStates.Classified), advisory.Results.Select(x => x.AdvisoryStatus).FirstOrDefault() ?? JevSemanticAdvisoryStates.NoAmbiguities);
                VisualResultsPalette.ShowJevAssist(document, snapshot, advisory, path);
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nES_JEVASSIST failed\nReason: {0}\n", exception.Message); VisualResultsPalette.ShowFailure("ES_JEVASSIST", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message); }
        }

        [CommandMethod("ES_JEVCALIBRATE")]
        public void JevCalibrate()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { VisualResultsPalette.ShowFailure("ES_JEVCALIBRATE", null, "<none>", "No active drawing."); return; }
                if (!AutoCADHostBoundary.IsJevNetworkAllowed(AutoCADHostBoundary.IsCoreConsole())) { document.Editor.WriteMessage("\nJEV CALIBRATION\nStatus: JEV_DISABLED\nREAD-ONLY\n"); return; }
                DrawingScanner.Scan(document, true, out DrawingSnapshot snapshot, out PlotGeometryCapture capture);
                EngineeringDrawingModel semantic = SemanticExtractor.Extract(snapshot);
                EngineeringEntityIndex index = EntityIndexer.Build(semantic);
                PlotPlanModel plotPlan = PlotPlanBuilder.Build(index, semantic, snapshot, capture);
                List<SemanticAmbiguityCase> cases = JevSemanticAmbiguityCaseBuilder.Build(plotPlan);
                if (cases.Count == 0) { VerifyJevCalibrationDbmod(snapshot); document.Editor.WriteMessage("\nJEV CALIBRATION\nStatus: NO_AMBIGUITIES\nREAD-ONLY\n"); return; }
                JevCalibrationPackageStore store = new JevCalibrationPackageStore();
                JevCalibrationPackage package = store.CreateOrReuse(snapshot.FileName ?? "unnamed", plotPlan, cases, out string directory);
                List<JevEngineerLabel> labels = JevCalibrationPackageStore.LoadValidLabels(directory, package);
                if (labels.Count == 0)
                {
                    VerifyJevCalibrationDbmod(snapshot);
                    document.Editor.WriteMessage("\nJEV CALIBRATION\nStatus: {0}\nAmbiguities: {1}\nLabeled: 0\nDBMOD unchanged: YES\nREAD-ONLY\n", JevCalibrationStates.AwaitingEngineerLabels, cases.Count);
                    VisualResultsPalette.ShowJevCalibration(document, snapshot, package, directory, 0, JevCalibrationStates.AwaitingEngineerLabels);
                    return;
                }
                var execution = new JevCalibrationBenchmarkExecutionService().Execute(directory, package, labels, cases, JevSemanticAssistPolicy.FromEnvironment(), () => new TypeSafeJevClient());
                VerifyJevCalibrationDbmod(snapshot);
                if (execution.Status == JevCalibrationStates.BenchmarkInputChanged) throw new InvalidOperationException(JevCalibrationStates.BenchmarkInputChanged);
                if (execution.Report != null) { VisualResultsPalette.ShowJevCalibrationBenchmark(document, snapshot, package, directory, execution.Report); return; }
                document.Editor.WriteMessage("\nJEV CALIBRATION\nStatus: {0}\nDBMOD unchanged: YES\nREAD-ONLY\n", execution.Status);
                return;
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nES_JEVCALIBRATE failed\nReason: {0}\n", exception.Message); VisualResultsPalette.ShowFailure("ES_JEVCALIBRATE", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message); }
        }

        [CommandMethod("ES_JEVLABEL")]
        public void JevLabel()
        {
            try { VisualResultsPalette.ShowJevEngineerLabeling(); }
            catch (System.Exception exception) { WriteToActiveEditor("\nES_JEVLABEL failed\nReason: {0}\n", exception.Message); }
        }

        /// <summary>
        /// Maps the accepted P9 governance aggregate onto the governed-resolution lookup the Phase C projection
        /// consumes. The aggregate is the single source: no resolution is re-derived or invented here.
        /// </summary>
        private static System.Collections.Generic.IReadOnlyDictionary<string, ProjectDistanceGovernedRuleResolution> BuildGovernedResolutions(ProjectDistanceGovernanceAggregateResult governance)
        {
            if (governance == null || governance.Items == null) return new System.Collections.Generic.Dictionary<string, ProjectDistanceGovernedRuleResolution>(System.StringComparer.Ordinal);
            return governance.Items
                .Where(x => x != null && x.Resolution != null && !string.IsNullOrEmpty(x.ProjectRuleId))
                .GroupBy(x => x.ProjectRuleId!, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.OrderBy(y => y.CandidateId, StringComparer.Ordinal).First().Resolution!, StringComparer.Ordinal);
        }

        private static System.Collections.Generic.List<KnownQaRule> KnownPlotPlanRules()
        {
            return BuiltInQaRules.All.Select(x => new KnownQaRule { RuleId = x.RuleId, Severity = x.Severity }).Concat(BuiltInPlotPlanQaRules.All.Select(x => new KnownQaRule { RuleId = x.RuleId, Severity = x.Severity })).Concat(new[]
            {
                new KnownQaRule { RuleId = "PP-CLEAR-001", Severity = "Warning", DefaultEnabled = false },
                new KnownQaRule { RuleId = "PP-CLEAR-002", Severity = "Warning", DefaultEnabled = false }
            }).OrderBy(x => x.RuleId, System.StringComparer.Ordinal).ToList();
        }

        private static void WriteToActiveEditor(string message, params object[] arguments)
        {
            try
            {
                Editor? editor = Application.DocumentManager?.MdiActiveDocument?.Editor;
                if (editor != null)
                {
                    editor.WriteMessage(message, arguments);
                }
            }
            catch (System.Exception exception)
            {
                System.Diagnostics.Debug.WriteLine("EngiSentry command output error: " + exception);
            }
        }

        private static void VerifyJevCalibrationDbmod(DrawingSnapshot snapshot)
        {
            int current = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
            if (snapshot.DbModBefore != current) throw new InvalidOperationException("DBMOD changed during Jev calibration.");
        }
    }

    internal static class AutoCADHostBoundary
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool IsCoreConsole()
        {
            return string.Equals(Process.GetCurrentProcess().ProcessName, "accoreconsole", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsJevNetworkAllowed(bool isCoreConsole)
        {
            return !isCoreConsole;
        }
    }
}

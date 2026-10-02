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
                report = ExternalReferenceCompletenessGate.Apply(report, snapshot.Xrefs);
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
                System.Collections.Generic.IReadOnlyList<EquipmentPlacementResolution> engineerResolutions = ResolveEngineerResolutions(document, snapshot.InsertionUnits);
                WriteToActiveEditor("[ES_PLOTQA] Trusted engineer resolutions applied: {0}\n", engineerResolutions.Count);
                System.Collections.Generic.IReadOnlyList<AuthoritativeEquipmentCoordinate> authoritativeCoordinates = ResolveAuthoritativeCoordinates(document);
                WriteToActiveEditor("[ES_PLOTQA] Active authoritative coordinate package covers {0} occurrence(s)\n", authoritativeCoordinates.Count);
                System.Collections.Generic.IReadOnlyList<DrawingPlacementCandidateDecision> candidateDecisions = ResolveCandidateDecisions(document, snapshot.InsertionUnits);
                PlotPlanModel plotPlan = PlotPlanBuilder.Build(index, semantic, snapshot, geometryCapture, engineerResolutions, authoritativeCoordinates, candidateDecisions);
                int authoritativeResolvedCount = plotPlan.EquipmentPlacements.Count(x => string.Equals(x.AuthoritativeCoordinate?.PlacementStatus, AuthoritativePlacementStatuses.Resolved, System.StringComparison.Ordinal));
                int authoritativeConflictCount = plotPlan.EquipmentPlacements.Count(x => string.Equals(x.AuthoritativeCoordinate?.PlacementStatus, AuthoritativePlacementStatuses.Conflict, System.StringComparison.Ordinal));
                WriteToActiveEditor("[ES_PLOTQA] Authoritative evidence: resolved {0}, conflict {1}\n", authoritativeResolvedCount, authoritativeConflictCount);
                WriteToActiveEditor("[ES_PLOTQA] PlotPlan model built\n");
                QaReport baseQa = QaEngine.Evaluate(index, BuiltInQaRules.All, plotPlan, BuiltInPlotPlanQaRules.All);
                string approvalDirectory = System.IO.Path.Combine(SnapshotFileWriter.ResolveArtifactsRoot(), "qa", "plotplan");
                ProjectDistanceGovernanceAggregateResult governance = ProjectDistanceGovernanceRuntimeLoader.LoadCanonical(criteria, approvalDirectory);
                QaReport qa = ProjectEngineeringDistanceRuntime.Evaluate(criteria, plotPlan, baseQa, governance);
                qa = ExternalReferenceCompletenessGate.Apply(qa, snapshot.Xrefs);
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
                qa = ExternalReferenceCompletenessGate.Apply(qa, snapshot.Xrefs);
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
                qa = ExternalReferenceCompletenessGate.Apply(qa, snapshot.Xrefs);
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
                // The authority path is single and ordered: the accepted trust store first, then the verified
                // signed standards-authority policy intake (which contributes policies ONLY into the normal
                // registry → resolver → execution-gate chain), then the production context, then the verified
                // signed project-rule binding intake. No artifact sets AutoExecutionAllowed directly.
                EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trustedIssuers = EngineeringDistanceApprovalAuthorityTrustStore.LoadCanonical();
                PlotPlanStandardsAuthorityRuntimeContext authorityContext = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create(criteria.ProjectDirectory, trustedIssuers);
                // Production bindings come ONLY from the verified intake loader: a binding reaches the projection
                // exclusively as signed, trusted, in-scope, non-revoked, unambiguous material. With no such material
                // installed the loader returns no binding and every project rule stays fail-closed (0 auto-executable).
                System.Collections.Generic.IReadOnlyList<ProjectRuleAuthorityBinding> authorityBindings =
                    ProjectRuleAuthorityBindingCatalog.LoadCanonical(criteria.ProjectDirectory, trustedIssuers);
                System.Collections.Generic.IReadOnlyList<StandardsPackRule> governedRules = PlacementProposalGovernedRuleProjection.Build(criteria, governance, authorityContext, authorityBindings);
                PlacementProposalRunResult result = PlacementProposalRuntime.Build(plotPlan, governedRules, governance, out PlacementProposalRunDiagnostics diagnostics);
                string? proposalDrawingSha256 = TryComputeDrawingSha256(document);
                if (proposalDrawingSha256 != null)
                {
                    foreach (PlacementProposal proposal in result.Proposals)
                    {
                        proposal.SourceDrawingSha256 = proposalDrawingSha256;
                        proposal.ProposalFingerprint = PlacementProposalFingerprint.Compute(proposal);
                    }
                }
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

        [CommandMethod("ES_ENGINEERREVIEW")]
        public void EngineerReview()
        {
            try { VisualResultsPalette.ShowProjectEngineerReview(); }
            catch (System.Exception exception) { WriteToActiveEditor("\nES_ENGINEERREVIEW failed\nReason: {0}\n", exception.Message); }
        }

        /// <summary>
        /// Engineer-assisted equipment placement resolution. Only unresolved equipment placements are
        /// presented, and each is resolved exclusively by an explicit engineer pick (a model-space
        /// entity pick, or an explicit free-space point). The drawing is read only: the command scans,
        /// computes the current DWG SHA, and opens the resolution surface; picks are read-only editor
        /// prompts and no drawing modification is ever issued.
        /// </summary>
        [CommandMethod("ES_PLACEMENTRESOLVE")]
        public void PlacementResolve()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nES_PLACEMENTRESOLVE requires an active drawing.\n"); VisualResultsPalette.ShowFailure("ES_PLACEMENTRESOLVE", null, "<none>", "No active drawing."); return; }
                string? drawingSha256 = TryComputeDrawingSha256(document);
                if (drawingSha256 == null)
                {
                    WriteToActiveEditor("\nES_PLACEMENTRESOLVE requires a drawing saved on disk so the resolution can be bound to its SHA-256.\n");
                    VisualResultsPalette.ShowFailure("ES_PLACEMENTRESOLVE", document, System.IO.Path.GetFileName(document.Name), "The drawing is not saved on disk; a resolution cannot be bound to a drawing hash.");
                    return;
                }
                DrawingScanner.Scan(document, true, out DrawingSnapshot snapshot, out PlotGeometryCapture geometryCapture);
                EngineeringDrawingModel semantic = SemanticExtractor.Extract(snapshot);
                EngineeringEntityIndex index = EntityIndexer.Build(semantic);
                System.Collections.Generic.IReadOnlyList<EquipmentPlacementResolution> engineerResolutions = ResolveEngineerResolutions(document, snapshot.InsertionUnits);
                PlotPlanModel plotPlan = PlotPlanBuilder.Build(index, semantic, snapshot, geometryCapture, engineerResolutions,
                    ResolveAuthoritativeCoordinates(document), ResolveCandidateDecisions(document, snapshot.InsertionUnits));
                int afterProcessing = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterProcessing) throw new InvalidOperationException("DBMOD changed during placement resolution processing.");
                int unresolved = EquipmentPlacementResolutionQueue.CountUnresolved(plotPlan.EquipmentPlacements);
                document.Editor.WriteMessage("\nEngiSentry engineer-assisted equipment placement resolution\nDrawing: {0}\nDrawing SHA256: {1}\nUnresolved equipment entities: {2}\nTrusted engineer resolutions already applied: {3}\nDBMOD unchanged: YES\nStatus: ENGINEER INPUT REQUIRED | READ-ONLY\n", snapshot.FileName ?? "<unnamed>", drawingSha256, unresolved, engineerResolutions.Count);
                if (unresolved == 0) WriteToActiveEditor("ES_PLACEMENTRESOLVE: no unresolved equipment entities remain.\n");
                System.Collections.Generic.IReadOnlyList<PlacementProposal> proposals = PlacementProposalSnapshotWriter.ReadCurrentForDrawing(snapshot.FileName ?? "<unnamed>", drawingSha256!);
                VisualResultsPalette.ShowEquipmentPlacementResolution(plotPlan, snapshot.FileName ?? "<unnamed>", drawingSha256!, snapshot.InsertionUnits ?? string.Empty, new ProjectEngineerReviewService(), new EquipmentPlacementResolutionService(), new EditorEquipmentPlacementPickProvider(document), proposals, document.Name);
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nES_PLACEMENTRESOLVE failed\nReason: {0}: {1}\n", exception.GetType().FullName, exception.Message); VisualResultsPalette.ShowFailure("ES_PLACEMENTRESOLVE", Application.DocumentManager?.MdiActiveDocument, "<active drawing>", exception.Message); }
        }

        /// <summary>Command-line form of the same exact-occurrence review used by the palette.</summary>
        [CommandMethod("ES_PLACEMENTCANDIDATE")]
        public void PlacementCandidateReview()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) throw new InvalidOperationException("An active drawing is required.");
                string? sha = TryComputeDrawingSha256(document);
                if (sha == null) throw new InvalidOperationException("The drawing must be saved on disk.");
                DrawingScanner.Scan(document, true, out DrawingSnapshot snapshot, out PlotGeometryCapture geometry);
                EngineeringDrawingModel semantic = SemanticExtractor.Extract(snapshot);
                EngineeringEntityIndex index = EntityIndexer.Build(semantic);
                PlotPlanModel plan = PlotPlanBuilder.Build(index, semantic, snapshot, geometry,
                    ResolveEngineerResolutions(document, snapshot.InsertionUnits), ResolveAuthoritativeCoordinates(document),
                    ResolveCandidateDecisions(document, snapshot.InsertionUnits));
                PromptResult idResult = document.Editor.GetString("\nExact occurrence ID to review: ");
                if (idResult.Status != PromptStatus.OK) return;
                EquipmentPlacement? placement = plan.EquipmentPlacements.SingleOrDefault(x => x.PlacementId == idResult.StringResult);
                if (placement?.ResolutionState != PlacementResolutionState.ReviewRequired || placement.DrawingCandidate == null)
                    throw new InvalidOperationException("That exact occurrence has no current REVIEW_REQUIRED candidate.");
                DrawingPlacementCandidate candidate = placement.DrawingCandidate;
                document.Editor.WriteMessage("\nOccurrence: {0}\nTag: {1}\nX: {2} Y: {3}\nBasis: {4}\nEvidence: {5}\nHandles: TAG={6} X={7} Y={8}\nReason: {9}\n",
                    placement.PlacementId, placement.NormalizedTag, candidate.CandidateX, candidate.CandidateY,
                    candidate.CoordinateBasis, candidate.EvidenceType, candidate.TagEntityHandle,
                    candidate.XEntityHandle, candidate.YEntityHandle, candidate.EvidenceSummary);
                PromptResult actionResult = document.Editor.GetString("\nExplicit action ACCEPT or REJECT: ");
                if (actionResult.Status != PromptStatus.OK) return;
                string action = actionResult.StringResult.Trim().ToUpperInvariant();
                if (action != DrawingPlacementCandidateActions.Accept && action != DrawingPlacementCandidateActions.Reject)
                    throw new InvalidOperationException("Only ACCEPT or REJECT is valid.");
                var nameOptions = new PromptStringOptions("\nReviewer name: ") { AllowSpaces = true };
                PromptResult name = document.Editor.GetString(nameOptions);
                if (name.Status != PromptStatus.OK) return;
                var roleOptions = new PromptStringOptions("\nReviewer role: ") { AllowSpaces = true };
                PromptResult role = document.Editor.GetString(roleOptions);
                if (role.Status != PromptStatus.OK) return;
                var service = new EquipmentPlacementCandidateDecisionService();
                DrawingPlacementCandidateDecision decision = service.Record(placement, snapshot.FileName ?? System.IO.Path.GetFileName(document.Name),
                    document.Name, sha, snapshot.InsertionUnits ?? string.Empty, action, name.StringResult, role.StringResult);
                int after = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (after != snapshot.DbModBefore) throw new InvalidOperationException("DBMOD changed during candidate review.");
                document.Editor.WriteMessage("\nCandidate decision recorded: {0} {1} {2}\nDBMOD unchanged: YES\n",
                    decision.OccurrenceId, decision.Action, decision.DecisionId);
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nES_PLACEMENTCANDIDATE failed: {0}\n", exception.Message); }
        }

        /// <summary>
        /// Generates the single engineer-facing workbook <c>EngiSentry-Equipment-Coordinates.xlsx</c> for
        /// the current drawing: source-document metadata fields at the top and exactly one row per
        /// equipment occurrence with Easting, Northing and Elevation left blank.
        ///
        /// This asks the project authority for the coordinates; it never supplies or infers any. The
        /// engineer receives exactly one file and returns exactly one file — there is no JSON, no CSV,
        /// no hash and no fingerprint for the engineer to deal with. The drawing is read only: it is
        /// scanned for the inventory and nothing is written to it. No package is activated and no modal
        /// dialog is shown.
        /// </summary>
        [CommandMethod("ES_COORDREQUEST")]
        public void CoordinateRequest()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nES_COORDREQUEST requires an active drawing.\n"); return; }
                string? drawingSha256 = TryComputeDrawingSha256(document);
                if (drawingSha256 == null)
                {
                    WriteToActiveEditor("\nES_COORDREQUEST requires a drawing saved on disk so the request can be bound to its SHA-256.\n");
                    return;
                }
                DrawingScanner.Scan(document, out DrawingSnapshot snapshot);
                EngineeringDrawingModel semantic = SemanticExtractor.Extract(snapshot);
                EngineeringEntityIndex index = EntityIndexer.Build(semantic);
                PlotPlanModel plotPlan = PlotPlanBuilder.Build(index, semantic, snapshot);
                int afterProcessing = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterProcessing) throw new InvalidOperationException("DBMOD changed during coordinate request processing.");

                System.Collections.Generic.List<AuthoritativeEquipmentCoordinateInventoryEntry> inventory = BuildCoordinateInventory(plotPlan);
                AuthoritativeEquipmentCoordinateRequestResult request = new AuthoritativeEquipmentCoordinateRequestBuilder()
                    .CreateWorkbook(snapshot.FileName, drawingSha256, inventory, System.DateTime.UtcNow);

                int afterArtifact = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterArtifact) throw new InvalidOperationException("DBMOD changed during coordinate request workbook creation.");

                document.Editor.WriteMessage("\nEngiSentry authoritative equipment coordinate request\nDrawing: {0}\nDrawing SHA256: {1}\nEquipment occurrences: {2}\nWorkbook rows: {3}\nEquipment Tags: {4}\nOccurrenceIds: {5}\nBlank Eastings: {6}\nBlank Northings: {7}\nBlank Elevations: {8}\nDuplicate OccurrenceIds: {9}\nMissing occurrences: {10}\nWorkbook: {11}\nFiles the engineer must handle: 1\nDBMOD unchanged: YES\nStatus: READ-ONLY | REQUEST ONLY | NO COORDINATES SUPPLIED\n", snapshot.FileName ?? "<unnamed>", drawingSha256, inventory.Count, request.RowCount, request.NonEmptyEquipmentTagCount, request.NonEmptyOccurrenceIdCount, request.BlankEastingCount, request.BlankNorthingCount, request.BlankElevationCount, request.DuplicateOccurrenceIdCount, request.MissingOccurrenceIdCount, request.WorkbookPath);
            }
            catch (System.IO.IOException exception) { WriteToActiveEditor("\nES_COORDREQUEST failed: the output workbook already exists or cannot be created. Existing file preserved.\nReason: {0}\n", exception.Message); }
            catch (System.Exception exception) { WriteToActiveEditor("\nES_COORDREQUEST failed\nReason: {0}: {1}\n", exception.GetType().FullName, exception.Message); }
        }

        /// <summary>
        /// Imports the completed engineer-facing workbook for the current drawing.
        ///
        /// The engineer supplies exactly ONE <c>.xlsx</c> path as command-line text (never a file
        /// dialog): the same workbook that <c>ES_COORDREQUEST</c> generated, filled in and saved. The
        /// workbook is validated strictly against the current drawing and its live equipment inventory;
        /// only a fully valid workbook that covers every occurrence activates a package. A blank or
        /// cancelled input writes nothing and leaves any active package untouched. The drawing is never
        /// modified.
        /// </summary>
        [CommandMethod("ES_COORDIMPORT")]
        public void CoordinateImport()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nES_COORDIMPORT requires an active drawing.\n"); return; }
                string? drawingSha256 = TryComputeDrawingSha256(document);
                if (drawingSha256 == null)
                {
                    WriteToActiveEditor("\nES_COORDIMPORT requires a drawing saved on disk so the workbook can be bound to its SHA-256.\n");
                    return;
                }

                var options = new PromptStringOptions("\nEngiSentry-Equipment-Coordinates.xlsx path: ") { AllowSpaces = true };
                PromptResult input = document.Editor.GetString(options);
                if (input.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(input.StringResult))
                {
                    WriteToActiveEditor("\nES_COORDIMPORT cancelled: no workbook was supplied. Nothing was imported and any active package is unchanged.\n");
                    return;
                }
                string workbookPath = input.StringResult.Trim().Trim('"');

                DrawingScanner.Scan(document, out DrawingSnapshot snapshot);
                EngineeringDrawingModel semantic = SemanticExtractor.Extract(snapshot);
                EngineeringEntityIndex index = EntityIndexer.Build(semantic);
                PlotPlanModel plotPlan = PlotPlanBuilder.Build(index, semantic, snapshot);
                int afterProcessing = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterProcessing) throw new InvalidOperationException("DBMOD changed during coordinate import processing.");

                System.Collections.Generic.List<AuthoritativeEquipmentCoordinateInventoryEntry> inventory = BuildCoordinateInventory(plotPlan);
                AuthoritativeEquipmentCoordinateWorkbookImportResult workbook =
                    AuthoritativeEquipmentCoordinateWorkbookReader.Read(workbookPath, inventory, drawingSha256);

                WriteToActiveEditor("\nES_COORDIMPORT workbook validation\nWorkbook: {0}\nStatus: {1}\n", workbook.WorkbookPath, workbook.Status);
                foreach (string message in workbook.Messages) WriteToActiveEditor("  {0}\n", message);

                if (!workbook.IsValid || workbook.Validation == null || workbook.SourceWorkbookBytes == null)
                {
                    WriteToActiveEditor("\nES_COORDIMPORT REJECTED: the workbook was not installed or activated. Any existing active package is unchanged.\nStatus: READ-ONLY\n");
                    return;
                }

                AuthoritativeEquipmentCoordinateValidationResult validation = workbook.Validation;
                var store = new AuthoritativeEquipmentCoordinatePackageStore();
                AuthoritativeEquipmentCoordinateActivePackage? active = store.ReadActive(drawingSha256);
                bool alreadyActive = active != null
                    && string.Equals(active.PackageFingerprint, validation.PackageFingerprint, System.StringComparison.Ordinal)
                    && string.Equals(active.SourceWorkbookSha256, workbook.SourceWorkbookSha256, System.StringComparison.Ordinal);
                if (alreadyActive)
                {
                    WriteToActiveEditor("\nES_COORDIMPORT ALREADY_ACTIVE: this exact workbook is already active. Nothing changed.\nStatus: READ-ONLY\n");
                    return;
                }
                if (active != null)
                {
                    var confirm = new PromptKeywordOptions("\nA different authoritative coordinate package is already active. Replace it with the validated workbook? [Yes/No]: ", "Yes No") { AllowNone = true };
                    PromptResult answer = document.Editor.GetKeywords(confirm);
                    if (answer.Status != PromptStatus.OK || !string.Equals(answer.StringResult, "Yes", System.StringComparison.OrdinalIgnoreCase))
                    {
                        WriteToActiveEditor("\nES_COORDIMPORT DECLINED: the current active package was kept. Nothing changed.\nStatus: READ-ONLY\n");
                        return;
                    }
                }

                var provenance = new AuthoritativeEquipmentCoordinateWorkbookProvenance
                {
                    SourceWorkbookSha256 = workbook.SourceWorkbookSha256,
                    RequestFingerprint = workbook.RequestFingerprint,
                    SourceDocumentNumber = workbook.Metadata?.SourceDocumentNumber ?? string.Empty,
                    SourceRevision = workbook.Metadata?.SourceRevision ?? string.Empty,
                    SourceIssueStatus = workbook.Metadata?.SourceIssueStatus ?? string.Empty,
                    IssueDate = workbook.Metadata?.IssueDate ?? string.Empty
                };

                AuthoritativeEquipmentCoordinateImportReceipt receipt = store.InstallWorkbook(
                    drawingSha256,
                    workbook.NormalizedManifestBytes!,
                    workbook.NormalizedScheduleBytes!,
                    workbook.SourceWorkbookBytes!,
                    provenance,
                    validation,
                    active?.PackageFingerprint);

                int afterArtifact = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterArtifact) throw new InvalidOperationException("DBMOD changed during coordinate import artifact creation.");

                document.Editor.WriteMessage("\nEngiSentry authoritative equipment coordinate import complete\nStatus: {0}\nDrawing: {1}\nDrawing SHA256: {2}\nSource type: XLSX\nSource Document Number: {3}\nSource Revision: {4}\nSource Issue Status: {5}\nIssued By: {6}\nIssue Date: {7}\nSource workbook SHA256: {8}\nPackage fingerprint: {9}\nOccurrences activated: {10}\nDBMOD unchanged: YES\nStatus: READ-ONLY (DRAWING UNCHANGED)\n", receipt.Status, snapshot.FileName ?? "<unnamed>", drawingSha256, receipt.SourceDocumentNumber, receipt.SourceRevision, receipt.SourceIssueStatus, receipt.IssuedBy, receipt.IssueDate, receipt.SourceWorkbookSha256, receipt.PackageFingerprint, receipt.OccurrenceCount);
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nES_COORDIMPORT failed\nReason: {0}: {1}\n", exception.GetType().FullName, exception.Message); }
        }

        /// <summary>
        /// Reports the authoritative coordinate package state for the current drawing in user-facing
        /// terms. Read only: it writes nothing, does not touch the drawing, and never exposes internal
        /// manifest, schedule or store paths.
        /// </summary>
        [CommandMethod("ES_COORDSTATUS")]
        public void CoordinateStatus()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nES_COORDSTATUS\nDrawing: <none>\nStatus: NO ACTIVE AUTHORITATIVE COORDINATE PACKAGE\nREAD-ONLY\n"); return; }
                string drawingName = string.IsNullOrWhiteSpace(document.Name) ? "<unnamed>" : System.IO.Path.GetFileName(document.Name);
                string? drawingSha256 = TryComputeDrawingSha256(document);
                if (drawingSha256 == null)
                {
                    WriteToActiveEditor("\nES_COORDSTATUS\nDrawing: {0}\nDrawing is not saved on disk.\nCurrent drawing binding: NOT VERIFIED\nStatus: NO ACTIVE AUTHORITATIVE COORDINATE PACKAGE\nStatus: READ-ONLY\n", drawingName);
                    return;
                }
                int dbmodBefore = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                var provider = new AuthoritativeEquipmentCoordinateProvider();
                AuthoritativeEquipmentCoordinateActivePackage? currentActive = provider.GetActive(drawingSha256);
                string? currentRequestFingerprint = null;
                bool drawingModified = dbmodBefore != 0;
                if (currentActive != null && !drawingModified)
                {
                    DrawingScanner.Scan(document, out DrawingSnapshot snapshot);
                    EngineeringDrawingModel semantic = SemanticExtractor.Extract(snapshot);
                    EngineeringEntityIndex index = EntityIndexer.Build(semantic);
                    PlotPlanModel plotPlan = PlotPlanBuilder.Build(index, semantic, snapshot);
                    currentRequestFingerprint = AuthoritativeEquipmentCoordinateRequestFingerprint.Compute(
                        AuthoritativeEquipmentCoordinateWorkbookSchema.Current, drawingSha256, BuildCoordinateInventory(plotPlan));
                    int afterScan = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                    drawingModified = snapshot.DbModBefore != afterScan || afterScan != dbmodBefore;
                }
                AuthoritativeEquipmentCoordinateStatusSnapshot status = provider.InspectStatus(
                    drawingSha256, currentRequestFingerprint, drawingModified);
                int dbmodAfter = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                bool dbmodUnchanged = dbmodBefore == dbmodAfter;
                if (!dbmodUnchanged)
                    status = provider.InspectStatus(drawingSha256, currentRequestFingerprint, true);
                AuthoritativeEquipmentCoordinateActivePackage? active = status.ActivePackage;
                if (active == null)
                {
                    string state = status.State == AuthoritativeEquipmentCoordinateStatusState.NoActivePackage
                        ? "NO ACTIVE AUTHORITATIVE COORDINATE PACKAGE" : "ACTIVE PACKAGE MISMATCHED TO CURRENT DRAWING";
                    WriteToActiveEditor("\nES_COORDSTATUS\nDrawing: {0}\nActive coordinate package: NO FOR THIS DRAWING\nCurrent drawing binding: {1}\nReason: {2}\nDBMOD unchanged: {3}\nStatus: {4} | READ-ONLY\n",
                        drawingName, status.State == AuthoritativeEquipmentCoordinateStatusState.NoActivePackage ? "NO ACTIVE PACKAGE" : "MISMATCH",
                        status.Reason, dbmodUnchanged ? "YES" : "NO", state);
                    return;
                }
                string sourceType = string.IsNullOrWhiteSpace(active.SourceWorkbookSha256) ? "PROJECT_PACKAGE" : "XLSX";
                string binding = status.State == AuthoritativeEquipmentCoordinateStatusState.ActiveValid ? "VALID" : "MISMATCH";
                document.Editor.WriteMessage("\nES_COORDSTATUS\nDrawing: {0}\nActive coordinate package: YES\nSource type: {1}\nCoverage: {2}/{3}\nCurrent drawing binding: {4}\nBinding reason: {5}\nCoordinateUnits: {6}\nCoordinateBasis: {7}\nSource Document Number: {8}\nSource Revision: {9}\nSource Issue Status: {10}\nIssued By: {11}\nIssue Date: {12}\nSource workbook SHA256: {13}\nPackage fingerprint: {14}\nActivated (UTC): {15}\nDBMOD unchanged: {16}\nStatus: {17} | READ-ONLY\n",
                    drawingName, sourceType, status.ValidOccurrenceCount, active.OccurrenceCount, binding, status.Reason,
                    AuthoritativeEquipmentCoordinateSchema.CoordinateUnitsMeters, AuthoritativeEquipmentCoordinateSchema.CoordinateBasisPilotWcs,
                    active.SourceDocumentNumber ?? "<none>", active.SourceRevision ?? "<none>", active.SourceIssueStatus ?? "<none>",
                    active.IssuedBy, active.IssueDate ?? "<none>", active.SourceWorkbookSha256 ?? "<none>", active.PackageFingerprint,
                    active.ActivatedTimestampUtc, dbmodUnchanged ? "YES" : "NO", binding == "VALID" ? "ACTIVE PACKAGE VALID" : "ACTIVE PACKAGE MISMATCHED");
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nES_COORDSTATUS failed\nReason: {0}: {1}\n", exception.GetType().FullName, exception.Message); }
        }

        /// <summary>
        /// The current drawing's equipment occurrence inventory, deterministically ordered. The
        /// occurrence identity is the existing canonical <c>EquipmentPlacement.PlacementId</c>
        /// (<c>placement:&lt;equipment canonical id&gt;:&lt;tag handle&gt;</c>), which is stable, derived
        /// from the tag and its handle, and independent of enumeration order.
        /// </summary>
        private static System.Collections.Generic.List<AuthoritativeEquipmentCoordinateInventoryEntry> BuildCoordinateInventory(PlotPlanModel plotPlan)
        {
            var inventory = new System.Collections.Generic.List<AuthoritativeEquipmentCoordinateInventoryEntry>();
            int index = 0;
            foreach (EquipmentPlacement placement in plotPlan.EquipmentPlacements.OrderBy(x => x.PlacementId, System.StringComparer.Ordinal))
            {
                index++;
                inventory.Add(new AuthoritativeEquipmentCoordinateInventoryEntry
                {
                    OccurrenceId = placement.PlacementId,
                    EquipmentTag = placement.NormalizedTag,
                    OccurrenceIndex = index,
                    SourceEntityHandle = placement.TagSourceHandle,
                    Layer = placement.TagSourceLayer ?? string.Empty,
                    CurrentPlacementStatus = placement.PlacementStatus,
                    CurrentPlacementReason = "Easting=" + placement.Easting.Status + "; Northing=" + placement.Northing.Status + "; Centerline=" + (placement.CenterlineEvidence?.Status ?? "None")
                });
            }
            return inventory;
        }

        /// <summary>
        /// Computes the SHA-256 of the on-disk DWG so a resolution can be bound to the exact drawing
        /// revision. The file is opened read-only; a missing or unreadable file yields null.
        /// </summary>
        private static string? TryComputeDrawingSha256(Document? document)
        {
            try
            {
                string? path = document?.Name;
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path)) return null;
                return HumanEngineeringReviewDecisionFileStore.ComputeSha256(path!);
            }
            catch (System.Exception) { return null; }
        }

        /// <summary>
        /// Loads the trusted engineer resolutions for the current drawing and units. A missing hash,
        /// missing units, or any resolution-store problem simply yields no resolutions: unresolved
        /// equipment stays unresolved and never blocks the rest of the QA run.
        /// </summary>
        private static System.Collections.Generic.IReadOnlyList<EquipmentPlacementResolution> ResolveEngineerResolutions(Document? document, string? insertionUnits)
        {
            try
            {
                string? drawingSha256 = TryComputeDrawingSha256(document);
                if (drawingSha256 == null || string.IsNullOrWhiteSpace(insertionUnits)) return System.Array.Empty<EquipmentPlacementResolution>();
                return new EquipmentPlacementResolutionService().ResolveTrustedForDrawing(drawingSha256, insertionUnits!);
            }
            catch (System.Exception) { return System.Array.Empty<EquipmentPlacementResolution>(); }
        }

        private static System.Collections.Generic.IReadOnlyList<DrawingPlacementCandidateDecision> ResolveCandidateDecisions(Document? document, string? insertionUnits)
        {
            try
            {
                string? sha = TryComputeDrawingSha256(document);
                if (sha == null || string.IsNullOrWhiteSpace(insertionUnits)) return System.Array.Empty<DrawingPlacementCandidateDecision>();
                return new EquipmentPlacementCandidateDecisionService().ResolveForDrawing(sha, insertionUnits!);
            }
            catch (System.Exception) { return System.Array.Empty<DrawingPlacementCandidateDecision>(); }
        }

        /// <summary>
        /// Loads the authoritative equipment coordinates from the drawing's active coordinate package.
        /// A missing hash, a missing package, or any store problem simply yields no coordinates: the
        /// existing placement behaviour is left completely unchanged and nothing is ever inferred.
        /// </summary>
        private static System.Collections.Generic.IReadOnlyList<AuthoritativeEquipmentCoordinate> ResolveAuthoritativeCoordinates(Document? document)
        {
            try
            {
                string? drawingSha256 = TryComputeDrawingSha256(document);
                if (drawingSha256 == null) return System.Array.Empty<AuthoritativeEquipmentCoordinate>();
                return new AuthoritativeEquipmentCoordinateProvider().ResolveForDrawing(drawingSha256);
            }
            catch (System.Exception) { return System.Array.Empty<AuthoritativeEquipmentCoordinate>(); }
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

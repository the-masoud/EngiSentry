using System;
using System.Linq;
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

namespace EngiSentry.AutoCAD2021
{
    public sealed class Commands
    {
        [CommandMethod("ES_HELLO")]
        public void Hello()
        {
            WriteToActiveEditor("\nEngiSentry M0.1\nAutoCAD connection: OK\n");
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
                    return;
                }

                string drawingName = string.IsNullOrWhiteSpace(document.Name)
                    ? "<unnamed>"
                    : System.IO.Path.GetFileName(document.Name);
                document.Editor.WriteMessage(
                    "\nEngiSentry\nAutoCAD connection: OK\nDrawing: {0}\nStatus: READY\n",
                    drawingName);
            }
            catch (System.Exception exception)
            {
                WriteToActiveEditor("\nEngiSentry error: {0}\n", exception.Message);
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
                    return;
                }

                DrawingScanner.Scan(document, out EngiSentry.Contracts.DrawingSnapshot snapshot);
                string path = SnapshotFileWriter.Write(snapshot);
                int unresolved = 0;
                foreach (EngiSentry.Contracts.XrefSnapshot xref in snapshot.Xrefs) if (!xref.IsResolved) unresolved++;
                document.Editor.WriteMessage("\nEngiSentry M0.2 Scan Complete\nDrawing: {0}\nModel entities: {1}\nPaper entities: {2}\nBlocks: {3}\nBlock references: {4}\nXrefs: {5}\nUnresolved Xrefs: {6}\nSnapshot: {7}\nDBMOD unchanged: {8}\nStatus: READ-ONLY\n", snapshot.FileName ?? "<unnamed>", snapshot.ModelSpaceEntityCount, snapshot.PaperSpaceEntityCount, snapshot.BlockDefinitions.Count, snapshot.BlockReferenceCount, snapshot.Xrefs.Count, unresolved, path, snapshot.DrawingModifiedByScan ? "NO" : "YES");
            }
            catch (System.Exception exception)
            {
                WriteToActiveEditor("\nEngiSentry M0.2 Scan Failed\nReason: {0}\n", exception.Message);
            }
        }

        [CommandMethod("ES_SEMANTIC")]
        public void Semantic()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nEngiSentry M0.3 Semantic Extraction Failed\nReason: no active drawing\n"); return; }
                DrawingScanner.Scan(document, out DrawingSnapshot snapshot);
                EngineeringDrawingModel model = SemanticExtractor.Extract(snapshot);
                int afterExtraction = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterExtraction) throw new InvalidOperationException("DBMOD changed during semantic processing.");
                string path = SnapshotFileWriter.WriteSemantic(model, snapshot.FileName, snapshot.ScanTimestampUtc);
                int afterArtifact = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterArtifact) throw new InvalidOperationException("DBMOD changed during semantic artifact creation.");
                SemanticStatistics statistics = model.Statistics;
                document.Editor.WriteMessage("\nEngiSentry M0.3 Semantic Extraction Complete\nDrawing: {0}\nEquipment tag occurrences: {1}\nUnique equipment tags: {2}\nStructure tag occurrences: {3}\nCoordinates: {4}\nElevations: {5}\nRoad labels: {6}\nBoundary labels: {7}\nEquipment list rows: {8}\nSemantic model: {9}\nDBMOD unchanged: YES\nStatus: READ-ONLY\n", snapshot.FileName ?? "<unnamed>", statistics.EquipmentTagOccurrences, statistics.UniqueEquipmentTags, statistics.StructureTagOccurrences, statistics.CoordinateCount, statistics.ElevationCount, statistics.RoadLabelCount, statistics.BoundaryLabelCount, statistics.EquipmentListEntryCount, path);
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nEngiSentry M0.3 Semantic Extraction Failed\nReason: {0}\n", exception.Message); }
        }

        [CommandMethod("ES_INDEX")]
        public void Index()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nEngiSentry M0.4 Entity Index Failed\nReason: no active drawing\n"); return; }
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
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nEngiSentry M0.4 Entity Index Failed\nReason: {0}\n", exception.Message); }
        }

        [CommandMethod("ES_QA")]
        public void Qa()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nEngiSentry M1.0 QA Failed\nReason: no active drawing\n"); return; }
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
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nEngiSentry M1.0 QA Failed\nReason: {0}\n", exception.Message); }
        }

        [CommandMethod("ES_PLOTQA")]
        public void PlotPlanQa()
        {
            try
            {
                WriteToActiveEditor("\n[ES_PLOTQA] START\n");
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nEngiSentry M2.2 Plot Plan QA Failed\nReason: no active drawing\n"); return; }
                DrawingScanner.Scan(document, true, out DrawingSnapshot snapshot, out PlotGeometryCapture geometryCapture);
                WriteToActiveEditor("[ES_PLOTQA] Snapshot built\n");
                EngineeringDrawingModel semantic = SemanticExtractor.Extract(snapshot);
                WriteToActiveEditor("[ES_PLOTQA] Semantic model built\n");
                EngineeringEntityIndex index = EntityIndexer.Build(semantic);
                WriteToActiveEditor("[ES_PLOTQA] Entity index built\n");
                PlotPlanModel plotPlan = PlotPlanBuilder.Build(index, semantic, snapshot, geometryCapture);
                WriteToActiveEditor("[ES_PLOTQA] PlotPlan model built\n");
                QaReport qa = QaEngine.Evaluate(index, BuiltInQaRules.All, plotPlan, BuiltInPlotPlanQaRules.All);
                WriteToActiveEditor("[ES_PLOTQA] QA complete\n");
                int afterProcessing = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterProcessing) throw new InvalidOperationException("DBMOD changed during plot-plan QA processing.");
                string path = SnapshotFileWriter.WritePlotPlanQaReport(new PlotPlanQaReport { PlotPlan = plotPlan, Qa = qa }, snapshot.FileName, snapshot.ScanTimestampUtc);
                WriteToActiveEditor("[ES_PLOTQA] Artifact written\n");
                int afterArtifact = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterArtifact) throw new InvalidOperationException("DBMOD changed during plot-plan QA artifact creation.");
                PlotPlanStatistics s = plotPlan.Statistics;
                PlotGeometryStatistics g = plotPlan.Geometry.Statistics;
                document.Editor.WriteMessage("\nEngiSentry M2.2 Plot Plan QA Complete\nDrawing: {0}\nEquipment placements: {1}\nComplete placements: {2}\nIncomplete placements: {3}\nAmbiguous placements: {4}\nGeometry primitives: {5}\nGeometry associations: {6}\nResolved geometry associations: {7}\nMissing geometry associations: {8}\nAmbiguous geometry associations: {9}\nSpatial constraints: {10}\nResolved spatial constraints: {11}\nMissing spatial constraints: {12}\nAmbiguous spatial constraints: {13}\nRules executed: {14}\nFindings: {15}\nInfo: {16}\nWarnings: {17}\nErrors: {18}\nOverall status: {19}\nQA report: {20}\nDBMOD unchanged: YES\nStatus: READ-ONLY\n", snapshot.FileName ?? "<unnamed>", s.EquipmentPlacementCount, s.CompletePlacementCount, s.IncompletePlacementCount, s.AmbiguousPlacementCount, g.SupportedPrimitiveCount, g.GeometryAssociationCount, g.ResolvedAssociationCount, g.MissingAssociationCount, g.AmbiguousAssociationCount, g.SpatialConstraintCount, g.ResolvedSpatialConstraintCount, g.MissingSpatialConstraintCount, g.AmbiguousSpatialConstraintCount, qa.Statistics.RuleCount, qa.Statistics.TotalFindings, qa.Statistics.InfoFindings, qa.Statistics.WarningFindings, qa.Statistics.ErrorFindings, qa.OverallStatus, path);
            }
            catch (System.Exception exception) { WriteToActiveEditor("\nEngiSentry M2.2 Plot Plan QA Failed\nReason: {0}: {1}\n", exception.GetType().FullName, exception.Message); }
        }

        [CommandMethod("ES_PROFILEQA")]
        public void ProfileQa()
        {
            try
            {
                Document? document = Application.DocumentManager?.MdiActiveDocument;
                if (document == null) { WriteToActiveEditor("\nEngiSentry M3.0 Profile QA Failed\nReason: no active drawing\n"); return; }
                PromptResult prompt = document.Editor.GetString(new PromptStringOptions("\nProfile JSON path: ") { AllowSpaces = true });
                if (prompt.Status != PromptStatus.OK) { document.Editor.WriteMessage("\nEngiSentry M3.0 Profile QA Cancelled\n"); return; }
                ProfileResolution resolution = ProfileResolver.Resolve(KnownPlotPlanRules(), ProfileJson.ReadFile(prompt.StringResult));
                DrawingScanner.Scan(document, true, out DrawingSnapshot snapshot, out PlotGeometryCapture geometryCapture);
                EngineeringDrawingModel semantic = SemanticExtractor.Extract(snapshot);
                EngineeringEntityIndex index = EntityIndexer.Build(semantic);
                PlotPlanModel plotPlan = PlotPlanBuilder.Build(index, semantic, snapshot, geometryCapture);
                QaReport qa = QaEngine.EvaluateWithProfile(index, BuiltInQaRules.All, plotPlan, BuiltInPlotPlanQaRules.All, resolution);
                int afterProcessing = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterProcessing) throw new InvalidOperationException("DBMOD changed during profile QA processing.");
                string path = SnapshotFileWriter.WriteProfilePlotPlanQaReport(new PlotPlanQaReport { PlotPlan = plotPlan, Qa = qa }, snapshot.FileName, snapshot.ScanTimestampUtc);
                int afterArtifact = Convert.ToInt32(Application.GetSystemVariable("DBMOD"), System.Globalization.CultureInfo.InvariantCulture);
                if (snapshot.DbModBefore != afterArtifact) throw new InvalidOperationException("DBMOD changed during profile QA artifact creation.");
                document.Editor.WriteMessage("\nEngiSentry M3.0 Profile QA Complete\nDrawing: {0}\nEffective profiles: {1}\nRules executed: {2}\nFindings: {3}\nInfo: {4}\nWarnings: {5}\nErrors: {6}\nOverall status: {7}\nQA report: {8}\nDBMOD unchanged: YES\nStatus: READ-ONLY\n", snapshot.FileName ?? "<unnamed>", string.Join(" -> ", resolution.AppliedProfileIds), qa.Statistics.RuleCount, qa.Statistics.TotalFindings, qa.Statistics.InfoFindings, qa.Statistics.WarningFindings, qa.Statistics.ErrorFindings, qa.OverallStatus, path);
            }
            catch (ProfileValidationException exception) { WriteToActiveEditor("\nEngiSentry M3.0 Profile QA Failed\nReason: {0}\n", exception.Message); }
            catch (System.Exception exception) { WriteToActiveEditor("\nEngiSentry M3.0 Profile QA Failed\nReason: {0}: {1}\n", exception.GetType().FullName, exception.Message); }
        }

        private static System.Collections.Generic.List<KnownQaRule> KnownPlotPlanRules()
        {
            return BuiltInQaRules.All.Select(x => new KnownQaRule { RuleId = x.RuleId, Severity = x.Severity }).Concat(BuiltInPlotPlanQaRules.All.Select(x => new KnownQaRule { RuleId = x.RuleId, Severity = x.Severity })).OrderBy(x => x.RuleId, System.StringComparer.Ordinal).ToList();
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
    }
}

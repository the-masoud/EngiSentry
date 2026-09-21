#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Windows;
using EngiSentry.Contracts;
using EngiSentry.Revision;
using EngiSentry.Standards;
using EngiSentry.AutoCAD2021.Scanning;

namespace EngiSentry.AutoCAD2021
{
    internal static class VisualResultsPalette
    {
        public static void ShowHome(string command, Document document, string drawing, string status, string detail)
        {
            Publish(new VisualResultsData
            {
                Command = command,
                Title = "EngiSentry",
                Drawing = drawing,
                Status = status,
                StatusDetail = detail,
                Document = document,
                Metrics = new List<VisualMetric>
                {
                    new VisualMetric("Mode", "Read-only"),
                    new VisualMetric("DWG changes", "None"),
                    new VisualMetric("Output", "Not applicable")
                },
                Details = new List<VisualDetail>
                {
                    new VisualDetail("Command", command),
                    new VisualDetail("Connection", detail)
                }
            });
        }

        public static void ShowScan(Document document, DrawingSnapshot snapshot, string outputPath)
        {
            int unresolved = snapshot.Xrefs.Count(x => !x.IsResolved);
            Publish(new VisualResultsData
            {
                Command = "ES_SCAN",
                Title = "Drawing Scan",
                Drawing = snapshot.FileName,
                Status = "COMPLETE",
                StatusDetail = "Read-only scan completed",
                OutputPath = outputPath,
                Document = document,
                Metrics = new List<VisualMetric>
                {
                    new VisualMetric("Model entities", snapshot.ModelSpaceEntityCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Paper entities", snapshot.PaperSpaceEntityCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Blocks", snapshot.BlockDefinitions.Count.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Unresolved Xrefs", unresolved.ToString(CultureInfo.InvariantCulture))
                },
                Details = new List<VisualDetail>
                {
                    new VisualDetail("Block references", snapshot.BlockReferenceCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualDetail("Xrefs", snapshot.Xrefs.Count.ToString(CultureInfo.InvariantCulture)),
                    new VisualDetail("Scan duration", snapshot.ScanDurationMs.ToString(CultureInfo.InvariantCulture) + " ms"),
                    new VisualDetail("DBMOD guard", snapshot.DrawingModifiedByScan ? "Changed" : "Unchanged"),
                    new VisualDetail("Snapshot", outputPath)
                }
            });
        }

        public static void ShowSemantic(Document document, DrawingSnapshot snapshot, EngineeringDrawingModel model, string outputPath)
        {
            SemanticStatistics s = model.Statistics;
            Publish(new VisualResultsData
            {
                Command = "ES_SEMANTIC",
                Title = "Semantic Extraction",
                Drawing = snapshot.FileName,
                Status = "COMPLETE",
                StatusDetail = "Semantic model built",
                OutputPath = outputPath,
                Document = document,
                Metrics = new List<VisualMetric>
                {
                    new VisualMetric("Equipment tags", s.EquipmentTagOccurrences.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Unique tags", s.UniqueEquipmentTags.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Structure tags", s.StructureTagOccurrences.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Coordinates", s.CoordinateCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Elevations", s.ElevationCount.ToString(CultureInfo.InvariantCulture))
                },
                Details = new List<VisualDetail>
                {
                    new VisualDetail("Road labels", s.RoadLabelCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualDetail("Boundary labels", s.BoundaryLabelCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualDetail("Equipment list rows", s.EquipmentListEntryCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualDetail("Semantic model", outputPath),
                    new VisualDetail("DBMOD guard", "Unchanged")
                }
            });
        }

        public static void ShowIndex(Document document, DrawingSnapshot snapshot, EngineeringEntityIndex index, string outputPath)
        {
            EntityIndexStatistics s = index.Statistics;
            Publish(new VisualResultsData
            {
                Command = "ES_INDEX",
                Title = "Entity Index",
                Drawing = snapshot.FileName,
                Status = "COMPLETE",
                StatusDetail = "Entity index built",
                OutputPath = outputPath,
                Document = document,
                Metrics = new List<VisualMetric>
                {
                    new VisualMetric("Equipment entities", s.EquipmentEntityCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Structure entities", s.StructureEntityCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("With both sources", s.EquipmentEntitiesWithBoth.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Grouped tags", s.GroupedTagCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Unresolved links", s.UnresolvedLinkCount.ToString(CultureInfo.InvariantCulture))
                },
                Details = new List<VisualDetail>
                {
                    new VisualDetail("Drawing evidence", s.EquipmentEntitiesWithDrawingEvidence.ToString(CultureInfo.InvariantCulture)),
                    new VisualDetail("Equipment-list evidence", s.EquipmentEntitiesWithEquipmentListEvidence.ToString(CultureInfo.InvariantCulture)),
                    new VisualDetail("Grouped member links", s.GroupedMemberLinkCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualDetail("Ambiguous links", s.AmbiguousLinkCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualDetail("Entity index", outputPath)
                }
            });
        }

        public static void ShowQa(Document document, DrawingSnapshot snapshot, QaReport report, string outputPath, string command, string title, string note)
        {
            QaStatistics s = report.Statistics;
            Publish(new VisualResultsData
            {
                Command = command,
                Title = title,
                Drawing = snapshot.FileName,
                Status = report.OverallStatus,
                StatusDetail = "QA evaluation completed",
                OutputPath = outputPath,
                Document = document,
                Note = note,
                Metrics = QaMetrics(s),
                QaFindings = report.Findings
            });
        }

        public static void ShowPlotQa(Document document, DrawingSnapshot snapshot, PlotPlanQaReport report, string outputPath, StandardAuthorityReviewPackage standardsReviewPackage = null, string standardsReviewPackagePath = null, string standardsReviewPackageSha256 = null)
        {
            PlotPlanModel plotPlan = report.PlotPlan;
            QaReport qaReport = report.Qa;
            PlotPlanStatistics p = plotPlan.Statistics;
            PlotGeometryStatistics g = plotPlan.Geometry.Statistics;
            QaStatistics q = qaReport.Statistics;
            EngineeringDistanceRunSummary distanceSummary = qaReport.EngineeringDistanceSummary;
            List<VisualMetric> metrics = new List<VisualMetric>
            {
                new VisualMetric("Rules", q.RuleCount.ToString(CultureInfo.InvariantCulture)),
                new VisualMetric("Findings", q.TotalFindings.ToString(CultureInfo.InvariantCulture)),
                new VisualMetric("Warnings", q.WarningFindings.ToString(CultureInfo.InvariantCulture)),
                new VisualMetric("Errors", q.ErrorFindings.ToString(CultureInfo.InvariantCulture))
            };
            Publish(new VisualResultsData
            {
                Command = "ES_PLOTQA",
                Title = "Plot Plan QA",
                Drawing = snapshot.FileName,
                Status = qaReport.OverallStatus,
                StatusDetail = "Plot-plan QA evaluation completed",
                OutputPath = outputPath,
                Document = document,
                Note = "Placements: " + p.EquipmentPlacementCount.ToString(CultureInfo.InvariantCulture) +
                       " | Incomplete: " + p.IncompletePlacementCount.ToString(CultureInfo.InvariantCulture) +
                       " | Geometry gaps: " + g.MissingAssociationCount.ToString(CultureInfo.InvariantCulture) +
                       " | Spatial gaps: " + g.MissingSpatialConstraintCount.ToString(CultureInfo.InvariantCulture),
                Metrics = metrics,
                QaFindings = qaReport.Findings,
                DistanceRows = PlotPlanDistanceRows(distanceSummary, plotPlan.Measurements),
                AuthorityReviewItems = report.ReviewProjection == null
                    ? new List<StandardAuthorityReviewItem>()
                    : report.ReviewProjection.ReviewItems,
                StandardsReviewPackage = standardsReviewPackage,
                StandardsReviewPackagePath = standardsReviewPackagePath,
                StandardsReviewPackageSha256 = standardsReviewPackageSha256
            });
        }

        public static void ShowPlacementProposals(Document document, DrawingSnapshot snapshot, PlacementProposalRunResult result, PlacementProposalRunDiagnostics diagnostics, string outputPath)
        {
            PlacementProposalRunResult safeResult = result ?? new PlacementProposalRunResult();
            PlacementProposalRunDiagnostics safeDiagnostics = diagnostics ?? new PlacementProposalRunDiagnostics();
            List<PlacementProposal> proposals = (safeResult.Proposals ?? new List<PlacementProposal>())
                .Where(x => x != null)
                .OrderBy(x => x.ProposalId ?? string.Empty, StringComparer.Ordinal)
                .ToList();

            Publish(new VisualResultsData
            {
                Command = "ES_SUGGEST",
                Title = "Placement Proposals",
                Drawing = snapshot == null ? "<none>" : snapshot.FileName,
                Status = "COMPLETE",
                StatusDetail = "Read-only placement proposal projection completed",
                OutputPath = outputPath,
                Document = document,
                Note = "No drawing changes were made. Zero proposals is a valid governed result.",
                Metrics = new List<VisualMetric>
                {
                    new VisualMetric("Governed rules", safeDiagnostics.GovernedRuleCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Bounded constraints", safeDiagnostics.BoundedConstraintCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Violated constraints", safeDiagnostics.ViolatedCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Review-required constraints", safeDiagnostics.ReviewRequiredCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Insufficient-evidence constraints", safeDiagnostics.InsufficientEvidenceCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Executable violations", safeDiagnostics.ExecutableViolatedCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Placement proposals", proposals.Count.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Mode", "READ-ONLY")
                },
                Details = PlacementProposalPresentation.BuildDetails(proposals, safeDiagnostics, outputPath)
            });
        }

        public static void ShowApprovalPreparation(Document document, DrawingSnapshot snapshot, IReadOnlyList<EngineeringDistanceApprovalReadinessItem> readiness, IReadOnlyList<string> approvalRequestPaths, IReadOnlyList<string> policyRequestPaths)
        {
            List<EngineeringDistanceApprovalReadinessItem> items = (readiness ?? new List<EngineeringDistanceApprovalReadinessItem>()).OrderBy(x => x.CandidateId, StringComparer.Ordinal).ToList();
            Publish(new VisualResultsData
            {
                Command = "ES_APPROVALPREP",
                Title = "Approval Onboarding Preparation",
                Drawing = snapshot == null ? "<none>" : snapshot.FileName,
                Status = "PREPARATION ONLY",
                StatusDetail = "NO APPROVAL GRANTED | READ-ONLY",
                Document = document,
                Note = "Request artifacts are informational only and are not approval, authority policy, trust data, or activation artifacts.",
                Metrics = new List<VisualMetric>
                {
                    new VisualMetric("Candidates", items.Count.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Executable", items.Count(x => x.AutoExecutionAllowed).ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Approval requests", (approvalRequestPaths ?? new List<string>()).Count.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Policy requests", (policyRequestPaths ?? new List<string>()).Count.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Mode", "READ-ONLY")
                },
                Details = ApprovalPreparationDetails(items, approvalRequestPaths, policyRequestPaths)
            });
        }

        private static List<VisualDetail> ApprovalPreparationDetails(IReadOnlyList<EngineeringDistanceApprovalReadinessItem> items, IReadOnlyList<string> approvalPaths, IReadOnlyList<string> policyPaths)
        {
            var details = new List<VisualDetail>
            {
                new VisualDetail("Status", "PREPARATION ONLY | NO APPROVAL GRANTED | READ-ONLY"),
                new VisualDetail("Approval-request artifacts", string.Join(" | ", approvalPaths ?? new List<string>())),
                new VisualDetail("Policy-request artifacts", string.Join(" | ", policyPaths ?? new List<string>()))
            };
            foreach (EngineeringDistanceApprovalReadinessItem item in items)
            {
                details.Add(new VisualDetail(item.CandidateId, "Rule=" + item.RuleId + " | Value=" + item.CandidateValueMeters.ToString("0.###", CultureInfo.InvariantCulture) + " m | Project criterion=" + item.ConfiguredCriterionMeters.ToString("0.###", CultureInfo.InvariantCulture) + " m"));
                details.Add(new VisualDetail("  Readiness", "Approval=" + item.ApprovalArtifactCount.ToString(CultureInfo.InvariantCulture) + " | Currentness=" + item.ApprovalCurrentness + " | Trust=" + (item.TrustedIssuerPresent ? "YES" : "NO") + " | Authority policy=" + (item.VerifiedAuthorityPolicyPresent ? "YES" : "NO")));
                details.Add(new VisualDetail("  Governance", item.GovernanceState + " | AutoExecutionAllowed=" + (item.AutoExecutionAllowed ? "YES" : "NO")));
                details.Add(new VisualDetail("  Blocking reason", string.Join("; ", item.BlockingReasons ?? new List<string>())));
            }
            return details;
        }

        public static void ShowApprovalHandoff(Document document, DrawingSnapshot snapshot, EngineeringDistanceApprovalHandoffPackage package, EngineeringDistanceApprovalHandoffVerificationResult verification)
        {
            IReadOnlyList<EngineeringDistanceApprovalHandoffCandidate> candidates = new[] { package.Manifest.Road, package.Manifest.Fence };
            Publish(new VisualResultsData
            {
                Command = "ES_APPROVALPACKAGE",
                Title = "APPROVAL HANDOFF PACKAGE",
                Drawing = snapshot == null ? "<none>" : snapshot.FileName,
                Status = "PREPARATION ONLY",
                StatusDetail = "NO APPROVAL GRANTED | READ-ONLY",
                OutputPath = package.DirectoryPath,
                Document = document,
                Note = "Directory handoff package only. No approval, authority, trust, signing, installation, or activation occurred.",
                Metrics = new List<VisualMetric>
                {
                    new VisualMetric("PackageId", package.PackageId),
                    new VisualMetric("AutoExecutionAllowed", candidates.Count(x => x.AutoExecutionAllowed).ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Manifest verification", verification.Status),
                    new VisualMetric("Payload hash verification", verification.PayloadHashesValid ? "PASS" : "FAIL"),
                    new VisualMetric("Production authority material", verification.ProductionAuthorityMaterialPresent ? "YES" : "NO"),
                    new VisualMetric("Mode", "READ-ONLY")
                },
                Details = new List<VisualDetail>
                {
                    new VisualDetail("Status", "APPROVAL HANDOFF PACKAGE | PREPARATION ONLY | NO APPROVAL GRANTED | READ-ONLY"),
                    new VisualDetail("Package directory", package.DirectoryPath),
                    new VisualDetail("Road RequestId", package.Manifest.RoadApprovalRequestId),
                    new VisualDetail("Fence RequestId", package.Manifest.FenceApprovalRequestId),
                    new VisualDetail("Road readiness", package.Manifest.Road.GovernanceState + " | AutoExecutionAllowed=" + (package.Manifest.Road.AutoExecutionAllowed ? "YES" : "NO")),
                    new VisualDetail("Fence readiness", package.Manifest.Fence.GovernanceState + " | AutoExecutionAllowed=" + (package.Manifest.Fence.AutoExecutionAllowed ? "YES" : "NO")),
                    new VisualDetail("Verifier diagnostics", string.Join(" | ", verification.Diagnostics ?? new List<string>()))
                }
            });
        }

        public static void ShowApprovalIntake(Document document, DrawingSnapshot snapshot, EngineeringDistanceApprovalQuarantineResult result, string incomingRoot)
        {
            Publish(new VisualResultsData
            {
                Command = "ES_APPROVALINTAKE",
                Title = "RETURNED APPROVAL INTAKE",
                Drawing = snapshot == null ? "<none>" : snapshot.FileName,
                Status = result.Status == EngineeringDistanceApprovalQuarantineStates.NoReturnedArtifacts ? "SAFE NO-OP" : result.Status,
                StatusDetail = result.Status == EngineeringDistanceApprovalQuarantineStates.NoReturnedArtifacts ? "NO RETURNED ARTIFACTS | PRODUCTION UNCHANGED | READ-ONLY" : "PRODUCTION INSTALLATION: NO | RULE ACTIVATION: NO | READ-ONLY",
                OutputPath = result.QuarantineDirectory,
                Document = document,
                Note = "Returned artifacts are quarantined only. No approval, policy, trust, installation, or activation occurred.",
                Metrics = new List<VisualMetric>
                {
                    new VisualMetric("Incoming root", incomingRoot),
                    new VisualMetric("Returned files", result.ReturnedFileCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Quarantined files", result.QuarantinedFileCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("AutoExecutionAllowed", "0"),
                    new VisualMetric("Production unchanged", "YES"),
                    new VisualMetric("Mode", "READ-ONLY")
                },
                Details = new List<VisualDetail>
                {
                    new VisualDetail("Status", result.Status == EngineeringDistanceApprovalQuarantineStates.NoReturnedArtifacts ? "SAFE NO-OP | NO RETURNED ARTIFACTS | PRODUCTION UNCHANGED | READ-ONLY" : result.Status),
                    new VisualDetail("Source H-B PackageId", result.SourcePackageId),
                    new VisualDetail("Quarantine directory", result.QuarantineDirectory),
                    new VisualDetail("Diagnostics", string.Join(" | ", result.Diagnostics ?? new List<string>()))
                }
            });
        }

        public static void ShowPlotQa(Document document, DrawingSnapshot snapshot, PlotPlanModel plotPlan, QaReport report, string outputPath)
        {
            ShowPlotQa(document, snapshot, new PlotPlanQaReport { PlotPlan = plotPlan, Qa = report }, outputPath);
        }

        public static void ShowProfileQa(Document document, DrawingSnapshot snapshot, PlotPlanModel plotPlan, QaReport report, ProfileResolution resolution, string outputPath)
        {
            string chain = resolution == null || resolution.AppliedProfileIds.Count == 0
                ? "None"
                : string.Join(" -> ", resolution.AppliedProfileIds);
            ShowPlotQa(document, snapshot, plotPlan, report, outputPath);
            UpdateNote("Applied profile chain: " + chain);
            UpdateCommand("ES_PROFILEQA", "Profile QA");
        }

        public static void ShowCrossQa(Document document, DrawingSnapshot snapshot, ExternalEquipmentRegister register, QaReport report, string outputPath)
        {
            QaStatistics s = report.Statistics;
            Publish(new VisualResultsData
            {
                Command = "ES_CROSSQA",
                Title = "Cross-Document QA",
                Drawing = snapshot.FileName,
                Status = report.OverallStatus,
                StatusDetail = "External register comparison completed",
                OutputPath = outputPath,
                Document = document,
                Note = "External register: " + register.SourceFileName + " | SHA256: " + register.SourceSha256,
                Metrics = new List<VisualMetric>
                {
                    new VisualMetric("Source rows", register.Statistics.SourceRowCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Expanded identities", register.Statistics.ExpandedEquipmentCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Findings", s.TotalFindings.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Warnings", s.WarningFindings.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Errors", s.ErrorFindings.ToString(CultureInfo.InvariantCulture))
                },
                QaFindings = report.Findings
            });
        }

        public static void ShowRevisionBaseline(Document document, DrawingSnapshot snapshot, RevisionSnapshot revision, string outputPath)
        {
            Publish(new VisualResultsData
            {
                Command = "ES_REVBASELINE",
                Title = "Revision Baseline",
                Drawing = snapshot.FileName,
                Status = "COMPLETE",
                StatusDetail = "Read-only baseline created",
                OutputPath = outputPath,
                Document = document,
                Metrics = new List<VisualMetric>
                {
                    new VisualMetric("Equipment", revision.Statistics.EquipmentCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Mode", "Read-only"),
                    new VisualMetric("DWG changes", "None")
                },
                Details = new List<VisualDetail>
                {
                    new VisualDetail("Baseline", outputPath),
                    new VisualDetail("Drawing", snapshot.FileName),
                    new VisualDetail("DBMOD guard", "Unchanged")
                }
            });
        }

        public static void ShowRevision(Document document, DrawingSnapshot snapshot, RevisionComparisonReport report, string outputPath)
        {
            RevisionComparisonStatistics s = report.Statistics;
            RevisionLineageAssessment lineage = RevisionLineageAssessor.Assess(report.Baseline, report.Current);
            string overlap = lineage.IdentityOverlapPercent.ToString("0.#", CultureInfo.InvariantCulture) + "%";
            string lineageLabel = lineage.Confidence.Replace("_", " ");
            string statusDetail = s.TotalChangeCount == 0 ? "No engineering changes detected" : "Baseline comparison completed";
            string note = s.TotalChangeCount == 0
                ? "Baseline and current drawing match for all tracked revision evidence."
                : "Review the detected engineering changes below.";
            if (string.Equals(lineage.Confidence, "LOW", StringComparison.Ordinal))
            {
                statusDetail += " | LOW lineage confidence (" + overlap + " identity overlap)";
                note = "These drawings may not be revisions of the same engineering drawing. Comparison completed; engineering review is required.";
            }
            else if (string.Equals(lineage.Confidence, "MEDIUM", StringComparison.Ordinal))
            {
                statusDetail += " | MEDIUM lineage confidence (" + overlap + " identity overlap)";
                note = "Moderate revision-lineage confidence. Review drawing context before disposition.";
            }
            else if (string.Equals(lineage.Confidence, "INSUFFICIENT_EVIDENCE", StringComparison.Ordinal))
            {
                statusDetail += " | lineage confidence unavailable";
                note = "Revision lineage could not be assessed because no canonical equipment identities were available.";
            }
            else
            {
                statusDetail += " | HIGH lineage confidence (" + overlap + " identity overlap)";
            }
            Publish(new VisualResultsData
            {
                Command = "ES_REVISION",
                Title = "Revision Comparison",
                Drawing = snapshot.FileName,
                Status = s.TotalChangeCount == 0 ? "UNCHANGED" : "CHANGES FOUND",
                StatusDetail = statusDetail,
                OutputPath = outputPath,
                Document = document,
                Note = note,
                Metrics = new List<VisualMetric>
                {
                    new VisualMetric("Baseline equipment", s.BaselineEquipmentCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Current equipment", s.CurrentEquipmentCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Added", s.AddedEquipmentCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Removed", s.RemovedEquipmentCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Coordinate changes", s.CoordinateChangedEquipmentCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Attribute changes", s.AttributeChangedEquipmentCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Total changes", s.TotalChangeCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Unchanged", s.UnchangedEquipmentCount.ToString(CultureInfo.InvariantCulture)),
                    new VisualMetric("Revision lineage", lineageLabel),
                    new VisualMetric("Identity overlap", overlap)
                },
                RevisionChanges = report.Changes
            });
        }

        public static void ShowFailure(string command, Document document, string drawing, string message)
        {
            Publish(new VisualResultsData
            {
                Command = command,
                Title = "Command Failed",
                Drawing = drawing,
                Status = "FAILED",
                StatusDetail = message,
                Document = document,
                Note = "No drawing changes were made.",
                Details = new List<VisualDetail> { new VisualDetail("Reason", message) }
            });
        }

        public static void ShowCancelled(string command, Document document, string drawing, string message)
        {
            Publish(new VisualResultsData
            {
                Command = command,
                Title = "Command Cancelled",
                Drawing = drawing,
                Status = "CANCELLED",
                StatusDetail = message,
                Document = document,
                Note = "No drawing changes were made.",
                Details = new List<VisualDetail> { new VisualDetail("State", message) }
            });
        }

        private static List<VisualMetric> QaMetrics(QaStatistics s)
        {
            return new List<VisualMetric>
            {
                new VisualMetric("Rules", s.RuleCount.ToString(CultureInfo.InvariantCulture)),
                new VisualMetric("Findings", s.TotalFindings.ToString(CultureInfo.InvariantCulture)),
                new VisualMetric("Warnings", s.WarningFindings.ToString(CultureInfo.InvariantCulture)),
                new VisualMetric("Errors", s.ErrorFindings.ToString(CultureInfo.InvariantCulture))
            };
        }

        private static void Publish(VisualResultsData data)
        {
            if (AutoCADHostBoundary.IsCoreConsole()) return;
            PublishInteractive(data);
        }

        private static List<VisualDistanceRow> PlotPlanDistanceRows(EngineeringDistanceRunSummary summary, PlotSpatialMeasurementModel measurements)
        {
            return PlotPlanDistancePresentation.Build(summary, measurements).Select(x => new VisualDistanceRow
            {
                Name = x.Name,
                Minimum = x.MinimumDistanceMeters.ToString("0.###", CultureInfo.InvariantCulture) + " m",
                Observed = x.Observed,
                Status = x.DisplayStatus,
                Interpretation = x.Interpretation
            }).ToList();
        }

        private static string DisplayDistanceStatus(string status)
        {
            if (status == "INSUFFICIENT_EVIDENCE") return "INSUFFICIENT EVIDENCE";
            if (status == "VIOLATIONS") return "VIOLATIONS / FAIL";
            return status ?? "<unknown>";
        }

        private static string GovernanceDisplay(ProjectDistanceGovernanceAggregateItem governance)
        {
            if (governance == null || governance.Resolution == null || string.IsNullOrWhiteSpace(governance.Resolution.State)) return string.Empty;
            if (governance.Resolution.State == "PROJECT_RULE_ONLY") return "Project criterion only — no approved standards-governance basis is active.";
            if (governance.Resolution.State == "EXPERT_REVIEW_REQUIRED") return "Engineering review required.";
            if (governance.Resolution.State == "SOURCE_QUARANTINED") return "Source quarantined — not auto-enforced.";
            return "Governance state: " + governance.Resolution.State;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PublishInteractive(VisualResultsData data)
        {
            InteractiveVisualResultsPalette.Publish(data);
        }

        private static void UpdateNote(string note)
        {
            if (AutoCADHostBoundary.IsCoreConsole()) return;
            UpdateNoteInteractive(note);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void UpdateNoteInteractive(string note)
        {
            InteractiveVisualResultsPalette.UpdateNote(note);
        }

        private static void UpdateCommand(string command, string title)
        {
            if (AutoCADHostBoundary.IsCoreConsole()) return;
            UpdateCommandInteractive(command, title);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void UpdateCommandInteractive(string command, string title)
        {
            InteractiveVisualResultsPalette.UpdateCommand(command, title);
        }
    }

    public sealed class PlotPlanDistancePresentationRow
    {
        public string RuleId { get; set; }
        public string Name { get; set; }
        public decimal MinimumDistanceMeters { get; set; }
        public string Observed { get; set; }
        public int ResolvedCount { get; set; }
        public string UnderlyingStatus { get; set; }
        public string DisplayStatus { get; set; }
        public string Interpretation { get; set; }
    }

    public static class PlotPlanDistancePresentation
    {
        public static IReadOnlyList<PlotPlanDistancePresentationRow> Build(EngineeringDistanceRunSummary summary, PlotSpatialMeasurementModel measurements)
        {
            if (summary == null || summary.Evaluations == null) return new List<PlotPlanDistancePresentationRow>();
            return new[]
            {
                BuildRow(summary, measurements, ProjectDistanceRuleBinder.EquipmentToRoadRuleId, "Equipment → Road", summary.AccessRoadEdgeMinimumMeters),
                BuildRow(summary, measurements, ProjectDistanceRuleBinder.EquipmentToPlantRuleId, "Equipment → Plant / Security Fence", summary.SecurityFenceMinimumMeters),
                BuildRow(summary, measurements, ProjectDistanceRuleBinder.EquipmentToUnitBoundaryRuleId, "Equipment → Unit Boundary", summary.UnitBoundaryMinimumMeters)
            };
        }

        private static PlotPlanDistancePresentationRow BuildRow(EngineeringDistanceRunSummary summary, PlotSpatialMeasurementModel measurements, string ruleId, string name, decimal minimum)
        {
            EngineeringDistanceRuleEvaluation evaluation = summary.Evaluations.SingleOrDefault(x => x != null && x.RuleId == ruleId);
            ProjectDistanceGovernanceAggregateItem governance = summary.Governance == null || summary.Governance.Items == null
                ? null
                : summary.Governance.Items.SingleOrDefault(x => x != null && x.ProjectRuleId == ruleId);
            List<EquipmentSpatialMeasurement> resolved = ResolvedMeasurements(evaluation, measurements);
            string interpretation = evaluation == null || evaluation.Status == "INSUFFICIENT_EVIDENCE"
                ? "No resolved evidence is available for this check."
                : evaluation.Status == "PASS"
                    ? "Requirement met based on available evidence."
                    : evaluation.Status == "VIOLATIONS"
                        ? "Observed evidence is below the configured project minimum."
                        : "No resolved evidence is available for this check.";
            string governanceText = GovernanceDisplay(governance);
            if (!string.IsNullOrWhiteSpace(governanceText)) interpretation += " " + governanceText;
            bool hasEvidence = evaluation != null && evaluation.Status != "INSUFFICIENT_EVIDENCE" && resolved.Count > 0;
            string observed = !hasEvidence
                ? "<not resolved>"
                : string.Format(CultureInfo.InvariantCulture, "{0:0.###} m ({1} resolved)", resolved.Min(x => x.Distance2D) * (evaluation.MetersPerDrawingUnit), resolved.Count);
            return new PlotPlanDistancePresentationRow
            {
                RuleId = ruleId,
                Name = name,
                MinimumDistanceMeters = minimum,
                Observed = observed,
                ResolvedCount = hasEvidence ? resolved.Count : 0,
                UnderlyingStatus = evaluation == null ? "<not available>" : evaluation.Status,
                DisplayStatus = DisplayDistanceStatus(evaluation == null ? null : evaluation.Status),
                Interpretation = interpretation
            };
        }

        private static List<EquipmentSpatialMeasurement> ResolvedMeasurements(EngineeringDistanceRuleEvaluation evaluation, PlotSpatialMeasurementModel measurements)
        {
            if (evaluation == null || measurements == null || measurements.EquipmentSpatialMeasurements == null) return new List<EquipmentSpatialMeasurement>();
            string subjectKind = evaluation.MeasurementKind == "EquipmentToRoad2D" ? "Road" : "Boundary";
            return measurements.EquipmentSpatialMeasurements
                .Where(x => x != null
                    && string.Equals(x.SubjectKind, subjectKind, StringComparison.Ordinal)
                    && string.Equals(x.EngineeringMeasurementBasisKey, evaluation.RequiredMeasurementBasisKey, StringComparison.Ordinal)
                    && string.Equals(x.EngineeringApplicabilityKey, evaluation.RequiredApplicabilityKey, StringComparison.Ordinal))
                .GroupBy(x => x.PlacementId, StringComparer.Ordinal)
                .Select(x => x.OrderBy(y => y.Distance2D).ThenBy(y => y.MeasurementId, StringComparer.Ordinal).First())
                .OrderBy(x => x.PlacementId, StringComparer.Ordinal)
                .ThenBy(x => x.MeasurementId, StringComparer.Ordinal)
                .ToList();
        }

        private static string DisplayDistanceStatus(string status)
        {
            if (status == "INSUFFICIENT_EVIDENCE") return "INSUFFICIENT EVIDENCE";
            if (status == "VIOLATIONS") return "VIOLATIONS / FAIL";
            return status ?? "<unknown>";
        }

        private static string GovernanceDisplay(ProjectDistanceGovernanceAggregateItem governance)
        {
            if (governance == null || governance.Resolution == null || string.IsNullOrWhiteSpace(governance.Resolution.State)) return string.Empty;
            if (governance.Resolution.State == "PROJECT_RULE_ONLY") return "Project criterion only — no approved standards-governance basis is active.";
            if (governance.Resolution.State == "EXPERT_REVIEW_REQUIRED") return "Engineering review required.";
            if (governance.Resolution.State == "SOURCE_QUARANTINED") return "Source quarantined — not auto-enforced.";
            return "Governance state: " + governance.Resolution.State;
        }
    }

    internal static class InteractiveVisualResultsPalette
    {
        private static Form window;
        private static VisualResultsControl control;

        internal static void Publish(VisualResultsData data)
        {
            try
            {
                if (window == null || window.IsDisposed)
                {
                    control = new VisualResultsControl();
                    window = new Form
                    {
                        Text = "EngiSentry Results",
                        StartPosition = FormStartPosition.CenterScreen,
                        Size = new Size(930, 680),
                        MinimumSize = new Size(520, 360),
                        FormBorderStyle = FormBorderStyle.Sizable,
                        MaximizeBox = true,
                        MinimizeBox = true,
                        ShowInTaskbar = true,
                        BackColor = Color.FromArgb(30, 31, 34)
                    };
                    control.Dock = DockStyle.Fill;
                    window.Controls.Add(control);
                    window.FormClosed += (sender, args) =>
                    {
                        window = null;
                        control = null;
                    };
                    Autodesk.AutoCAD.ApplicationServices.Application.ShowModelessDialog(window);
                }

                control.SetResult(data);
                EnsureUsableWindowSize(data);
                if (window.WindowState == FormWindowState.Minimized)
                    window.WindowState = FormWindowState.Normal;
                window.Show();
                window.BringToFront();
            }
            catch (Exception exception)
            {
                Debug.WriteLine("EngiSentry visual results error: " + exception);
            }
        }

        internal static void UpdateNote(string note)
        {
            if (control != null) control.SetNote(note);
        }

        internal static void UpdateCommand(string command, string title)
        {
            if (control != null) control.SetCommandAndTitle(command, title);
        }

        private static void EnsureUsableWindowSize(VisualResultsData data)
        {
            if (window == null || window.IsDisposed) return;

            bool hasTable = (data.QaFindings != null && data.QaFindings.Count > 0) ||
                            (data.RevisionChanges != null && data.RevisionChanges.Count > 0);
            int rowCount = data.QaFindings != null && data.QaFindings.Count > 0
                ? data.QaFindings.Count
                : data.RevisionChanges != null && data.RevisionChanges.Count > 0
                    ? data.RevisionChanges.Count
                    : data.Details == null ? 0 : data.Details.Count;

            int preferredWidth = hasTable ? 930 : 720;
            int visibleRows = Math.Max(3, Math.Min(rowCount, hasTable ? 12 : 8));
            int preferredHeight = Math.Max(460, Math.Min(760, 255 + (visibleRows * 25)));

            // Respect a user's manual resize after the window has opened. Only grow
            // a newly-small window enough to keep the result usable; never dock it.
            if (window.Width < preferredWidth) window.Width = preferredWidth;
            if (window.Height < preferredHeight) window.Height = preferredHeight;
        }
    }

    internal sealed class VisualResultsData
    {
        public string Command { get; set; }
        public string Title { get; set; }
        public string Drawing { get; set; }
        public string Status { get; set; }
        public string StatusDetail { get; set; }
        public string OutputPath { get; set; }
        public string Note { get; set; }
        public Document Document { get; set; }
        public List<VisualMetric> Metrics { get; set; } = new List<VisualMetric>();
        public List<VisualDistanceRow> DistanceRows { get; set; } = new List<VisualDistanceRow>();
        public List<VisualDetail> Details { get; set; } = new List<VisualDetail>();
        public List<QaFinding> QaFindings { get; set; } = new List<QaFinding>();
        public List<RevisionChangeRecord> RevisionChanges { get; set; } = new List<RevisionChangeRecord>();
        public List<StandardAuthorityReviewItem> AuthorityReviewItems { get; set; } = new List<StandardAuthorityReviewItem>();
        public StandardAuthorityReviewPackage StandardsReviewPackage { get; set; }
        public string StandardsReviewPackagePath { get; set; }
        public string StandardsReviewPackageSha256 { get; set; }
    }

    internal sealed class VisualMetric
    {
        public VisualMetric(string label, string value) { Label = label; Value = value; }
        public string Label { get; private set; }
        public string Value { get; private set; }
    }

    internal sealed class VisualDetail
    {
        public VisualDetail(string label, string value) { Label = label; Value = value ?? string.Empty; }
        public string Label { get; private set; }
        public string Value { get; private set; }
    }

    internal static class PlacementProposalPresentation
    {
        private const string NotAvailable = "Not available in PlacementProposal projection";

        internal static List<VisualDetail> BuildDetails(IReadOnlyList<PlacementProposal> proposals, PlacementProposalRunDiagnostics diagnostics, string sidecarPath)
        {
            var details = new List<VisualDetail>
            {
                new VisualDetail("Status", "READ-ONLY"),
                new VisualDetail("Placement-proposal sidecar", Safe(sidecarPath)),
                new VisualDetail("Satisfied constraints", CountState(proposals, ProposalConstraintState.Satisfied)),
                new VisualDetail("Violated constraints", CountState(proposals, ProposalConstraintState.Violated)),
                new VisualDetail("Insufficient-evidence constraints", CountState(proposals, ProposalConstraintState.InsufficientEvidence)),
                new VisualDetail("Review-required constraints", CountState(proposals, ProposalConstraintState.ReviewRequired)),
                new VisualDetail("Not-applicable constraints", CountState(proposals, ProposalConstraintState.NotApplicable)),
                new VisualDetail("Unchecked constraints", JoinDistinct(proposals.SelectMany(x => x.UncheckedConstraints ?? new List<string>()), "Not available")),
                new VisualDetail("Assumptions", JoinDistinct(proposals.SelectMany(x => x.Assumptions ?? new List<string>()), "Not available")),
                new VisualDetail("EngineeringReviewRequired", proposals.Count == 0 ? "YES (proposal layer requires engineering review)" : (proposals.All(x => x.EngineeringReviewRequired) ? "YES" : "NO")),
                new VisualDetail("Known limitations", JoinDistinct(proposals.SelectMany(x => x.KnownLimitations ?? new List<string>()), "Not available")),
                new VisualDetail("Rejected candidates", RejectedCandidates(proposals)),
                new VisualDetail("Governance diagnostics", string.Join(" | ", (diagnostics == null ? new List<string>() : diagnostics.DiagnosticReasons ?? new List<string>()).OrderBy(x => x, StringComparer.Ordinal)))
            };

            foreach (PlacementProposal proposal in proposals.OrderBy(x => x.ProposalId ?? string.Empty, StringComparer.Ordinal))
            {
                AddProposalDetails(details, proposal);
            }

            if (proposals.Count == 0)
            {
                details.Add(new VisualDetail("Proposal details", "No placement proposals were produced; governed evaluation completed successfully."));
                details.Add(new VisualDetail("Unchecked constraints", JoinDistinct(ProposalUncheckedConstraints.Defaults, "Not available")));
                details.Add(new VisualDetail("Assumptions", JoinDistinct(ProposalAssumptions.Defaults, "Not available")));
                details.Add(new VisualDetail("Known limitations", JoinDistinct(ProposalKnownLimitations.Defaults, "Not available")));
            }

            return details;
        }

        private static void AddProposalDetails(List<VisualDetail> details, PlacementProposal proposal)
        {
            StandardsPackRule rule = proposal.ViolatedRule;
            details.Add(new VisualDetail("Proposal " + Safe(proposal.ProposalId), ""));
            details.Add(new VisualDetail("  PlacementId", NotAvailable));
            details.Add(new VisualDetail("  EquipmentEntityId", NotAvailable));
            details.Add(new VisualDetail("  NormalizedTag", NotAvailable));
            details.Add(new VisualDetail("  RuleId", Safe(rule == null ? null : rule.RuleId)));
            details.Add(new VisualDetail("  Company / Pack", Safe(rule == null ? null : (rule.Company ?? rule.PackName ?? rule.PackId))));
            details.Add(new VisualDetail("  Document number / title", Safe(rule == null ? null : (rule.DocumentNumber + " / " + rule.DocumentTitle))));
            details.Add(new VisualDetail("  Revision / edition", Safe(rule == null ? null : rule.RevisionOrEdition)));
            details.Add(new VisualDetail("  Clause / reference / page", ClauseText(proposal)));
            details.Add(new VisualDetail("  Source SHA256", SourceHash(proposal)));
            details.Add(new VisualDetail("  Observed distance", proposal.ObservedDistanceMeters.ToString("0.###", CultureInfo.InvariantCulture) + " m"));
            details.Add(new VisualDetail("  Required distance", proposal.RequiredDistanceMeters.ToString("0.###", CultureInfo.InvariantCulture) + " m"));
            details.Add(new VisualDetail("  Measurement basis", Safe(proposal.ObservedMeasurementBasis ?? proposal.MeasurementBasisKey)));
            details.Add(new VisualDetail("  Unit context", Safe(proposal.UnitContext)));
            details.Add(new VisualDetail("  Original position", Point(proposal.OriginalEasting, proposal.OriginalNorthing)));
            details.Add(new VisualDetail("  Proposed position", Point(proposal.ProposedEasting, proposal.ProposedNorthing)));
            details.Add(new VisualDetail("  Movement", proposal.MovementDistanceMeters.ToString("0.###", CultureInfo.InvariantCulture) + " m / " + Safe(proposal.MovementDirection)));
            details.Add(new VisualDetail("  Road re-evaluation", Outcome(proposal.RoadReEvaluation)));
            details.Add(new VisualDetail("  Plant Fence re-evaluation", Outcome(proposal.PlantFenceReEvaluation)));
            details.Add(new VisualDetail("  Unit Boundary re-evaluation", Outcome(proposal.UnitBoundaryReEvaluation)));
            details.Add(new VisualDetail("  Constraint outcomes", Outcomes(proposal)));
            details.Add(new VisualDetail("  Rejected candidates", RejectedCandidates(new[] { proposal })));
            details.Add(new VisualDetail("  EngineeringReviewRequired", proposal.EngineeringReviewRequired ? "YES: " + Safe(proposal.EngineeringReviewReason) : "NO"));
        }

        private static string ClauseText(PlacementProposal proposal)
        {
            StandardClauseCitation clause = (proposal.ClauseEvidence ?? new List<StandardClauseCitation>()).OrderBy(x => x.ClauseId ?? string.Empty, StringComparer.Ordinal).FirstOrDefault();
            return clause == null ? NotAvailable : Safe(clause.ClauseReference) + " / " + (clause.PageNumber.HasValue ? clause.PageNumber.Value.ToString(CultureInfo.InvariantCulture) : "page not available");
        }

        private static string SourceHash(PlacementProposal proposal)
        {
            StandardClauseCitation clause = (proposal.ClauseEvidence ?? new List<StandardClauseCitation>()).OrderBy(x => x.ClauseId ?? string.Empty, StringComparer.Ordinal).FirstOrDefault();
            return clause == null ? NotAvailable : Safe(clause.SourceSha256);
        }

        private static string Outcomes(PlacementProposal proposal)
        {
            IEnumerable<PlacementProposalConstraintOutcome> outcomes = (new[] { proposal.RoadReEvaluation, proposal.PlantFenceReEvaluation, proposal.UnitBoundaryReEvaluation })
                .Concat(proposal.SatisfiedConstraints ?? new List<PlacementProposalConstraintOutcome>())
                .Concat(proposal.ViolatedConstraints ?? new List<PlacementProposalConstraintOutcome>())
                .Concat(proposal.InsufficientEvidenceConstraints ?? new List<PlacementProposalConstraintOutcome>())
                .Where(x => x != null).GroupBy(x => x.ConstraintId ?? string.Empty, StringComparer.Ordinal).Select(x => x.First()).OrderBy(x => x.ConstraintId ?? string.Empty, StringComparer.Ordinal);
            return string.Join(" | ", outcomes.Select(x => Safe(x.ConstraintId) + "=" + Safe(x.EvaluationState) + (string.IsNullOrWhiteSpace(x.Reason) ? string.Empty : " (" + x.Reason + ")")));
        }

        private static string Outcome(PlacementProposalConstraintOutcome outcome)
        {
            return outcome == null ? NotAvailable : Safe(outcome.EvaluationState) + (string.IsNullOrWhiteSpace(outcome.Reason) ? string.Empty : " (" + outcome.Reason + ")");
        }

        private static string RejectedCandidates(IEnumerable<PlacementProposal> proposals)
        {
            IEnumerable<string> values = proposals.SelectMany(x => x.RejectedCandidates ?? new List<PlacementProposalCandidate>())
                .Where(x => x != null).OrderBy(x => x.CandidateId ?? string.Empty, StringComparer.Ordinal)
                .Select(x => Safe(x.CandidateId) + ": " + JoinDistinct(x.RejectedReasons ?? new List<string>(), "reason not available"));
            return string.Join(" | ", values.DefaultIfEmpty("None"));
        }

        private static string CountState(IEnumerable<PlacementProposal> proposals, string state)
        {
            return proposals.SelectMany(x => (new[] { x.RoadReEvaluation, x.PlantFenceReEvaluation, x.UnitBoundaryReEvaluation })
                .Concat(x.SatisfiedConstraints ?? new List<PlacementProposalConstraintOutcome>())
                .Concat(x.ViolatedConstraints ?? new List<PlacementProposalConstraintOutcome>())
                .Concat(x.InsufficientEvidenceConstraints ?? new List<PlacementProposalConstraintOutcome>()))
                .Count(x => x != null && string.Equals(x.EvaluationState, state, StringComparison.Ordinal)).ToString(CultureInfo.InvariantCulture);
        }

        private static string JoinDistinct(IEnumerable<string> values, string empty)
        {
            List<string> clean = (values ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            return clean.Count == 0 ? empty : string.Join(" | ", clean);
        }

        private static string Point(double x, double y) => "(" + x.ToString("0.###", CultureInfo.InvariantCulture) + ", " + y.ToString("0.###", CultureInfo.InvariantCulture) + ")";
        private static string Safe(string value) => string.IsNullOrWhiteSpace(value) ? NotAvailable : value;
    }

    internal sealed class VisualDistanceRow
    {
        public string Name { get; set; }
        public string Minimum { get; set; }
        public string Observed { get; set; }
        public string Status { get; set; }
        public string Interpretation { get; set; }
    }

    internal sealed class VisualResultsControl : UserControl
    {
        private readonly Label titleLabel = new Label();
        private readonly Label commandLabel = new Label();
        private readonly Label drawingLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly Label noteLabel = new Label();
        private readonly Label artifactPathLabel = new Label();
        private readonly FlowLayoutPanel metricsPanel = new FlowLayoutPanel();
        private readonly DataGridView detailGrid = new DataGridView();
        private readonly DataGridView distanceGrid = new DataGridView();
        private readonly Label distanceLabel = new Label();
        private readonly Panel distancePanel = new Panel();
        private readonly Label gridLabel = new Label();
        private readonly Panel reviewPanel = new Panel();
        private readonly DataGridView reviewGrid = new DataGridView();
        private readonly Label reviewLabel = new Label();
        private readonly Button openOutputButton = new Button();
        private readonly Button zoomEvidenceButton = new Button();
        private readonly TextBox reviewerTextBox = new TextBox();
        private readonly ComboBox humanReviewComboBox = new ComboBox();
        private readonly TextBox reviewCommentTextBox = new TextBox();
        private readonly Button saveReviewDecisionButton = new Button();
        private readonly Label existingReviewLabel = new Label();
        private readonly Label evidenceCurrentnessLabel = new Label();
        private readonly Label historyStateLabel = new Label();
        private readonly Label existingReviewerLabel = new Label();
        private readonly Label decisionTimeLabel = new Label();
        private readonly Label decisionStatusLabel = new Label();
        private readonly Panel reviewInputPanel = new Panel();
        private readonly Panel bodyPanel = new Panel();
        private VisualResultsData current;

        public VisualResultsControl()
        {
            BackColor = Color.FromArgb(30, 31, 34);
            ForeColor = Color.FromArgb(235, 237, 240);
            Font = new System.Drawing.Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Dock = DockStyle.Fill;
            BuildLayout();
        }

        public void SetResult(VisualResultsData data)
        {
            current = data;
            titleLabel.Text = data.Title ?? "EngiSentry Results";
            commandLabel.Text = data.Command ?? string.Empty;
            drawingLabel.Text = "Drawing: " + (string.IsNullOrWhiteSpace(data.Drawing) ? "<none>" : data.Drawing);
            statusLabel.Text = string.IsNullOrWhiteSpace(data.StatusDetail) ? data.Status : data.Status + "  |  " + data.StatusDetail;
            statusLabel.ForeColor = StatusColor(data.Status);
            noteLabel.Text = data.Note ?? string.Empty;
            noteLabel.Visible = !string.IsNullOrWhiteSpace(data.Note);
            artifactPathLabel.Text = string.IsNullOrWhiteSpace(data.OutputPath) ? string.Empty : "QA artifact: " + data.OutputPath;
            artifactPathLabel.Visible = !string.IsNullOrWhiteSpace(data.OutputPath);
            openOutputButton.Enabled = !string.IsNullOrWhiteSpace(data.OutputPath);
            zoomEvidenceButton.Visible = data.QaFindings != null && data.QaFindings.Count > 0;
            BuildMetrics(data.Metrics);
            BuildDistanceGrid(data.DistanceRows);
            BuildGrid(data);
        }

        public void SetNote(string note)
        {
            noteLabel.Text = note ?? string.Empty;
            noteLabel.Visible = !string.IsNullOrWhiteSpace(note);
        }

        public void SetCommandAndTitle(string command, string title)
        {
            commandLabel.Text = command;
            titleLabel.Text = title;
        }

        private void BuildLayout()
        {
            Panel header = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Color.FromArgb(38, 40, 44), Padding = new Padding(10, 5, 10, 4) };
            titleLabel.Dock = DockStyle.Top;
            titleLabel.Height = 20;
            titleLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 12.5F, FontStyle.Bold);
            titleLabel.ForeColor = Color.White;
            commandLabel.Dock = DockStyle.Right;
            commandLabel.Width = 115;
            commandLabel.TextAlign = ContentAlignment.TopRight;
            commandLabel.ForeColor = Color.FromArgb(160, 166, 175);
            drawingLabel.Dock = DockStyle.Fill;
            drawingLabel.AutoEllipsis = true;
            drawingLabel.ForeColor = Color.FromArgb(190, 195, 202);
            header.Controls.Add(drawingLabel);
            header.Controls.Add(commandLabel);
            header.Controls.Add(titleLabel);

            metricsPanel.Dock = DockStyle.Top;
            metricsPanel.Height = 104;
            metricsPanel.Padding = new Padding(8, 6, 8, 6);
            metricsPanel.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            metricsPanel.WrapContents = true;
            metricsPanel.AutoScroll = false;
            metricsPanel.BackColor = Color.FromArgb(30, 31, 34);
            metricsPanel.SizeChanged += (sender, args) => { if (current != null) BuildMetrics(current.Metrics); };

            Panel statusPanel = new Panel { Dock = DockStyle.Top, Height = 63, Padding = new Padding(10, 3, 10, 3), BackColor = Color.FromArgb(30, 31, 34) };
            statusLabel.Dock = DockStyle.Top;
            statusLabel.Height = 19;
            statusLabel.AutoEllipsis = true;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            noteLabel.Dock = DockStyle.Bottom;
            noteLabel.Height = 19;
            noteLabel.AutoEllipsis = true;
            noteLabel.TextAlign = ContentAlignment.MiddleLeft;
            noteLabel.ForeColor = Color.FromArgb(160, 166, 175);
            artifactPathLabel.Dock = DockStyle.Bottom;
            artifactPathLabel.Height = 19;
            artifactPathLabel.AutoEllipsis = true;
            artifactPathLabel.TextAlign = ContentAlignment.MiddleLeft;
            artifactPathLabel.ForeColor = Color.FromArgb(160, 166, 175);
            statusPanel.Controls.Add(statusLabel);
            statusPanel.Controls.Add(noteLabel);
            statusPanel.Controls.Add(artifactPathLabel);

            bodyPanel.Dock = DockStyle.Fill;
            bodyPanel.Padding = new Padding(10, 2, 10, 8);
            gridLabel.Dock = DockStyle.Top;
            gridLabel.Height = 17;
            gridLabel.Text = "DETAILS";
            gridLabel.ForeColor = Color.FromArgb(160, 166, 175);
            gridLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 8F, FontStyle.Bold);
            detailGrid.Dock = DockStyle.Fill;
            ConfigureGrid();
            bodyPanel.Controls.Add(detailGrid);
            bodyPanel.Controls.Add(gridLabel);
            reviewPanel.Dock = DockStyle.Bottom;
            reviewPanel.Height = 360;
            reviewLabel.Dock = DockStyle.Top;
            reviewLabel.Height = 17;
            reviewLabel.Text = "ENGINEERING REVIEW";
            reviewLabel.ForeColor = Color.FromArgb(160, 166, 175);
            reviewLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 8F, FontStyle.Bold);
            reviewPanel.Padding = new Padding(0, 2, 0, 0);
            reviewGrid.Dock = DockStyle.Top;
            reviewGrid.Height = 110;
            reviewPanel.Controls.Add(reviewInputPanel);
            reviewPanel.Controls.Add(reviewGrid);
            reviewPanel.Controls.Add(reviewLabel);
            reviewPanel.Visible = false;
            bodyPanel.Controls.Add(reviewPanel);
            ConfigureReviewGrid();
            BuildReviewInputPanel();

            FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(6, 3, 6, 4), BackColor = Color.FromArgb(38, 40, 44), FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight, WrapContents = false, AutoScroll = true };
            openOutputButton.Text = "Open Output Folder";
            openOutputButton.AutoSize = true;
            openOutputButton.FlatStyle = FlatStyle.Flat;
            openOutputButton.FlatAppearance.BorderColor = Color.FromArgb(80, 84, 92);
            openOutputButton.ForeColor = Color.White;
            openOutputButton.BackColor = Color.FromArgb(52, 55, 61);
            openOutputButton.Click += OpenOutputFolder;
            zoomEvidenceButton.Text = "Zoom To Evidence";
            zoomEvidenceButton.AutoSize = true;
            zoomEvidenceButton.FlatStyle = FlatStyle.Flat;
            zoomEvidenceButton.FlatAppearance.BorderColor = Color.FromArgb(80, 84, 92);
            zoomEvidenceButton.ForeColor = Color.White;
            zoomEvidenceButton.BackColor = Color.FromArgb(52, 55, 61);
            zoomEvidenceButton.Enabled = false;
            zoomEvidenceButton.Click += ZoomToEvidence;
            footer.Controls.Add(zoomEvidenceButton);
            footer.Controls.Add(openOutputButton);

            Controls.Add(bodyPanel);
            Controls.Add(footer);
            Controls.Add(statusPanel);
            Controls.Add(metricsPanel);
            Controls.Add(header);
        }

        private void ConfigureGrid()
        {
            detailGrid.AllowUserToAddRows = false;
            detailGrid.AllowUserToDeleteRows = false;
            detailGrid.AllowUserToResizeRows = false;
            detailGrid.BackgroundColor = Color.FromArgb(30, 31, 34);
            detailGrid.BorderStyle = BorderStyle.None;
            detailGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            detailGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            detailGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(52, 55, 61), ForeColor = Color.FromArgb(230, 233, 237), Font = new System.Drawing.Font("Segoe UI Semibold", 8F, FontStyle.Bold) };
            detailGrid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(30, 31, 34), ForeColor = Color.FromArgb(225, 228, 232), SelectionBackColor = Color.FromArgb(55, 75, 95), SelectionForeColor = Color.White, WrapMode = DataGridViewTriState.False };
            detailGrid.EnableHeadersVisualStyles = false;
            detailGrid.MultiSelect = false;
            detailGrid.ReadOnly = true;
            detailGrid.RowHeadersVisible = false;
            detailGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            detailGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            detailGrid.RowTemplate.Height = 25;
            detailGrid.ColumnHeadersHeight = 26;
            detailGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            detailGrid.SelectionChanged += DetailGridSelectionChanged;

            distancePanel.Dock = DockStyle.Top;
            distancePanel.Height = 122;
            distanceLabel.Dock = DockStyle.Top;
            distanceLabel.Height = 17;
            distanceLabel.Text = "PROJECT DISTANCE CRITERIA";
            distanceLabel.ForeColor = Color.FromArgb(160, 166, 175);
            distanceLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 8F, FontStyle.Bold);
            distanceGrid.Dock = DockStyle.Fill;
            ConfigureDistanceGrid();
            distancePanel.Controls.Add(distanceGrid);
            distancePanel.Controls.Add(distanceLabel);
            distancePanel.Visible = false;
            bodyPanel.Controls.Add(distancePanel);
        }

        private void ConfigureReviewGrid()
        {
            reviewGrid.AllowUserToAddRows = false;
            reviewGrid.AllowUserToDeleteRows = false;
            reviewGrid.AllowUserToResizeRows = false;
            reviewGrid.BackgroundColor = Color.FromArgb(30, 31, 34);
            reviewGrid.BorderStyle = BorderStyle.None;
            reviewGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            reviewGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            reviewGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(52, 55, 61), ForeColor = Color.FromArgb(230, 233, 237), Font = new System.Drawing.Font("Segoe UI Semibold", 8F, FontStyle.Bold) };
            reviewGrid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(30, 31, 34), ForeColor = Color.FromArgb(225, 228, 232), SelectionBackColor = Color.FromArgb(55, 75, 95), SelectionForeColor = Color.White, WrapMode = DataGridViewTriState.False };
            reviewGrid.EnableHeadersVisualStyles = false;
            reviewGrid.MultiSelect = false;
            reviewGrid.ReadOnly = true;
            reviewGrid.RowHeadersVisible = false;
            reviewGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            reviewGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            reviewGrid.RowTemplate.Height = 25;
            reviewGrid.ColumnHeadersHeight = 26;
            reviewGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            reviewGrid.SelectionChanged += ReviewGridSelectionChanged;
        }

        private void BuildReviewInputPanel()
        {
            reviewInputPanel.Dock = DockStyle.Fill;
            reviewInputPanel.Padding = new Padding(0, 4, 0, 0);
            reviewerTextBox.Width = 180;
            humanReviewComboBox.Width = 180;
            humanReviewComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            humanReviewComboBox.Items.AddRange(new object[] { HumanEngineeringReviewDecisionStates.ReviewAccepted, HumanEngineeringReviewDecisionStates.ReviewRejected, HumanEngineeringReviewDecisionStates.NeedsClarification });
            humanReviewComboBox.SelectedIndex = 0;
            reviewCommentTextBox.Multiline = true;
            reviewCommentTextBox.Height = 34;
            reviewCommentTextBox.Width = 290;
            saveReviewDecisionButton.Text = "Save Review Decision";
            saveReviewDecisionButton.AutoSize = true;
            saveReviewDecisionButton.Enabled = false;
            saveReviewDecisionButton.Click += SaveReviewDecision;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 8, Padding = new Padding(0, 2, 0, 0) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            AddReviewInput(layout, "Reviewer", reviewerTextBox, 0, 0);
            AddReviewInput(layout, "Human Review", humanReviewComboBox, 0, 1);
            AddReviewInput(layout, "Comment", reviewCommentTextBox, 0, 2);
            layout.Controls.Add(saveReviewDecisionButton, 2, 2);
            layout.SetColumnSpan(saveReviewDecisionButton, 2);
            layout.Controls.Add(existingReviewLabel, 0, 3);
            layout.SetColumnSpan(existingReviewLabel, 4);
            layout.Controls.Add(evidenceCurrentnessLabel, 0, 4);
            layout.SetColumnSpan(evidenceCurrentnessLabel, 4);
            layout.Controls.Add(historyStateLabel, 0, 5);
            layout.SetColumnSpan(historyStateLabel, 4);
            layout.Controls.Add(existingReviewerLabel, 0, 6);
            layout.SetColumnSpan(existingReviewerLabel, 4);
            layout.Controls.Add(decisionStatusLabel, 0, 7);
            layout.SetColumnSpan(decisionStatusLabel, 4);
            reviewInputPanel.Controls.Add(layout);
            foreach (Label control in new[] { existingReviewLabel, evidenceCurrentnessLabel, historyStateLabel, existingReviewerLabel, decisionTimeLabel, decisionStatusLabel })
            {
                control.ForeColor = Color.FromArgb(190, 195, 202);
                control.Dock = DockStyle.Fill;
                control.AutoEllipsis = false;
                control.TextAlign = ContentAlignment.MiddleLeft;
            }
            decisionStatusLabel.AutoSize = false;
            decisionStatusLabel.AutoEllipsis = false;
        }

        private static void AddReviewInput(TableLayoutPanel layout, string label, Control control, int column, int row)
        {
            var panel = new Panel { Dock = DockStyle.Fill };
            var caption = new Label { Text = label + ":", Dock = DockStyle.Left, Width = 105, ForeColor = Color.FromArgb(168, 174, 184), TextAlign = ContentAlignment.MiddleLeft };
            control.Dock = DockStyle.Fill;
            panel.Controls.Add(control);
            panel.Controls.Add(caption);
            layout.Controls.Add(panel, column, row);
            layout.SetColumnSpan(panel, 2);
        }

        private void BuildMetrics(IEnumerable<VisualMetric> metrics)
        {
            metricsPanel.SuspendLayout();
            metricsPanel.Controls.Clear();
            List<VisualMetric> items = (metrics ?? Enumerable.Empty<VisualMetric>()).ToList();
            bool hasTable = current != null &&
            ((current.QaFindings != null && current.QaFindings.Count > 0) ||
                 (current.RevisionChanges != null && current.RevisionChanges.Count > 0) ||
                 (current.AuthorityReviewItems != null && current.AuthorityReviewItems.Count > 0));
            int available = Math.Max(180, metricsPanel.ClientSize.Width - metricsPanel.Padding.Horizontal - 2);
            int columns = 2;
            int gap = 5;
            int cardWidth = Math.Max(74, (available - gap * 2) / 2);
            int cardHeight = hasTable ? 40 : 48;
            int rows = Math.Max(1, (items.Count + columns - 1) / columns);
            metricsPanel.Height = metricsPanel.Padding.Vertical + rows * (cardHeight + 4);
            foreach (VisualMetric metric in items)
            {
                Panel card = new Panel { Width = cardWidth, Height = cardHeight, Margin = new Padding(0, 0, gap, 4), BackColor = Color.FromArgb(43, 45, 50), Padding = new Padding(8, 5, 7, 4) };
                Label value = new Label { Dock = DockStyle.Top, Height = hasTable ? 18 : 22, Text = metric.Value, ForeColor = Color.White, Font = new System.Drawing.Font("Segoe UI Semibold", hasTable ? 10.5F : 12.5F, FontStyle.Bold), AutoEllipsis = true };
                Label label = new Label { Dock = DockStyle.Fill, Text = metric.Label.ToUpperInvariant(), ForeColor = Color.FromArgb(168, 174, 184), Font = new System.Drawing.Font("Segoe UI", 7F, FontStyle.Regular), AutoEllipsis = true };
                card.Controls.Add(label);
                card.Controls.Add(value);
                metricsPanel.Controls.Add(card);
            }
            metricsPanel.ResumeLayout();
        }

        private void BuildGrid(VisualResultsData data)
        {
            detailGrid.Columns.Clear();
            detailGrid.Rows.Clear();
            detailGrid.ScrollBars = ScrollBars.Vertical;
            if (data.QaFindings != null && data.QaFindings.Count > 0)
            {
                gridLabel.Text = "FINDINGS";
                AddColumn("Severity", 82);
                AddColumn("Rule", 105);
                AddColumn("Tag / Subject", 145);
                AddColumn("Message", 420);
                foreach (QaFinding finding in data.QaFindings)
                {
                    int row = detailGrid.Rows.Add(finding.Severity, finding.RuleId, Subject(finding), finding.Message);
                    detailGrid.Rows[row].Tag = finding;
                }
            }
            else if (data.RevisionChanges != null && data.RevisionChanges.Count > 0)
            {
                gridLabel.Text = "CHANGES";
                detailGrid.ScrollBars = data.RevisionChanges.Count <= 8 ? ScrollBars.None : ScrollBars.Vertical;
                AddColumn("Change", 145);
                AddColumn("Equipment Tag", 150);
                AddColumn("Details", 520);
                foreach (RevisionChangeRecord change in data.RevisionChanges)
                {
                    int row = detailGrid.Rows.Add(RevisionChangeLabel(change), change.NormalizedTag, RevisionChangeDetail(change));
                    DataGridViewRow gridRow = detailGrid.Rows[row];
                    gridRow.Height = 25;
                    gridRow.Cells[0].Style.Font = new System.Drawing.Font("Segoe UI Semibold", 8F, FontStyle.Bold);
                    gridRow.Cells[0].Style.ForeColor = RevisionChangeColor(change);
                }
                detailGrid.ClearSelection();
            }
            else if (string.Equals(data.Command, "ES_REVISION", StringComparison.OrdinalIgnoreCase) && !string.Equals(data.Status, "FAILED", StringComparison.OrdinalIgnoreCase) && !string.Equals(data.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                gridLabel.Text = "CHANGES";
                AddColumn("Result", 710);
                detailGrid.ScrollBars = ScrollBars.None;
                int row = detailGrid.Rows.Add("No engineering changes detected between the active baseline and current drawing.");
                detailGrid.Rows[row].Height = 28;
                detailGrid.ClearSelection();
            }
            else
            {
                gridLabel.Text = "DETAILS";
                AddColumn("Metric", 190);
                AddColumn("Value", 520);
                foreach (VisualDetail detail in data.Details ?? new List<VisualDetail>()) detailGrid.Rows.Add(detail.Label, detail.Value);
            }
            BuildReviewGrid(data.AuthorityReviewItems);
            UpdateZoomEvidenceButtonState();
        }

        private void ConfigureDistanceGrid()
        {
            distanceGrid.AllowUserToAddRows = false;
            distanceGrid.AllowUserToDeleteRows = false;
            distanceGrid.AllowUserToResizeRows = false;
            distanceGrid.BackgroundColor = Color.FromArgb(30, 31, 34);
            distanceGrid.BorderStyle = BorderStyle.None;
            distanceGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(52, 55, 61), ForeColor = Color.FromArgb(230, 233, 237), Font = new System.Drawing.Font("Segoe UI Semibold", 8F, FontStyle.Bold) };
            distanceGrid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(30, 31, 34), ForeColor = Color.FromArgb(225, 228, 232), SelectionBackColor = Color.FromArgb(55, 75, 95), SelectionForeColor = Color.White, WrapMode = DataGridViewTriState.False };
            distanceGrid.EnableHeadersVisualStyles = false;
            distanceGrid.ReadOnly = true;
            distanceGrid.RowHeadersVisible = false;
            distanceGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            distanceGrid.ColumnHeadersHeight = 26;
            distanceGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        }

        private void BuildDistanceGrid(IEnumerable<VisualDistanceRow> rows)
        {
            distanceGrid.Columns.Clear();
            distanceGrid.Rows.Clear();
            List<VisualDistanceRow> values = (rows ?? Enumerable.Empty<VisualDistanceRow>()).ToList();
            distancePanel.Visible = values.Count > 0;
            if (values.Count == 0) return;
            AddDistanceColumn("Check", 230);
            AddDistanceColumn("Minimum", 105);
            AddDistanceColumn("Observed", 115);
            AddDistanceColumn("Status", 145);
            AddDistanceColumn("Interpretation", 420);
            foreach (VisualDistanceRow value in values) distanceGrid.Rows.Add(value.Name, value.Minimum, value.Observed, value.Status, value.Interpretation);
        }

        private void AddDistanceColumn(string name, int width)
        {
            distanceGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = name,
                MinimumWidth = 55,
                FillWeight = width,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        private void BuildReviewGrid(IEnumerable<StandardAuthorityReviewItem> items)
        {
            reviewGrid.Columns.Clear();
            reviewGrid.Rows.Clear();
            List<StandardAuthorityReviewItem> reviewItems = (items ?? Enumerable.Empty<StandardAuthorityReviewItem>()).ToList();
            reviewPanel.Visible = reviewItems.Count > 0;
            if (reviewItems.Count == 0) return;

            AddReviewColumn("Rule", 235);
            AddReviewColumn("Decision", 170);
            AddReviewColumn("Enforcement", 180);
            AddReviewColumn("Authority evidence", 560);
            foreach (StandardAuthorityReviewItem item in reviewItems)
            {
                int row = reviewGrid.Rows.Add(item.RuleId, item.DecisionState, item.EffectiveEnforcementMode, ReviewEvidence(item));
                reviewGrid.Rows[row].Height = 36;
                reviewGrid.Rows[row].Tag = item;
            }
            UpdateReviewDecisionPanel();
        }

        private void AddReviewColumn(string name, int width)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn
            {
                Name = name, HeaderText = name, MinimumWidth = 55, FillWeight = width,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            reviewGrid.Columns.Add(column);
        }

        private static string ReviewEvidence(StandardAuthorityReviewItem item)
        {
            if (item == null || item.Clauses == null || item.Clauses.Count == 0) return item == null ? string.Empty : item.Message;
            return string.Join(" | ", item.Clauses.Select(clause =>
                clause.ClauseId + " / " + clause.SourceId + " / " + clause.PolicyId + " / " + clause.PolicyClauseId +
                " / " + clause.AuthorityClass + " / " + clause.EnforcementMode + " / " + clause.Rationale));
        }

        private void ReviewGridSelectionChanged(object sender, EventArgs e)
        {
            UpdateReviewDecisionPanel();
        }

        private void UpdateReviewDecisionPanel()
        {
            saveReviewDecisionButton.Enabled = false;
            DataGridViewRow row = reviewGrid.CurrentRow;
            StandardAuthorityReviewItem item = row == null ? null : row.Tag as StandardAuthorityReviewItem;
            if (item == null)
            {
                existingReviewLabel.Text = "Existing human review: <none>";
                evidenceCurrentnessLabel.Text = "Evidence: none";
                historyStateLabel.Text = "History: none";
                existingReviewerLabel.Text = "Reviewer: <none>";
                return;
            }

            existingReviewLabel.Text = "Existing human review: <none>";
            evidenceCurrentnessLabel.Text = "Evidence: none";
            historyStateLabel.Text = "History: none";
            existingReviewerLabel.Text = "Reviewer: <none> | Decision time: <none>";
            if (current == null || current.StandardsReviewPackage == null || string.IsNullOrWhiteSpace(current.StandardsReviewPackagePath) || string.IsNullOrWhiteSpace(current.StandardsReviewPackageSha256))
            {
                decisionStatusLabel.Text = "Decision persistence unavailable: review package context is missing.";
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(current.StandardsReviewPackagePath);
                List<HumanEngineeringReviewDecisionRecord> records = HumanEngineeringReviewDecisionArtifactCatalog.Load(directory, current.StandardsReviewPackage.DrawingName);
                HumanEngineeringReviewDecisionHistoryResolution history = HumanEngineeringReviewDecisionHistoryResolver.Resolve(records);
                HumanEngineeringReviewDecisionHistoryEntry activeEntry = history.Entries.SingleOrDefault(entry => entry.RuleId == item.RuleId && entry.State == HumanEngineeringReviewDecisionHistoryStates.Active);
                if (activeEntry == null)
                {
                    decisionStatusLabel.Text = "No prior human review decision.";
                    saveReviewDecisionButton.Enabled = true;
                    return;
                }

                HumanEngineeringReviewDecisionRecord activeDecision = records.Single(record => record.DecisionRecordId == activeEntry.DecisionRecordId);
                HumanEngineeringReviewDecisionEvaluation evaluation = HumanEngineeringReviewDecisionStalenessValidator.Evaluate(activeDecision, current.StandardsReviewPackage, current.StandardsReviewPackageSha256);
                existingReviewLabel.Text = "Existing human review: " + activeDecision.HumanReviewState;
                evidenceCurrentnessLabel.Text = "Evidence: " + evaluation.Status;
                historyStateLabel.Text = "History: " + activeEntry.State;
                existingReviewerLabel.Text = "Reviewer: " + activeDecision.ReviewerDisplayName + " | Decision time: " + activeDecision.DecisionTimestampUtc.ToString("u", CultureInfo.InvariantCulture);
                decisionStatusLabel.Text = evaluation.Reasons.Count == 0 ? "Existing decision is current." : "Existing decision is stale: " + string.Join(", ", evaluation.Reasons);
                saveReviewDecisionButton.Enabled = true;
                humanReviewComboBox.SelectedItem = activeDecision.HumanReviewState;
            }
            catch (Exception exception)
            {
                decisionStatusLabel.Text = "Decision history unavailable: " + exception.Message;
            }
        }

        private void SaveReviewDecision(object sender, EventArgs e)
        {
            try
            {
                DataGridViewRow row = reviewGrid.CurrentRow;
                StandardAuthorityReviewItem item = row == null ? null : row.Tag as StandardAuthorityReviewItem;
                if (item == null || current == null || current.Document == null)
                {
                    SetNote("Select an engineering review item with a valid active document.");
                    return;
                }
                if (current.StandardsReviewPackage == null || string.IsNullOrWhiteSpace(current.StandardsReviewPackagePath) || string.IsNullOrWhiteSpace(current.StandardsReviewPackageSha256))
                {
                    SetNote("Review decision persistence is unavailable because the review package context is missing.");
                    return;
                }
                Document activeDocument = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager == null
                    ? null
                    : Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (activeDocument == null || !object.ReferenceEquals(activeDocument, current.Document))
                {
                    SetNote("The active AutoCAD document no longer matches the drawing reviewed by ES_PLOTQA. Reactivate that drawing or rerun ES_PLOTQA.");
                    return;
                }
                string activeDrawingName = Path.GetFileName(activeDocument.Name);
                if (!string.Equals(activeDrawingName, current.StandardsReviewPackage.DrawingName, StringComparison.Ordinal))
                {
                    SetNote("The active document does not match the captured review package drawing.");
                    return;
                }
                int dbmodBefore = Convert.ToInt32(Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("DBMOD"), CultureInfo.InvariantCulture);
                string persistedHash = HumanEngineeringReviewDecisionFileStore.ComputeSha256(current.StandardsReviewPackagePath);
                if (!string.Equals(persistedHash, current.StandardsReviewPackageSha256, StringComparison.OrdinalIgnoreCase))
                {
                    SetNote("Review package changed since ES_PLOTQA; rerun ES_PLOTQA before recording a decision.");
                    return;
                }
                string reviewer = (reviewerTextBox.Text ?? string.Empty).Trim();
                string humanReviewState = humanReviewComboBox.SelectedItem == null ? string.Empty : humanReviewComboBox.SelectedItem.ToString();
                if (string.IsNullOrWhiteSpace(reviewer) || (humanReviewState != HumanEngineeringReviewDecisionStates.ReviewAccepted && humanReviewState != HumanEngineeringReviewDecisionStates.ReviewRejected && humanReviewState != HumanEngineeringReviewDecisionStates.NeedsClarification))
                {
                    SetNote("Reviewer and a valid human review state are required.");
                    return;
                }
                string directory = Path.GetDirectoryName(current.StandardsReviewPackagePath);
                List<HumanEngineeringReviewDecisionRecord> records = HumanEngineeringReviewDecisionArtifactCatalog.Load(directory, current.StandardsReviewPackage.DrawingName);
                HumanEngineeringReviewDecisionHistoryResolution history = HumanEngineeringReviewDecisionHistoryResolver.Resolve(records);
                HumanEngineeringReviewDecisionHistoryEntry activeEntry = history.Entries.SingleOrDefault(entry => entry.RuleId == item.RuleId && entry.State == HumanEngineeringReviewDecisionHistoryStates.Active);
                DateTime decisionTimestampUtc = DateTime.UtcNow;
                HumanEngineeringReviewDecisionRecord decision = HumanEngineeringReviewDecisionBuilder.Create(current.StandardsReviewPackage, persistedHash, item.RuleId, humanReviewState, reviewer, decisionTimestampUtc, authenticatedReviewerIdentity: null, comment: string.IsNullOrWhiteSpace(reviewCommentTextBox.Text) ? null : reviewCommentTextBox.Text.Trim(), supersedesDecisionRecordId: activeEntry == null ? null : activeEntry.DecisionRecordId);
                DialogResult confirmation = MessageBox.Show("Rule: " + item.RuleId + "\nHuman Review: " + humanReviewState + "\nReviewer: " + reviewer + "\n\nThis records an immutable human review decision.\nIt does not approve or activate an engineering rule.", "Confirm Review Decision", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (confirmation != DialogResult.OK) return;
                string decisionPath = HumanEngineeringReviewDecisionFileStore.Write(decision, directory);
                int dbmodAfter = Convert.ToInt32(Autodesk.AutoCAD.ApplicationServices.Application.GetSystemVariable("DBMOD"), CultureInfo.InvariantCulture);
                if (dbmodBefore != dbmodAfter)
                {
                    SetNote("Review decision workflow changed DBMOD; no drawing save was performed.");
                    return;
                }
                SetNote("Immutable human review decision saved: " + decisionPath);
                UpdateReviewDecisionPanel();
            }
            catch (Exception exception)
            {
                SetNote("Review decision was not saved: " + exception.Message);
            }
        }

        private static string RevisionChangeLabel(RevisionChangeRecord change)
        {
            switch (change == null ? string.Empty : change.ChangeKind)
            {
                case "EquipmentAdded": return "ADDED";
                case "EquipmentRemoved": return "REMOVED";
                case "EquipmentCoordinateEvidenceChanged": return "COORDINATE CHANGED";
                case "EquipmentAttributeEvidenceChanged": return "ATTRIBUTE CHANGED";
                default: return string.IsNullOrWhiteSpace(change?.ChangeKind) ? "CHANGED" : change.ChangeKind;
            }
        }

        private static Color RevisionChangeColor(RevisionChangeRecord change)
        {
            switch (change == null ? string.Empty : change.ChangeKind)
            {
                case "EquipmentAdded": return Color.FromArgb(112, 214, 154);
                case "EquipmentRemoved": return Color.FromArgb(238, 139, 139);
                case "EquipmentCoordinateEvidenceChanged": return Color.FromArgb(105, 190, 245);
                case "EquipmentAttributeEvidenceChanged": return Color.FromArgb(238, 193, 106);
                default: return Color.White;
            }
        }

        private static string RevisionChangeDetail(RevisionChangeRecord change)
        {
            if (change == null) return string.Empty;
            switch (change.ChangeKind)
            {
                case "EquipmentAdded":
                    return "New equipment detected in the current drawing.";
                case "EquipmentRemoved":
                    return "Present in the baseline but not found in the current drawing.";
                case "EquipmentCoordinateEvidenceChanged":
                    return "Coordinates: " + RevisionCoordinates(change.Baseline) + "  ->  " + RevisionCoordinates(change.Current);
                case "EquipmentAttributeEvidenceChanged":
                    return RevisionAttributeDetail(change);
                default:
                    return string.Join(", ", change.ChangedFields ?? new List<string>());
            }
        }

        private static string RevisionCoordinates(RevisionEquipmentRecord record)
        {
            if (record == null || record.CompleteCoordinatePairs == null || record.CompleteCoordinatePairs.Count == 0) return "not resolved";
            return string.Join("; ", record.CompleteCoordinatePairs.Select(x =>
                "E " + x.Easting.ToString(CultureInfo.InvariantCulture) + " / N " + x.Northing.ToString(CultureInfo.InvariantCulture)));
        }

        private static string RevisionAttributeDetail(RevisionChangeRecord change)
        {
            var parts = new List<string>();
            foreach (string field in change.ChangedFields ?? new List<string>())
            {
                if (string.Equals(field, "Description", StringComparison.Ordinal))
                    parts.Add("Description: " + RevisionValues(change.Baseline?.DescriptionValues) + "  ->  " + RevisionValues(change.Current?.DescriptionValues));
                else if (string.Equals(field, "Installation", StringComparison.Ordinal))
                    parts.Add("Installation: " + RevisionValues(change.Baseline?.InstallationValues) + "  ->  " + RevisionValues(change.Current?.InstallationValues));
                else
                    parts.Add(field + " changed");
            }
            return parts.Count == 0 ? "Tracked equipment attributes changed." : string.Join(" | ", parts);
        }

        private static string RevisionValues(IEnumerable<string> values)
        {
            if (values == null) return "none";
            List<string> items = values.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            return items.Count == 0 ? "none" : string.Join("; ", items);
        }

        private void AddColumn(string name, int width)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn
            {
                Name = name, HeaderText = name, MinimumWidth = 55, FillWeight = width,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            detailGrid.Columns.Add(column);
        }

        private void DetailGridSelectionChanged(object sender, EventArgs e)
        {
            UpdateZoomEvidenceButtonState();
        }

        private void UpdateZoomEvidenceButtonState()
        {
            DataGridViewRow row = detailGrid.CurrentRow;
            QaFinding finding = row == null ? null : row.Tag as QaFinding;
            zoomEvidenceButton.Enabled = finding != null && !string.IsNullOrWhiteSpace(FirstHandle(finding)) && current != null && current.Document != null;
        }

        private void OpenOutputFolder(object sender, EventArgs e)
        {
            try
            {
                if (current == null || string.IsNullOrWhiteSpace(current.OutputPath)) return;
                string folder = Directory.Exists(current.OutputPath) ? current.OutputPath : Path.GetDirectoryName(current.OutputPath);
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    SetNote("Output folder is not available: " + current.OutputPath);
                    return;
                }
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + folder + "\"", UseShellExecute = false });
            }
            catch (Exception exception)
            {
                SetNote("Could not open output folder: " + exception.Message);
            }
        }

        private void ZoomToEvidence(object sender, EventArgs e)
        {
            try
            {
                DataGridViewRow row = detailGrid.CurrentRow;
                QaFinding finding = row == null ? null : row.Tag as QaFinding;
                string handleText = finding == null ? null : FirstHandle(finding);
                Document document = current == null ? null : current.Document;
                if (document == null || string.IsNullOrWhiteSpace(handleText))
                {
                    SetNote("No valid source handle is available for this finding.");
                    return;
                }

                string selectionHandle = handleText.Split(new[] { ':' }, 2)[0];
                long handleValue;
                if (!long.TryParse(selectionHandle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out handleValue))
                {
                    SetNote("The finding source handle is invalid.");
                    return;
                }

                // Validate that the evidence still resolves in the drawing, but let
                // AutoCAD's own ZOOM Object command perform the actual navigation.
                // This avoids WCS/DCS/view-target differences between drawings.
                bool modelSpaceEvidence;
                ObjectId targetModelLayoutId = ObjectId.Null;
                using (DocumentLock documentLock = document.LockDocument())
                {
                    ObjectId objectId = document.Database.GetObjectId(false, new Handle(handleValue), 0);
                    if (objectId.IsNull || !objectId.IsValid || objectId.IsErased)
                        throw new InvalidOperationException("The source handle does not resolve to a live drawing object.");

                    using (Transaction transaction = document.TransactionManager.StartTransaction())
                    {
                        Entity entity = transaction.GetObject(objectId, OpenMode.ForRead, false) as Entity;
                        if (entity == null) throw new InvalidOperationException("The source object is not drawable.");
                        BlockTable blockTable = (BlockTable)transaction.GetObject(document.Database.BlockTableId, OpenMode.ForRead, false);
                        ObjectId modelSpaceBlockTableRecordId = blockTable[BlockTableRecord.ModelSpace];
                        BlockTableRecord modelSpace = transaction.GetObject(modelSpaceBlockTableRecordId, OpenMode.ForRead, false) as BlockTableRecord;
                        modelSpaceEvidence = entity.OwnerId == modelSpaceBlockTableRecordId;
                        if (modelSpaceEvidence && modelSpace != null)
                            targetModelLayoutId = modelSpace.LayoutId;
                    }

                    if (modelSpaceEvidence && !targetModelLayoutId.IsNull && Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument == document)
                    {
                        LayoutManager layoutManager = LayoutManager.Current;
                        ObjectId currentLayoutId = layoutManager.GetLayoutId(layoutManager.CurrentLayout);
                        if (currentLayoutId != targetModelLayoutId)
                            layoutManager.SetCurrentLayoutId(targetModelLayoutId);
                    }
                }

                string safeHandle = new string(selectionHandle.Where(Uri.IsHexDigit).ToArray());
                string zoomCommand =
                    "(progn (setq _es_e (handent \"" + safeHandle + "\")) " +
                    "(if _es_e (progn (command \"_.ZOOM\" \"_O\" _es_e \"\") " +
                    "(sssetfirst nil (ssadd _es_e))))) ";
                document.SendStringToExecute(zoomCommand + Environment.NewLine, true, false, false);
                SetNote("Zoom requested for evidence handle " + handleText + " using AutoCAD ZOOM Object.");
            }
            catch (Exception exception)
            {
                SetNote("Evidence could not be resolved: " + exception.Message);
            }
        }

        private static string Subject(QaFinding finding)
        {
            if (!string.IsNullOrWhiteSpace(finding.Evidence == null ? null : finding.Evidence.NormalizedTag)) return finding.Evidence.NormalizedTag;
            if (!string.IsNullOrWhiteSpace(finding.SubjectId)) return finding.SubjectId;
            return finding.SubjectType;
        }

        private static string FirstHandle(QaFinding finding)
        {
            IEnumerable<string> handles = (finding.SourceHandles ?? new List<string>())
                .Concat(finding.Evidence == null ? Enumerable.Empty<string>() : finding.Evidence.DrawingTagSourceHandles ?? new List<string>())
                .Concat(finding.Evidence == null ? Enumerable.Empty<string>() : finding.Evidence.EquipmentListSourceHandles ?? new List<string>())
                .Concat(finding.Evidence == null ? Enumerable.Empty<string>() : finding.Evidence.Occurrences == null ? Enumerable.Empty<string>() : finding.Evidence.Occurrences.Select(x => x.SourceHandle))
                .Concat(finding.Evidence == null ? Enumerable.Empty<string>() : new[] { finding.Evidence.TagSourceHandle, finding.Evidence.GeometryAssociation == null ? null : finding.Evidence.GeometryAssociation.LabelSourceHandle, finding.Evidence.GeometryAssociation == null ? null : finding.Evidence.GeometryAssociation.SelectedGeometryHandle, finding.Evidence.SpatialCoordinateConstraint == null ? null : finding.Evidence.SpatialCoordinateConstraint.LabelSourceHandle, finding.Evidence.SpatialCoordinateConstraint == null ? null : finding.Evidence.SpatialCoordinateConstraint.SelectedGeometryHandle });
            return handles.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        }

        private static Color StatusColor(string status)
        {
            string value = (status ?? string.Empty).ToUpperInvariant();
            if (value.Contains("FAIL") || value.Contains("ERROR")) return Color.FromArgb(255, 112, 112);
            if (value.Contains("WARN") || value.Contains("CHANGE")) return Color.FromArgb(255, 190, 92);
            if (value.Contains("CANCEL")) return Color.FromArgb(175, 181, 190);
            return Color.FromArgb(108, 220, 155);
        }
    }
}

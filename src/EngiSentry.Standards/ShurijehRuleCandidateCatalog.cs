using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public static class ShurijehRuleKinds
    {
        public const string WellToRoad2D = "WellToRoad2D";
        public const string FlareToRoad2D = "FlareToRoad2D";
        public const string FireTrainingAreaToFacility2D = "FireTrainingAreaToFacility2D";
        public const string StorageToBoundaryOrPublicHighway2D = "StorageToBoundaryOrPublicHighway2D";
        public const string DikedAreaRoadAccessTopology = "DikedAreaRoadAccessTopology";
        public const string RestrictedAreaToSecurityFenceSemantic = "RestrictedAreaToSecurityFenceSemantic";
    }

    public static class ShurijehRuleCandidateCatalog
    {
        public const string SourceId = "ips.c-sf-550.rev1.2015.amd2021";
        public const string SourceSha256 = "4D2ABB7122365E9FF9F92EF8B4717A6381AA3F10AD322674FAD15B7DEA73D1DF";
        public const string RevisionOrEdition = "First Edition / October 2015 / approved Apr-2021 update included";
        public const string ProjectApplicabilityReference = "791124-2-0000-TRC-PM-LI-002-B01 / PDF page 71 / IPS-C-SF-550 / 2015";
        public const string ReviewRequired = "REVIEW_REQUIRED";

        private static readonly EngineeringRuleCandidateDefinition[] BuiltIns =
        {
            Candidate("shurijeh.restricted-area.security-fence.semantic",
                "std.shurijeh.restricted-area.security-fence.semantic",
                "Restricted Area Must Be Materialised by Security Fence",
                ShurijehRuleKinds.RestrictedAreaToSecurityFenceSemantic,
                "6.2.1", 8, "RestrictedArea", "SecurityFence",
                "RestrictedAreaBoundaryToSecurityFence", "", null,
                "RestrictedAreaContourKnown && SecurityFenceGeometryKnown",
                "Mandatory", "Restricted-area contour derives from hazard assessment; no generic equipment-to-fence distance is granted."),
            Candidate("shurijeh.diked-area.access-road.topology",
                "std.shurijeh.diked-area.access-road.topology",
                "Diked Area Road Access Topology",
                ShurijehRuleKinds.DikedAreaRoadAccessTopology,
                "7.1.4.2", 11, "DikedArea", "RoadNetwork",
                "RoadOnFourSidesLinkedAndAtLeastOneAccessRoad", "", null,
                "DikedAreaGeometryKnown && RoadNetworkKnown",
                "MandatoryConditional", "Unless otherwise approved by Client; approval must be explicit and must never be inferred."),

            Distance("shurijeh.well.public-road.500m",
                "std.shurijeh.well.public-road.minimum-distance",
                "Oil/Gas Well to Public Road Minimum Distance",
                ShurijehRuleKinds.WellToRoad2D,
                "Appendix C / Table C.3 / row 13", 27, "OilGasWell", "PublicRoad",
                "WellToRoad2D", 500m, "WellSemanticKnown && RoadClass==Public"),

            Distance("shurijeh.well.private-road.200m",
                "std.shurijeh.well.private-road.minimum-distance",
                "Oil/Gas Well to Private or Branch Road Minimum Distance",
                ShurijehRuleKinds.WellToRoad2D,
                "Appendix C / Table C.3 / row 14", 27, "OilGasWell", "PrivateOrBranchRoad",
                "WellToRoad2D", 200m, "WellSemanticKnown && RoadClass==PrivateOrBranch"),
            Distance("shurijeh.burning-pit.public-road.200m",
                "std.shurijeh.burning-pit.public-road.minimum-distance",
                "Burning Pit to Public Main Road Minimum Distance",
                ShurijehRuleKinds.FlareToRoad2D,
                "Appendix C / Table C.2", 27, "OilOrGasBurningPit", "PublicMainRoad",
                "FlareOrBurningPitToRoad2D", 200m, "SourceSubtype==BurningPit && RoadClass==PublicMain"),
            Distance("shurijeh.burning-pit.private-road.200m",
                "std.shurijeh.burning-pit.private-road.minimum-distance",
                "Burning Pit to Private or Branch Road Minimum Distance",
                ShurijehRuleKinds.FlareToRoad2D,
                "Appendix C / Table C.2", 27, "OilOrGasBurningPit", "PrivateOrBranchRoad",
                "FlareOrBurningPitToRoad2D", 200m, "SourceSubtype==BurningPit && RoadClass==PrivateOrBranch"),
            Distance("shurijeh.ground-flare.public-road.200m",
                "std.shurijeh.ground-flare.public-road.minimum-distance",
                "Ground-Level Flare to Public Main Road Minimum Distance",
                ShurijehRuleKinds.FlareToRoad2D,
                "Appendix C / Table C.2", 27, "GroundLevelFlare", "PublicMainRoad",
                "FlareOrBurningPitToRoad2D", 200m, "SourceSubtype==GroundLevelFlare && RoadClass==PublicMain"),
            Distance("shurijeh.ground-flare.private-road.150m",
                "std.shurijeh.ground-flare.private-road.minimum-distance",
                "Ground-Level Flare to Private or Branch Road Minimum Distance",
                ShurijehRuleKinds.FlareToRoad2D,
                "Appendix C / Table C.2", 27, "GroundLevelFlare", "PrivateOrBranchRoad",
                "FlareOrBurningPitToRoad2D", 150m, "SourceSubtype==GroundLevelFlare && RoadClass==PrivateOrBranch"),
            Distance("shurijeh.high-level-flare.public-road.150m",
                "std.shurijeh.high-level-flare.public-road.minimum-distance",
                "High-Level Flare to Public Main Road Minimum Distance",
                ShurijehRuleKinds.FlareToRoad2D,
                "Appendix C / Table C.2", 27, "HighLevelFlare", "PublicMainRoad",
                "FlareOrBurningPitToRoad2D", 150m, "SourceSubtype==HighLevelFlare && RoadClass==PublicMain"),
            Distance("shurijeh.high-level-flare.private-road.100m",
                "std.shurijeh.high-level-flare.private-road.minimum-distance",
                "High-Level Flare to Private or Branch Road Minimum Distance",
                ShurijehRuleKinds.FlareToRoad2D,
                "Appendix C / Table C.2", 27, "HighLevelFlare", "PrivateOrBranchRoad",
                "FlareOrBurningPitToRoad2D", 100m, "SourceSubtype==HighLevelFlare && RoadClass==PrivateOrBranch"),
            Distance("shurijeh.cold-flare.public-road.100m",
                "std.shurijeh.cold-flare.public-road.minimum-distance",
                "Unit Cold Flare to Public Main Road Minimum Distance",
                ShurijehRuleKinds.FlareToRoad2D,
                "Appendix C / Table C.2", 27, "UnitColdFlare", "PublicMainRoad",
                "FlareOrBurningPitToRoad2D", 100m, "SourceSubtype==UnitColdFlare && RoadClass==PublicMain"),
            Distance("shurijeh.cold-flare.private-road.50m",
                "std.shurijeh.cold-flare.private-road.minimum-distance",
                "Unit Cold Flare to Private or Branch Road Minimum Distance",
                ShurijehRuleKinds.FlareToRoad2D,
                "Appendix C / Table C.2", 27, "UnitColdFlare", "PrivateOrBranchRoad",
                "FlareOrBurningPitToRoad2D", 50m, "SourceSubtype==UnitColdFlare && RoadClass==PrivateOrBranch"),
            Distance("shurijeh.fire-training.process-facilities.60m",
                "std.shurijeh.fire-training.process-facilities.minimum-distance",
                "Fire Training Area to Listed Process Facilities Minimum Distance",
                ShurijehRuleKinds.FireTrainingAreaToFacility2D,
                "7.1.7", 14, "FireTrainingArea",
                "ProcessUnitBatteryLimit|MainControlRoom|FiredSteamGenerator|FirePump|CoolingTower|StorageTank",
                "FireTrainingAreaToFacility2D", 60m, "FireTrainingAreaKnown && TargetClassIn60mGroup"),
            Distance("shurijeh.fire-training.boundary-buildings.75m",
                "std.shurijeh.fire-training.boundary-buildings.minimum-distance",
                "Fire Training Area to Boundary/Buildings/Substation Minimum Distance",
                ShurijehRuleKinds.FireTrainingAreaToFacility2D,
                "7.1.7", 14, "FireTrainingArea",
                "PropertyBoundary|AdministrationBuilding|Shop|SimilarBuilding|MainSubstation",
                "FireTrainingAreaToFacility2D", 75m, "FireTrainingAreaKnown && TargetClassIn75mGroup"),

            Distance("shurijeh.refrigerated-storage.boundary-highway.60m",
                "std.shurijeh.refrigerated-storage.boundary-highway.minimum-distance",
                "Refrigerated Storage to Property Boundary/Public Highway Minimum Distance",
                ShurijehRuleKinds.StorageToBoundaryOrPublicHighway2D,
                "Appendix B / Table B.4", 24, "RefrigeratedStorageVessel", "PropertyBoundaryOrPublicHighway",
                "StorageToBoundaryOrPublicHighway2D", 60m, "StorageClassIn{DomeRoofTank,Sphere,Spheroid}"),
            Distance("shurijeh.atmospheric-storage.lowflash-crude.boundary-highway.60m",
                "std.shurijeh.atmospheric-storage.lowflash-crude.boundary-highway.minimum-distance",
                "Atmospheric Low-Flash/Crude Storage to Boundary/Public Highway Minimum Distance",
                ShurijehRuleKinds.StorageToBoundaryOrPublicHighway2D,
                "Appendix B / Table B.5", 24, "AtmosphericLowFlashOrCrudeStorageTank", "PropertyBoundaryOrPublicHighway",
                "StorageToBoundaryOrPublicHighway2D", 60m, "RoofAndStockClassMatchesTableB5Mandatory60mColumns"),
            Distance("shurijeh.pressure-storage.boundary-highway.60m",
                "std.shurijeh.pressure-storage.boundary-highway.minimum-distance",
                "Non-Refrigerated Pressure Storage to Boundary/Public Highway Minimum Distance",
                ShurijehRuleKinds.StorageToBoundaryOrPublicHighway2D,
                "Appendix B / Table B.7", 26, "NonRefrigeratedPressureStorageVessel", "PropertyBoundaryOrPublicHighway",
                "StorageToBoundaryOrPublicHighway2D", 60m, "StorageClassIn{Sphere,Spheroid,Drum}")
        };

        public static IReadOnlyList<EngineeringRuleCandidateDefinition> Candidates =>
            BuiltIns.OrderBy(x => x.CandidateId, StringComparer.Ordinal).Select(Copy).ToList();
        public static void Validate()
        {
            string[] allowedKinds = {
                ShurijehRuleKinds.WellToRoad2D,
                ShurijehRuleKinds.FlareToRoad2D,
                ShurijehRuleKinds.FireTrainingAreaToFacility2D,
                ShurijehRuleKinds.StorageToBoundaryOrPublicHighway2D,
                ShurijehRuleKinds.DikedAreaRoadAccessTopology,
                ShurijehRuleKinds.RestrictedAreaToSecurityFenceSemantic
            };
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var ruleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (EngineeringRuleCandidateDefinition candidate in BuiltIns)
            {
                if (candidate == null) throw new InvalidOperationException("Shurijeh rule catalog contains null.");
                if (!ids.Add(candidate.CandidateId)) throw new InvalidOperationException("Duplicate CandidateId: " + candidate.CandidateId);
                if (!ruleIds.Add(candidate.RuleId)) throw new InvalidOperationException("Duplicate RuleId: " + candidate.RuleId);
                if (!allowedKinds.Contains(candidate.RuleKind, StringComparer.Ordinal)) throw new InvalidOperationException("Unsupported RuleKind: " + candidate.RuleKind);
                if (candidate.SourceId != SourceId || candidate.SourceSha256 != SourceSha256) throw new InvalidOperationException("Unexpected source binding: " + candidate.CandidateId);
                if (candidate.RevisionOrEdition != RevisionOrEdition) throw new InvalidOperationException("Unexpected source revision: " + candidate.CandidateId);
                if (candidate.ProjectApplicabilityReference != ProjectApplicabilityReference) throw new InvalidOperationException("Missing project applicability binding: " + candidate.CandidateId);
                if (string.IsNullOrWhiteSpace(candidate.ClauseReference) || candidate.PdfPageNumber <= 0) throw new InvalidOperationException("Incomplete clause provenance: " + candidate.CandidateId);
                if (candidate.ExecutionState != ReviewRequired) throw new InvalidOperationException("Candidate is not safely review-gated: " + candidate.CandidateId);
                if (candidate.MinimumValue.HasValue && (candidate.MinimumValue.Value <= 0m || candidate.Unit != "m")) throw new InvalidOperationException("Invalid numeric candidate: " + candidate.CandidateId);
                if (!candidate.MinimumValue.HasValue && !string.IsNullOrEmpty(candidate.Unit)) throw new InvalidOperationException("Non-numeric candidate has a unit: " + candidate.CandidateId);
            }
        }
        private static EngineeringRuleCandidateDefinition Distance(
            string candidateId, string ruleId, string title, string ruleKind,
            string clause, int page, string subject, string target,
            string basis, decimal minimumMeters, string applicability)
        {
            return Candidate(candidateId, ruleId, title, ruleKind, clause, page, subject, target,
                basis, "m", minimumMeters, applicability, "Mandatory",
                "No automatic deviation or approval inference; any exception must pass project governance.");
        }

        private static EngineeringRuleCandidateDefinition Candidate(
            string candidateId, string ruleId, string title, string ruleKind,
            string clause, int page, string subject, string target,
            string basis, string unit, decimal? minimumValue, string applicability,
            string normativeStrength, string approvalSemantics)
        {
            return new EngineeringRuleCandidateDefinition
            {
                CandidateId = candidateId,
                RuleId = ruleId,
                Title = title,
                RuleKind = ruleKind,
                SourceId = SourceId,
                SourceSha256 = SourceSha256,
                RevisionOrEdition = RevisionOrEdition,
                ClauseReference = clause,
                PdfPageNumber = page,
                ProjectApplicabilityReference = ProjectApplicabilityReference,
                SubjectClass = subject,
                TargetClass = target,
                MeasurementBasisKey = basis,
                Unit = unit,
                MinimumValue = minimumValue,
                ApplicabilityPredicate = applicability,
                NormativeStrength = normativeStrength,
                ApprovalOrDeviationSemantics = approvalSemantics,
                ExecutionState = ReviewRequired
            };
        }

        private static EngineeringRuleCandidateDefinition Copy(EngineeringRuleCandidateDefinition x)
        {
            return new EngineeringRuleCandidateDefinition
            {
                CandidateId=x.CandidateId, RuleId=x.RuleId, Title=x.Title, RuleKind=x.RuleKind,
                SourceId=x.SourceId, SourceSha256=x.SourceSha256, RevisionOrEdition=x.RevisionOrEdition,
                ClauseReference=x.ClauseReference, PdfPageNumber=x.PdfPageNumber,
                ProjectApplicabilityReference=x.ProjectApplicabilityReference,
                SubjectClass=x.SubjectClass, TargetClass=x.TargetClass,
                MeasurementBasisKey=x.MeasurementBasisKey, Unit=x.Unit, MinimumValue=x.MinimumValue,
                ApplicabilityPredicate=x.ApplicabilityPredicate, NormativeStrength=x.NormativeStrength,
                ApprovalOrDeviationSemantics=x.ApprovalOrDeviationSemantics, ExecutionState=x.ExecutionState
            };
        }
    }
}

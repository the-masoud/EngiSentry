using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class EngineeringDistanceCandidateCatalogValidationException : Exception
    {
        public EngineeringDistanceCandidateCatalogValidationException(string message) : base(message) { }
    }

    public sealed class EngineeringDistanceCandidateCatalog
    {
        private static readonly EngineeringDistanceCandidateDefinition[] BuiltIns =
        {
            new EngineeringDistanceCandidateDefinition { CandidateId = "candidate.processing-equipment.access-road-edge.3m", PackId = "candidate.project.access-road-clearance", RuleId = "std.plotplan.processing-equipment.access-road-edge.minimum-clearance", Title = "Minimum Processing Equipment to Access Road Edge Clearance", MeasurementKind = "EquipmentToRoad2D", ProposedMinimumDistanceMeters = 3m, RequiredMeasurementBasisKey = "AccessRoadEdge", RequiredApplicabilityKey = "ProcessingEquipment", ApprovalStatus = "PENDING_ENGINEERING_APPROVAL", ClauseIds = new List<string> { "ips.e-pr-190.8.2.3", "trc.pcd-pd-135.3.3.9.b" } },
            new EngineeringDistanceCandidateDefinition { CandidateId = "candidate.equipment.security-fence.30m", PackId = "candidate.project.security-fence-clearance", RuleId = "std.plotplan.equipment.security-fence.minimum-clearance", Title = "Minimum Equipment to Security Fence Clearance", MeasurementKind = "EquipmentToBoundary2D", ProposedMinimumDistanceMeters = 30m, RequiredMeasurementBasisKey = "SecurityFence", RequiredApplicabilityKey = "Equipment", ApprovalStatus = "PENDING_ENGINEERING_APPROVAL", ClauseIds = new List<string> { "ips.e-pr-190.f.11.2", "trc.pcd-pd-135.3.3.10.a" } }
        };

        private readonly List<EngineeringDistanceCandidateDefinition> candidates;

        private EngineeringDistanceCandidateCatalog(IEnumerable<EngineeringDistanceCandidateDefinition> candidates)
        {
            this.candidates = candidates.OrderBy(x => x.CandidateId, StringComparer.Ordinal).Select(Copy).ToList();
        }

        public static IReadOnlyList<EngineeringDistanceCandidateDefinition> BuiltInCandidates => BuiltIns.OrderBy(x => x.CandidateId, StringComparer.Ordinal).Select(Copy).ToList();
        public IReadOnlyList<EngineeringDistanceCandidateDefinition> Candidates => candidates.Select(Copy).ToList();

        public static EngineeringDistanceCandidateCatalog Create(StandardClauseRegistry clauseRegistry, IEnumerable<EngineeringDistanceCandidateDefinition> candidates)
        {
            if (clauseRegistry == null) throw new ArgumentNullException(nameof(clauseRegistry));
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            List<EngineeringDistanceCandidateDefinition> supplied = candidates.ToList();
            var candidateIds = new HashSet<string>(StringComparer.Ordinal);
            var packIds = new HashSet<string>(StringComparer.Ordinal);
            var ruleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (EngineeringDistanceCandidateDefinition candidate in supplied)
            {
                if (candidate == null) throw new EngineeringDistanceCandidateCatalogValidationException("engineering distance candidate catalog contains a null candidate");
                if (string.IsNullOrWhiteSpace(candidate.CandidateId)) throw new EngineeringDistanceCandidateCatalogValidationException("engineering distance candidate CandidateId is required");
                if (!candidateIds.Add(candidate.CandidateId)) throw new EngineeringDistanceCandidateCatalogValidationException("duplicate engineering distance candidate CandidateId: " + candidate.CandidateId);
                if (string.IsNullOrWhiteSpace(candidate.PackId)) throw new EngineeringDistanceCandidateCatalogValidationException("engineering distance candidate PackId is required: " + candidate.CandidateId);
                if (!packIds.Add(candidate.PackId)) throw new EngineeringDistanceCandidateCatalogValidationException("duplicate engineering distance candidate PackId: " + candidate.PackId);
                if (string.IsNullOrWhiteSpace(candidate.RuleId)) throw new EngineeringDistanceCandidateCatalogValidationException("engineering distance candidate RuleId is required: " + candidate.CandidateId);
                if (!ruleIds.Add(candidate.RuleId)) throw new EngineeringDistanceCandidateCatalogValidationException("duplicate engineering distance candidate RuleId: " + candidate.RuleId);
                if (string.IsNullOrWhiteSpace(candidate.Title)) throw new EngineeringDistanceCandidateCatalogValidationException("engineering distance candidate Title is required: " + candidate.CandidateId);
                if (candidate.MeasurementKind != "EquipmentToRoad2D" && candidate.MeasurementKind != "EquipmentToBoundary2D") throw new EngineeringDistanceCandidateCatalogValidationException("unsupported engineering distance candidate MeasurementKind: " + candidate.CandidateId + " | " + candidate.MeasurementKind);
                if (candidate.ProposedMinimumDistanceMeters <= 0m) throw new EngineeringDistanceCandidateCatalogValidationException("engineering distance candidate ProposedMinimumDistanceMeters must be greater than zero: " + candidate.CandidateId);
                if (!EngineeringDistanceEvidenceKeys.IsSupportedMeasurementBasisKey(candidate.MeasurementKind, candidate.RequiredMeasurementBasisKey)) throw new EngineeringDistanceCandidateCatalogValidationException("unsupported engineering distance candidate RequiredMeasurementBasisKey: " + candidate.CandidateId + " | " + candidate.RequiredMeasurementBasisKey);
                if (!EngineeringDistanceEvidenceKeys.IsSupportedApplicabilityKey(candidate.RequiredApplicabilityKey)) throw new EngineeringDistanceCandidateCatalogValidationException("unsupported engineering distance candidate RequiredApplicabilityKey: " + candidate.CandidateId + " | " + candidate.RequiredApplicabilityKey);
                if (candidate.ApprovalStatus != "PENDING_ENGINEERING_APPROVAL") throw new EngineeringDistanceCandidateCatalogValidationException("unsupported engineering distance candidate ApprovalStatus: " + candidate.CandidateId + " | " + candidate.ApprovalStatus);
                if (candidate.ClauseIds == null || candidate.ClauseIds.Count == 0) throw new EngineeringDistanceCandidateCatalogValidationException("engineering distance candidate ClauseIds are required: " + candidate.CandidateId);
                var clauseIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (string clauseId in candidate.ClauseIds)
                {
                    if (string.IsNullOrWhiteSpace(clauseId) || !clauseIds.Add(clauseId)) throw new EngineeringDistanceCandidateCatalogValidationException("duplicate or invalid engineering distance candidate ClauseId: " + candidate.CandidateId);
                    if (!clauseRegistry.TryGet(clauseId, out _)) throw new EngineeringDistanceCandidateCatalogValidationException("engineering distance candidate references unknown ClauseId: " + candidate.CandidateId + " | " + clauseId);
                }
            }
            return new EngineeringDistanceCandidateCatalog(supplied);
        }

        public IReadOnlyList<StandardRulePackDefinition> ToStandardRulePacks() => candidates.OrderBy(x => x.CandidateId, StringComparer.Ordinal).Select(candidate => new StandardRulePackDefinition
        {
            PackId = candidate.PackId,
            Name = candidate.Title,
            Rules = new List<StandardRuleDefinition> { new StandardRuleDefinition { RuleId = candidate.RuleId, Title = candidate.Title, ClauseIds = candidate.ClauseIds.OrderBy(x => x, StringComparer.Ordinal).ToList() } }
        }).ToList();

        private static EngineeringDistanceCandidateDefinition Copy(EngineeringDistanceCandidateDefinition candidate) => new EngineeringDistanceCandidateDefinition
        {
            CandidateId = candidate.CandidateId,
            PackId = candidate.PackId,
            RuleId = candidate.RuleId,
            Title = candidate.Title,
            MeasurementKind = candidate.MeasurementKind,
            ProposedMinimumDistanceMeters = candidate.ProposedMinimumDistanceMeters,
            RequiredMeasurementBasisKey = candidate.RequiredMeasurementBasisKey,
            RequiredApplicabilityKey = candidate.RequiredApplicabilityKey,
            ApprovalStatus = candidate.ApprovalStatus,
            ClauseIds = candidate.ClauseIds == null ? new List<string>() : candidate.ClauseIds.ToList()
        };
    }
}

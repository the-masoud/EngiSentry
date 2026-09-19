using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class EngineeringDistanceApprovalHistoryValidationException : Exception
    {
        public EngineeringDistanceApprovalHistoryValidationException(string message) : base(message) { }
    }

    public static class EngineeringDistanceApprovalHistoryResolver
    {
        public static EngineeringDistanceApprovalHistoryResolution Resolve(IEnumerable<EngineeringDistanceApprovalRecord> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            List<EngineeringDistanceApprovalRecord> history = records.ToList();
            var byId = new Dictionary<string, EngineeringDistanceApprovalRecord>(StringComparer.Ordinal);
            foreach (EngineeringDistanceApprovalRecord record in history)
            {
                if (record == null) throw new EngineeringDistanceApprovalHistoryValidationException("engineering distance approval history contains a null record");
                EngineeringDistanceApprovalRecordValidator.Validate(record);
                if (byId.ContainsKey(record.ApprovalRecordId)) throw new EngineeringDistanceApprovalHistoryValidationException("duplicate engineering distance approval ApprovalRecordId: " + record.ApprovalRecordId);
                byId.Add(record.ApprovalRecordId, record);
            }
            var supersededBy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (EngineeringDistanceApprovalRecord record in history)
            {
                string? supersedesId = record.SupersedesApprovalRecordId;
                if (supersedesId == null || supersedesId.Length == 0) continue;
                if (!byId.TryGetValue(supersedesId, out EngineeringDistanceApprovalRecord target)) throw new EngineeringDistanceApprovalHistoryValidationException("supersession target was not found: " + supersedesId);
                if (!string.Equals(record.ProjectDirectory, target.ProjectDirectory, StringComparison.Ordinal) || !string.Equals(record.CandidateBinding.CandidateId, target.CandidateBinding.CandidateId, StringComparison.Ordinal)) throw new EngineeringDistanceApprovalHistoryValidationException("supersession target has a different ProjectDirectory or CandidateId: " + record.ApprovalRecordId);
                if (supersededBy.ContainsKey(target.ApprovalRecordId)) throw new EngineeringDistanceApprovalHistoryValidationException("ambiguous supersession branch for ApprovalRecordId: " + target.ApprovalRecordId);
                supersededBy.Add(target.ApprovalRecordId, record.ApprovalRecordId);
            }
            DetectCycles(byId, supersededBy);
            foreach (IGrouping<string, EngineeringDistanceApprovalRecord> group in history.GroupBy(x => x.ProjectDirectory + "\u001f" + x.CandidateBinding.CandidateId, StringComparer.Ordinal)) if (group.Count(x => !supersededBy.ContainsKey(x.ApprovalRecordId)) != 1) throw new EngineeringDistanceApprovalHistoryValidationException("history group must contain exactly one active head: " + group.Key);
            return new EngineeringDistanceApprovalHistoryResolution { Entries = history.OrderBy(x => x.ProjectDirectory, StringComparer.Ordinal).ThenBy(x => x.CandidateBinding.CandidateId, StringComparer.Ordinal).ThenBy(x => x.ApprovalRecordId, StringComparer.Ordinal).Select(x => new EngineeringDistanceApprovalHistoryEntry { ApprovalRecordId = x.ApprovalRecordId, ProjectDirectory = x.ProjectDirectory, CandidateId = x.CandidateBinding.CandidateId, State = supersededBy.ContainsKey(x.ApprovalRecordId) ? EngineeringDistanceApprovalHistoryStates.Superseded : EngineeringDistanceApprovalHistoryStates.Active, SupersededByApprovalRecordId = supersededBy.TryGetValue(x.ApprovalRecordId, out string next) ? next : null }).ToList() };
        }

        private static void DetectCycles(Dictionary<string, EngineeringDistanceApprovalRecord> byId, Dictionary<string, string> supersededBy)
        {
            var visiting = new HashSet<string>(StringComparer.Ordinal); var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in byId.Keys) DetectCycle(id, supersededBy, visiting, visited);
        }
        private static void DetectCycle(string id, Dictionary<string, string> supersededBy, HashSet<string> visiting, HashSet<string> visited)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw new EngineeringDistanceApprovalHistoryValidationException("supersession cycle detected at ApprovalRecordId: " + id);
            if (supersededBy.TryGetValue(id, out string next)) DetectCycle(next, supersededBy, visiting, visited);
            visiting.Remove(id); visited.Add(id);
        }
    }
}

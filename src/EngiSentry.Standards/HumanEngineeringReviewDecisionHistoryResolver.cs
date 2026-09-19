using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class HumanEngineeringReviewDecisionHistoryValidationException : Exception
    {
        public HumanEngineeringReviewDecisionHistoryValidationException(string message) : base(message) { }
    }

    public static class HumanEngineeringReviewDecisionHistoryResolver
    {
        public static HumanEngineeringReviewDecisionHistoryResolution Resolve(IEnumerable<HumanEngineeringReviewDecisionRecord> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            List<HumanEngineeringReviewDecisionRecord> history = records.ToList();
            var byId = new Dictionary<string, HumanEngineeringReviewDecisionRecord>(StringComparer.Ordinal);
            foreach (HumanEngineeringReviewDecisionRecord record in history)
            {
                if (record == null) throw new HumanEngineeringReviewDecisionHistoryValidationException("human engineering review history contains a null decision");
                HumanEngineeringReviewDecisionValidator.Validate(record);
                if (byId.ContainsKey(record.DecisionRecordId)) throw new HumanEngineeringReviewDecisionHistoryValidationException("duplicate human engineering review DecisionRecordId: " + record.DecisionRecordId);
                byId.Add(record.DecisionRecordId, record);
            }

            var supersededBy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (HumanEngineeringReviewDecisionRecord record in history)
            {
                string? supersedesId = record.SupersedesDecisionRecordId;
                if (string.IsNullOrEmpty(supersedesId)) continue;
                if (!byId.TryGetValue(supersedesId!, out HumanEngineeringReviewDecisionRecord target)) throw new HumanEngineeringReviewDecisionHistoryValidationException("supersession target was not found: " + supersedesId);
                if (target.DrawingName != record.DrawingName || target.RuleId != record.RuleId) throw new HumanEngineeringReviewDecisionHistoryValidationException("supersession target has a different DrawingName or RuleId: " + record.DecisionRecordId);
                if (supersededBy.ContainsKey(target.DecisionRecordId)) throw new HumanEngineeringReviewDecisionHistoryValidationException("ambiguous supersession branch for DecisionRecordId: " + target.DecisionRecordId);
                supersededBy.Add(target.DecisionRecordId, record.DecisionRecordId);
            }

            DetectCycles(byId, supersededBy);
            foreach (IGrouping<string, HumanEngineeringReviewDecisionRecord> group in history.GroupBy(record => record.DrawingName + "\u001f" + record.RuleId, StringComparer.Ordinal))
            {
                if (group.Count(record => !supersededBy.ContainsKey(record.DecisionRecordId)) != 1) throw new HumanEngineeringReviewDecisionHistoryValidationException("history group must contain exactly one active head: " + group.Key);
            }

            return new HumanEngineeringReviewDecisionHistoryResolution
            {
                Entries = history.OrderBy(record => record.DrawingName, StringComparer.Ordinal).ThenBy(record => record.RuleId, StringComparer.Ordinal).ThenBy(record => record.DecisionRecordId, StringComparer.Ordinal).Select(record => new HumanEngineeringReviewDecisionHistoryEntry
                {
                    DecisionRecordId = record.DecisionRecordId,
                    DrawingName = record.DrawingName,
                    RuleId = record.RuleId,
                    State = supersededBy.ContainsKey(record.DecisionRecordId) ? HumanEngineeringReviewDecisionHistoryStates.Superseded : HumanEngineeringReviewDecisionHistoryStates.Active,
                    SupersededByDecisionRecordId = supersededBy.TryGetValue(record.DecisionRecordId, out string supersedingId) ? supersedingId : null
                }).ToList()
            };
        }

        private static void DetectCycles(Dictionary<string, HumanEngineeringReviewDecisionRecord> byId, Dictionary<string, string> supersededBy)
        {
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in byId.Keys) DetectCycle(id, supersededBy, visiting, visited);
        }

        private static void DetectCycle(string id, Dictionary<string, string> supersededBy, HashSet<string> visiting, HashSet<string> visited)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw new HumanEngineeringReviewDecisionHistoryValidationException("supersession cycle detected at DecisionRecordId: " + id);
            if (supersededBy.TryGetValue(id, out string supersedingId)) DetectCycle(supersedingId, supersededBy, visiting, visited);
            visiting.Remove(id);
            visited.Add(id);
        }
    }
}

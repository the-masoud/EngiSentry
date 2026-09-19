using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public static class HumanEngineeringReviewDecisionArtifactCatalog
    {
        public static List<HumanEngineeringReviewDecisionRecord> Load(string directory, string drawingName)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("decision directory is required", nameof(directory));
            if (string.IsNullOrWhiteSpace(drawingName)) throw new ArgumentException("drawing name is required", nameof(drawingName));
            if (!Directory.Exists(directory)) return new List<HumanEngineeringReviewDecisionRecord>();

            string prefix = SanitizeFileName(drawingName) + ".";
            var records = new List<HumanEngineeringReviewDecisionRecord>();
            foreach (string path in Directory.GetFiles(directory, "*.standards.review.decision.json", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.Ordinal))
            {
                if (!Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal)) continue;
                HumanEngineeringReviewDecisionRecord record = HumanEngineeringReviewDecisionFileStore.Read(path);
                if (record.DrawingName == drawingName) records.Add(record);
            }
            return records.OrderBy(record => record.DrawingName, StringComparer.Ordinal).ThenBy(record => record.RuleId, StringComparer.Ordinal).ThenBy(record => record.DecisionRecordId, StringComparer.Ordinal).ToList();
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            var builder = new System.Text.StringBuilder(name.Length);
            foreach (char character in name) builder.Append(invalid.Contains(character) ? '_' : character);
            return builder.ToString();
        }
    }
}

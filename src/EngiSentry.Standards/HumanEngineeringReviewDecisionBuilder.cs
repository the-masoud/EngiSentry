using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public static class HumanEngineeringReviewDecisionBuilder
    {
        public static HumanEngineeringReviewDecisionRecord Create(
            StandardAuthorityReviewPackage package,
            string reviewPackageSha256,
            string ruleId,
            string humanReviewState,
            string reviewerDisplayName,
            DateTime decisionTimestampUtc,
            string? authenticatedReviewerIdentity = null,
            string? comment = null,
            string? supersedesDecisionRecordId = null)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (!IsSha256(reviewPackageSha256)) throw new ArgumentException("review package SHA256 must be a 64-character hexadecimal SHA256", nameof(reviewPackageSha256));
            if (package.ReviewProjection == null) throw new InvalidOperationException("review package ReviewProjection is required");
            if (string.IsNullOrWhiteSpace(ruleId)) throw new ArgumentException("RuleId is required", nameof(ruleId));

            List<StandardAuthorityReviewItem> matches = package.ReviewProjection.ReviewItems.Where(item => item != null && item.RuleId == ruleId).ToList();
            if (matches.Count != 1) throw new InvalidOperationException("review package must contain exactly one ReviewItem for RuleId: " + ruleId);
            StandardAuthorityReviewItem item = matches[0];
            if (item.Clauses == null || item.Clauses.Count == 0) throw new InvalidOperationException("review item contains no clauses: " + ruleId);

            var bindings = new List<HumanEngineeringReviewEvidenceBinding>();
            foreach (StandardClauseAuthorityResolution clause in item.Clauses)
            {
                if (clause == null || string.IsNullOrWhiteSpace(clause.ClauseId) || string.IsNullOrWhiteSpace(clause.SourceId) || !IsSha256(clause.SourceSha256)) throw new InvalidOperationException("review item contains incomplete clause/source evidence: " + ruleId);
                bindings.Add(new HumanEngineeringReviewEvidenceBinding { ClauseId = clause.ClauseId, SourceId = clause.SourceId, SourceSha256 = clause.SourceSha256! });
            }

            HumanEngineeringReviewDecisionRecord record = new HumanEngineeringReviewDecisionRecord
            {
                SchemaVersion = HumanEngineeringReviewDecisionValidator.SchemaVersion,
                DrawingName = package.DrawingName,
                ScanTimestampUtc = package.ScanTimestampUtc,
                ReviewPackageSha256 = reviewPackageSha256.ToUpperInvariant(),
                ReviewPackageSchemaVersion = package.SchemaVersion,
                RuleId = item.RuleId,
                AuthorityDecisionStateAtReview = item.DecisionState,
                EffectiveEnforcementModeAtReview = item.EffectiveEnforcementMode,
                HumanReviewState = humanReviewState,
                ReviewerDisplayName = reviewerDisplayName,
                AuthenticatedReviewerIdentity = authenticatedReviewerIdentity,
                DecisionTimestampUtc = decisionTimestampUtc,
                Comment = comment,
                SupersedesDecisionRecordId = supersedesDecisionRecordId,
                EvidenceBindings = bindings.OrderBy(binding => binding.ClauseId, StringComparer.Ordinal).ThenBy(binding => binding.SourceId, StringComparer.Ordinal).ThenBy(binding => binding.SourceSha256, StringComparer.Ordinal).ToList()
            };
            record.DecisionRecordId = ComputeDecisionRecordId(record);
            HumanEngineeringReviewDecisionValidator.Validate(record);
            return record;
        }

        public static string ComputeDecisionRecordId(HumanEngineeringReviewDecisionRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            HumanEngineeringReviewDecisionRecord identity = CopyWithoutId(record);
            byte[] bytes = Serialize(identity);
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
        }

        private static HumanEngineeringReviewDecisionRecord CopyWithoutId(HumanEngineeringReviewDecisionRecord source)
        {
            return new HumanEngineeringReviewDecisionRecord
            {
                SchemaVersion = source.SchemaVersion,
                DecisionRecordId = string.Empty,
                DrawingName = source.DrawingName,
                ScanTimestampUtc = source.ScanTimestampUtc,
                ReviewPackageSha256 = source.ReviewPackageSha256,
                ReviewPackageSchemaVersion = source.ReviewPackageSchemaVersion,
                RuleId = source.RuleId,
                AuthorityDecisionStateAtReview = source.AuthorityDecisionStateAtReview,
                EffectiveEnforcementModeAtReview = source.EffectiveEnforcementModeAtReview,
                HumanReviewState = source.HumanReviewState,
                ReviewerDisplayName = source.ReviewerDisplayName,
                AuthenticatedReviewerIdentity = source.AuthenticatedReviewerIdentity,
                DecisionTimestampUtc = source.DecisionTimestampUtc,
                Comment = source.Comment,
                SupersedesDecisionRecordId = source.SupersedesDecisionRecordId,
                EvidenceBindings = (source.EvidenceBindings ?? new List<HumanEngineeringReviewEvidenceBinding>()).OrderBy(binding => binding.ClauseId, StringComparer.Ordinal).ThenBy(binding => binding.SourceId, StringComparer.Ordinal).ThenBy(binding => binding.SourceSha256, StringComparer.Ordinal).Select(binding => new HumanEngineeringReviewEvidenceBinding { ClauseId = binding.ClauseId, SourceId = binding.SourceId, SourceSha256 = binding.SourceSha256 }).ToList()
            };
        }

        private static byte[] Serialize(HumanEngineeringReviewDecisionRecord record)
        {
            var serializer = new DataContractJsonSerializer(typeof(HumanEngineeringReviewDecisionRecord));
            using (var stream = new MemoryStream()) { serializer.WriteObject(stream, record); return stream.ToArray(); }
        }

        private static bool IsSha256(string? value)
        {
            if (value == null || value.Length != 64) return false;
            return value.All(character => (character >= '0' && character <= '9') || (character >= 'A' && character <= 'F') || (character >= 'a' && character <= 'f'));
        }
    }
}

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public static class HumanEngineeringReviewDecisionFileStore
    {
        public static string ComputeSha256(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required", nameof(path));
            // ReadWrite sharing is required so the hash can be taken while AutoCAD still holds the
            // drawing open for editing. The stream remains read-only, so analysis never modifies the DWG.
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        public static string Write(HumanEngineeringReviewDecisionRecord record, string directory)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("directory is required", nameof(directory));
            HumanEngineeringReviewDecisionValidator.Validate(record);
            if (!string.Equals(record.DecisionRecordId, HumanEngineeringReviewDecisionBuilder.ComputeDecisionRecordId(record), StringComparison.Ordinal)) throw new InvalidDataException("human engineering review decision ID does not match its content");
            Directory.CreateDirectory(directory);
            string drawing = SanitizeFileName(record.DrawingName);
            string scan = record.ScanTimestampUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
            string decision = record.DecisionTimestampUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, drawing + "." + scan + "." + decision + "." + record.DecisionRecordId + ".standards.review.decision.json");
            var serializer = new DataContractJsonSerializer(typeof(HumanEngineeringReviewDecisionRecord));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) serializer.WriteObject(stream, record);
            return path;
        }

        public static HumanEngineeringReviewDecisionRecord Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required", nameof(path));
            var serializer = new DataContractJsonSerializer(typeof(HumanEngineeringReviewDecisionRecord));
            HumanEngineeringReviewDecisionRecord record;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) record = (HumanEngineeringReviewDecisionRecord)serializer.ReadObject(stream)!;
            HumanEngineeringReviewDecisionValidator.Validate(record);
            if (!string.Equals(record.DecisionRecordId, HumanEngineeringReviewDecisionBuilder.ComputeDecisionRecordId(record), StringComparison.Ordinal)) throw new InvalidDataException("human engineering review decision ID does not match its content");
            return record;
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = new System.Collections.Generic.HashSet<char>(Path.GetInvalidFileNameChars());
            var builder = new StringBuilder(name.Length);
            foreach (char character in name) builder.Append(invalid.Contains(character) ? '_' : character);
            return builder.ToString();
        }
    }
}

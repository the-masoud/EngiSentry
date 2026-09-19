using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public static class EngineeringDistanceApprovalRecordFileStore
    {
        public static string ComputeSha256(string path) { using (FileStream s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) using (SHA256 h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)).Replace("-", string.Empty); }
        public static string Write(string directory, EngineeringDistanceApprovalRecord record) { EngineeringDistanceApprovalRecordValidator.Validate(record); if (record.ApprovalRecordId != EngineeringDistanceApprovalRecordBuilder.ComputeApprovalRecordId(record)) throw new InvalidDataException("approval record ID does not match its content"); Directory.CreateDirectory(directory); string name = Sanitize(record.CandidateBinding.CandidateId) + "." + record.DecisionTimestampUtc.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) + "." + record.ApprovalRecordId + ".engineering-distance.approval.json"; string path = Path.Combine(directory, name); using (var s = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) new DataContractJsonSerializer(typeof(EngineeringDistanceApprovalRecord)).WriteObject(s, record); return path; }
        public static EngineeringDistanceApprovalRecord Read(string path) { EngineeringDistanceApprovalRecord r; using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) r = (EngineeringDistanceApprovalRecord)new DataContractJsonSerializer(typeof(EngineeringDistanceApprovalRecord)).ReadObject(s)!; EngineeringDistanceApprovalRecordValidator.Validate(r); if (r.CandidateBinding.CandidateDefinitionSha256 != EngineeringDistanceApprovalRecordBuilder.ComputeCandidateDefinitionSha256(r.CandidateBinding) || r.ApprovalRecordId != EngineeringDistanceApprovalRecordBuilder.ComputeApprovalRecordId(r)) throw new InvalidDataException("approval record content hash does not match its content"); return r; }
        private static string Sanitize(string value) { var bad = new System.Collections.Generic.HashSet<char>(Path.GetInvalidFileNameChars()); var b = new StringBuilder(); foreach(char c in value) b.Append(bad.Contains(c)?'_':c); return b.ToString(); }
    }
}

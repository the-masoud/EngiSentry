using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class EngineeringDistanceApprovalArtifactCatalogException : Exception
    {
        public EngineeringDistanceApprovalArtifactCatalogException(string message, Exception? innerException = null) : base(message, innerException) { }
    }

    public static class EngineeringDistanceApprovalArtifactCatalog
    {
        private const string Suffix = ".engineering-distance.approval.json";

        public static IReadOnlyList<EngineeringDistanceApprovalRecord> Load(string directory, string projectDirectory)
        {
            string expectedProject = EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(projectDirectory);
            if (!Directory.Exists(directory)) return new List<EngineeringDistanceApprovalRecord>();
            var result = new List<EngineeringDistanceApprovalRecord>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(directory, "*" + Suffix, SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.Ordinal))
            {
                EngineeringDistanceApprovalRecord record;
                try { record = EngineeringDistanceApprovalRecordFileStore.Read(path); }
                catch (Exception exception) { throw new EngineeringDistanceApprovalArtifactCatalogException("invalid engineering distance approval artifact: " + path, exception); }
                if (!string.Equals(record.ProjectDirectory, expectedProject, StringComparison.Ordinal)) continue;
                if (!ids.Add(record.ApprovalRecordId)) throw new EngineeringDistanceApprovalArtifactCatalogException("duplicate engineering distance approval ApprovalRecordId: " + record.ApprovalRecordId);
                result.Add(record);
            }
            return result.OrderBy(x => x.ProjectDirectory, StringComparer.Ordinal).ThenBy(x => x.CandidateBinding.CandidateId, StringComparer.Ordinal).ThenBy(x => x.ApprovalRecordId, StringComparer.Ordinal).ToList();
        }
    }
}

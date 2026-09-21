using System;
using System.IO;
using System.Runtime.Serialization.Json;
using EngiSentry.Contracts;

namespace EngiSentry.AutoCAD2021.Scanning
{
    internal static class ApprovalPreparationArtifactWriter
    {
        internal static string WriteApprovalRequest(EngineeringDistanceApprovalRequest request, string directory)
        {
            return Write(request, directory, request.CandidateId + "." + request.RequestId + ".engineering-distance.approval-request.json");
        }

        internal static string WritePolicyRequest(EngineeringDistanceAuthorityPolicyRequest request, string directory)
        {
            return Write(request, directory, request.CandidateId + "." + request.RequestId + ".engineering-distance.authority-policy-request.json");
        }

        private static string Write(object value, string directory, string fileName)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileName);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                new DataContractJsonSerializer(value.GetType()).WriteObject(stream, value);
            }
            return path;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public sealed class EngineeringDistanceApprovalAuthorityPolicyArtifactCatalogException : Exception { public EngineeringDistanceApprovalAuthorityPolicyArtifactCatalogException(string message, Exception? innerException = null) : base(message, innerException) { } }
    public sealed class EngineeringDistanceApprovalAuthorityVerifiedPolicyCatalog
    {
        private readonly List<EngineeringDistanceApprovalAuthorityPolicyArtifact> artifacts;
        internal EngineeringDistanceApprovalAuthorityVerifiedPolicyCatalog(IEnumerable<EngineeringDistanceApprovalAuthorityPolicyArtifact> values) { artifacts = values.Select(EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.CopyArtifact).ToList(); }
        public IReadOnlyList<EngineeringDistanceApprovalAuthorityPolicyArtifact> Artifacts => artifacts.Select(EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.CopyArtifact).ToList();
        public IReadOnlyList<EngineeringDistanceApprovalAuthorityPolicyDefinition> Policies => artifacts.Select(x => EngineeringDistanceApprovalAuthorityPolicyArtifactCanonicalizer.Copy(x.Policy!)).ToList();
        public EngineeringDistanceApprovalAuthorityPolicyRegistry CreatePolicyRegistry() => EngineeringDistanceApprovalAuthorityPolicyRegistry.Create(Policies);
    }
    public static class EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog
    {
        public const string Suffix = ".engineering-distance.authority-policy.json";
        public static string CanonicalDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EngiSentry", "authority-policies");
        public static EngineeringDistanceApprovalAuthorityVerifiedPolicyCatalog LoadCanonical(string expectedProjectDirectory, EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trustedIssuers) => Load(CanonicalDirectory, expectedProjectDirectory, trustedIssuers);
        public static EngineeringDistanceApprovalAuthorityVerifiedPolicyCatalog Load(string directory, string expectedProjectDirectory, EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trustedIssuers)
        {
            if (trustedIssuers == null) throw new ArgumentNullException(nameof(trustedIssuers));
            string expected = EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(expectedProjectDirectory);
            if (!Directory.Exists(directory)) return new EngineeringDistanceApprovalAuthorityVerifiedPolicyCatalog(new EngineeringDistanceApprovalAuthorityPolicyArtifact[0]);
            var retained = new List<EngineeringDistanceApprovalAuthorityPolicyArtifact>();
            var artifactIds = new HashSet<string>(StringComparer.Ordinal);
            var policyKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(directory, "*" + Suffix, SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.Ordinal))
            {
                EngineeringDistanceApprovalAuthorityPolicyArtifact artifact;
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) artifact = (EngineeringDistanceApprovalAuthorityPolicyArtifact)new DataContractJsonSerializer(typeof(EngineeringDistanceApprovalAuthorityPolicyArtifact)).ReadObject(stream)!;
                    artifact = EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.Validate(artifact, trustedIssuers);
                }
                catch (Exception exception) { throw new EngineeringDistanceApprovalAuthorityPolicyArtifactCatalogException("invalid engineering distance authority policy artifact: " + path, exception); }
                if (!string.Equals(artifact.Policy!.ProjectDirectory, expected, StringComparison.Ordinal)) continue;
                if (!artifactIds.Add(artifact.PolicyArtifactId)) throw new EngineeringDistanceApprovalAuthorityPolicyArtifactCatalogException("duplicate policy artifact PolicyArtifactId: " + artifact.PolicyArtifactId);
                string policyKey = artifact.Policy.ProjectDirectory + "\u001f" + artifact.Policy.CandidateId + "\u001f" + artifact.Policy.ApprovalRecordId;
                if (!policyKeys.Add(policyKey)) throw new EngineeringDistanceApprovalAuthorityPolicyArtifactCatalogException("duplicate authority policy lookup key");
                retained.Add(artifact);
            }
            return new EngineeringDistanceApprovalAuthorityVerifiedPolicyCatalog(retained.OrderBy(x => x.Policy!.ProjectDirectory, StringComparer.Ordinal).ThenBy(x => x.Policy!.CandidateId, StringComparer.Ordinal).ThenBy(x => x.Policy!.ApprovalRecordId, StringComparer.Ordinal).ThenBy(x => x.PolicyArtifactId, StringComparer.Ordinal));
        }
    }
}

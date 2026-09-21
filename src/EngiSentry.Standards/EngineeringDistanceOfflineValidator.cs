using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using EngiSentry.Contracts;

namespace EngiSentry.Standards
{
    public static class EngineeringDistanceOfflineValidationStatuses
    {
        public const string ValidFormat = "VALID_FORMAT";
        public const string Invalid = "INVALID";
        public const string Untrusted = "UNTRUSTED";
        public const string Stale = "STALE";
        public const string WrongProject = "WRONG_PROJECT";
        public const string WrongCandidate = "WRONG_CANDIDATE";
        public const string WrongBinding = "WRONG_BINDING";
        public const string SignatureInvalid = "SIGNATURE_INVALID";
        public const string ReadyForAuthorizedInstallation = "READY_FOR_AUTHORIZED_INSTALLATION";
    }

    public sealed class EngineeringDistanceOfflineValidationResult
    {
        public string Status { get; set; } = EngineeringDistanceOfflineValidationStatuses.Invalid;
        public List<string> Diagnostics { get; } = new List<string>();
    }

    public static class EngineeringDistanceOfflineValidator
    {
        public static EngineeringDistanceOfflineValidationResult Validate(
            string approvalPath,
            string policyPath,
            string trustStorePath,
            string expectedProjectDirectory,
            string candidateId)
        {
            var result = new EngineeringDistanceOfflineValidationResult();
            var standards = ProjectEngineeringDistanceReferenceCatalog.Create();
            var candidates = EngineeringDistanceCandidateCatalog.Create(standards.ClauseRegistry, EngineeringDistanceCandidateCatalog.BuiltInCandidates).Candidates;
            var candidate = candidates.SingleOrDefault(item => string.Equals(item.CandidateId, candidateId, StringComparison.Ordinal));
            if (candidate == null)
            {
                result.Status = EngineeringDistanceOfflineValidationStatuses.WrongCandidate;
                result.Diagnostics.Add("Candidate is not present in the built-in standards catalog.");
                return result;
            }

            EngineeringDistanceApprovalRecord approval;
            try
            {
                approval = EngineeringDistanceApprovalRecordFileStore.Read(approvalPath);
            }
            catch (Exception exception)
            {
                result.Status = EngineeringDistanceOfflineValidationStatuses.Invalid;
                result.Diagnostics.Add("Approval record could not be read: " + exception.Message);
                return result;
            }

            var expectedProject = Path.GetFullPath(expectedProjectDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(approval.ProjectDirectory, expectedProject, StringComparison.OrdinalIgnoreCase))
            {
                result.Status = EngineeringDistanceOfflineValidationStatuses.WrongProject;
                result.Diagnostics.Add("Approval record project directory does not match the requested project.");
                return result;
            }

            if (!string.Equals(approval.CandidateBinding.CandidateId, candidate.CandidateId, StringComparison.Ordinal) ||
                !string.Equals(approval.CandidateBinding.RuleId, candidate.RuleId, StringComparison.Ordinal))
            {
                result.Status = EngineeringDistanceOfflineValidationStatuses.WrongCandidate;
                result.Diagnostics.Add("Approval record candidate or rule does not match the requested candidate.");
                return result;
            }

            var currentness = EngineeringDistanceApprovalCurrentnessValidator.Evaluate(approval, expectedProject, candidate, standards.ClauseRegistry, standards.SourceRegistry);
            if (currentness.Status != EngineeringDistanceApprovalCurrentnessStates.Current)
            {
                result.Status = EngineeringDistanceOfflineValidationStatuses.Stale;
                result.Diagnostics.AddRange(currentness.Reasons);
                return result;
            }

            EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trustStore;
            try
            {
                trustStore = EngineeringDistanceApprovalAuthorityTrustStore.Load(trustStorePath);
            }
            catch (Exception exception)
            {
                result.Status = EngineeringDistanceOfflineValidationStatuses.Invalid;
                result.Diagnostics.Add("Trust store could not be read: " + exception.Message);
                return result;
            }

            EngineeringDistanceApprovalAuthorityPolicyArtifact policyArtifact;
            try
            {
                using (var stream = File.OpenRead(policyPath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(EngineeringDistanceApprovalAuthorityPolicyArtifact));
                    policyArtifact = (EngineeringDistanceApprovalAuthorityPolicyArtifact)serializer.ReadObject(stream);
                }
            }
            catch (Exception exception)
            {
                result.Status = EngineeringDistanceOfflineValidationStatuses.Invalid;
                result.Diagnostics.Add("Policy artifact could not be read: " + exception.Message);
                return result;
            }

            try
            {
                EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.Validate(policyArtifact, trustStore);
            }
            catch (EngineeringDistanceApprovalAuthorityPolicyArtifactValidationException exception)
            {
                var message = exception.Message ?? string.Empty;
                result.Status = message.IndexOf("not trusted", StringComparison.OrdinalIgnoreCase) >= 0
                    ? EngineeringDistanceOfflineValidationStatuses.Untrusted
                    : message.IndexOf("signature", StringComparison.OrdinalIgnoreCase) >= 0
                        ? EngineeringDistanceOfflineValidationStatuses.SignatureInvalid
                        : EngineeringDistanceOfflineValidationStatuses.Invalid;
                result.Diagnostics.Add(message);
                return result;
            }
            catch (Exception exception)
            {
                result.Status = EngineeringDistanceOfflineValidationStatuses.Invalid;
                result.Diagnostics.Add(exception.Message);
                return result;
            }

            if (policyArtifact.Policy == null ||
                !string.Equals(policyArtifact.Policy.ProjectDirectory, expectedProject, StringComparison.OrdinalIgnoreCase))
            {
                result.Status = EngineeringDistanceOfflineValidationStatuses.WrongProject;
                result.Diagnostics.Add("Policy artifact project directory does not match the requested project.");
                return result;
            }

            if (!string.Equals(policyArtifact.Policy.CandidateId, candidate.CandidateId, StringComparison.Ordinal))
            {
                result.Status = EngineeringDistanceOfflineValidationStatuses.WrongCandidate;
                result.Diagnostics.Add("Policy artifact candidate or rule does not match the requested candidate.");
                return result;
            }

            if (!string.Equals(approval.Decision.DecisionStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                result.Status = EngineeringDistanceOfflineValidationStatuses.ValidFormat;
                result.Diagnostics.Add("Approval record format is valid, but its decision is not APPROVED.");
                return result;
            }

            if (!string.Equals(policyArtifact.Policy.ApprovalRecordId, approval.ApprovalRecordId, StringComparison.Ordinal))
            {
                result.Status = EngineeringDistanceOfflineValidationStatuses.WrongBinding;
                result.Diagnostics.Add("Policy artifact is not bound to the supplied approval record.");
                return result;
            }

            result.Status = EngineeringDistanceOfflineValidationStatuses.ReadyForAuthorizedInstallation;
            result.Diagnostics.Add("Validation passed; external authorized installation remains a separate operation.");
            return result;
        }
    }
}

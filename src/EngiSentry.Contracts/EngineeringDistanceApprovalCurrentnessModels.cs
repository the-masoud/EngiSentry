using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    public static class EngineeringDistanceApprovalCurrentnessStates
    {
        public const string Current = "CURRENT";
        public const string Stale = "STALE";
    }

    public static class EngineeringDistanceApprovalCurrentnessReasons
    {
        public const string ProjectScopeChanged = "PROJECT_SCOPE_CHANGED";
        public const string CandidateMissing = "CANDIDATE_MISSING";
        public const string CandidateDefinitionChanged = "CANDIDATE_DEFINITION_CHANGED";
        public const string EvidenceBindingsChanged = "EVIDENCE_BINDINGS_CHANGED";
    }

    [DataContract]
    public sealed class EngineeringDistanceApprovalCurrentnessResult
    {
        [DataMember(Order = 1)] public string Status { get; set; } = string.Empty;
        [DataMember(Order = 2)] public List<string> Reasons { get; set; } = new List<string>();
    }
}

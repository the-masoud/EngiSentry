using System.Collections.Generic;
using System.Runtime.Serialization;

namespace EngiSentry.Contracts
{
    public static class HumanEngineeringReviewDecisionEvaluationStatuses
    {
        public const string Current = "CURRENT";
        public const string Stale = "STALE";
    }

    public static class HumanEngineeringReviewDecisionEvaluationReasons
    {
        public const string ReviewPackageShaChanged = "REVIEW_PACKAGE_SHA_CHANGED";
        public const string ReviewPackageSchemaChanged = "REVIEW_PACKAGE_SCHEMA_CHANGED";
        public const string DrawingChanged = "DRAWING_CHANGED";
        public const string ScanTimestampChanged = "SCAN_TIMESTAMP_CHANGED";
        public const string RuleMissing = "RULE_MISSING";
        public const string AuthorityDecisionStateChanged = "AUTHORITY_DECISION_STATE_CHANGED";
        public const string EnforcementModeChanged = "ENFORCEMENT_MODE_CHANGED";
        public const string EvidenceBindingsChanged = "EVIDENCE_BINDINGS_CHANGED";
    }

    [DataContract]
    public sealed class HumanEngineeringReviewDecisionEvaluation
    {
        [DataMember(Order = 1)] public string DecisionRecordId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string RuleId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string Status { get; set; } = string.Empty;
        [DataMember(Order = 4)] public List<string> Reasons { get; set; } = new List<string>();
    }
}

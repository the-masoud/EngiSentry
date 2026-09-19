using System;
using System.Linq;
using EngiSentry.Contracts;
namespace EngiSentry.Standards
{
 public static class EngineeringDistanceApprovalAuthorityResolver
 {
  public static EngineeringDistanceApprovalAuthorityResult Resolve(EngineeringDistanceApprovalRecord approval,EngineeringDistanceApprovalHistoryEntry history,EngineeringDistanceApprovalCurrentnessResult currentness,EngineeringDistanceApprovalAuthorityPolicyRegistry policies,StandardClauseRegistry clauses,StandardSourceRegistry sources,StandardAuthorityPolicyRegistry standardPolicies)
  {
   if(approval==null||history==null||currentness==null||policies==null||clauses==null||sources==null||standardPolicies==null)throw new ArgumentNullException(); EngineeringDistanceApprovalRecordValidator.Validate(approval);var r=Base(approval);
   if(history.ApprovalRecordId!=approval.ApprovalRecordId||history.ProjectDirectory!=approval.ProjectDirectory||history.CandidateId!=approval.CandidateBinding.CandidateId||history.State!=EngineeringDistanceApprovalHistoryStates.Active){r.State=EngineeringDistanceApprovalAuthorityStates.InactiveApproval;return r;}
   if(approval.Decision.DecisionStatus!="APPROVED"){r.State=EngineeringDistanceApprovalAuthorityStates.NotApproved;return r;}
   if(currentness.Status!=EngineeringDistanceApprovalCurrentnessStates.Current){r.State=EngineeringDistanceApprovalAuthorityStates.StaleApproval;return r;}
   if(!policies.TryGet(approval.ProjectDirectory,approval.CandidateBinding.CandidateId,approval.ApprovalRecordId,out var policy)||policy==null){r.State=EngineeringDistanceApprovalAuthorityStates.NoAuthorityPolicy;return r;}
   r.PolicyId=policy.PolicyId;r.AuthorityBasisReference=policy.AuthorityBasisReference;
   if(policy.ApprovalReference!=approval.Decision.ApprovalReference){r.State=EngineeringDistanceApprovalAuthorityStates.AuthorityPolicyMismatch;return r;}
   var standard=approval.EvidenceBindings.Select(e=>ResolveEvidence(e,clauses,sources,standardPolicies)).ToList();
   if(standard.Any(x=>x==StandardAuthorityClasses.Quarantined)){r.State=EngineeringDistanceApprovalAuthorityStates.SourceQuarantined;return r;}
   if(standard.Any(x=>x==StandardAuthorityClasses.LegacyConflict)&&policy.LegacyConflictDisposition==EngineeringDistanceApprovalLegacyConflictDispositions.Unresolved){r.State=EngineeringDistanceApprovalAuthorityStates.LegacyConflictUnresolved;return r;}
   r.AuthorityClass=StandardAuthorityClasses.GoverningProject;r.EffectiveEnforcementMode=policy.EnforcementMode;r.State=policy.EnforcementMode==StandardEnforcementModes.Deterministic?EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic:EngineeringDistanceApprovalAuthorityStates.GoverningProjectConditional;return r;
  }
  private static string ResolveEvidence(EngineeringDistanceApprovalEvidenceBinding e,StandardClauseRegistry clauses,StandardSourceRegistry sources,StandardAuthorityPolicyRegistry policies){if(!clauses.TryGet(e.ClauseId,out var clause)||clause==null||clause.SourceId!=e.SourceId||!sources.TryGet(e.SourceId,out var source)||source==null||source.SourceSha256!=e.SourceSha256||!policies.TryResolve(e.SourceId,e.ClauseId,out var policy)||policy==null)throw new InvalidOperationException("approval authority evidence is not current/policy-resolved: "+e.ClauseId);return policy.AuthorityClass;}
  private static EngineeringDistanceApprovalAuthorityResult Base(EngineeringDistanceApprovalRecord a)=>new EngineeringDistanceApprovalAuthorityResult{ProjectDirectory=a.ProjectDirectory,CandidateId=a.CandidateBinding.CandidateId,ApprovalRecordId=a.ApprovalRecordId};
 }
}

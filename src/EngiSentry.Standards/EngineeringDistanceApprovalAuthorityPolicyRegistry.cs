using System;
using System.Collections.Generic;
using System.Linq;
using EngiSentry.Contracts;
namespace EngiSentry.Standards
{
 public sealed class EngineeringDistanceApprovalAuthorityPolicyRegistryValidationException:Exception { public EngineeringDistanceApprovalAuthorityPolicyRegistryValidationException(string message):base(message){} }
 public sealed class EngineeringDistanceApprovalAuthorityPolicyRegistry
 {
  private readonly List<EngineeringDistanceApprovalAuthorityPolicyDefinition> policies;
  private EngineeringDistanceApprovalAuthorityPolicyRegistry(List<EngineeringDistanceApprovalAuthorityPolicyDefinition> policies){this.policies=policies;}
  public IReadOnlyList<EngineeringDistanceApprovalAuthorityPolicyDefinition> Policies=>policies.Select(Copy).ToList();
  public static EngineeringDistanceApprovalAuthorityPolicyRegistry Create(IEnumerable<EngineeringDistanceApprovalAuthorityPolicyDefinition> values)
  {
   if(values==null)throw new ArgumentNullException(nameof(values)); var ids=new HashSet<string>(StringComparer.Ordinal);var keys=new HashSet<string>(StringComparer.Ordinal);var accepted=new List<EngineeringDistanceApprovalAuthorityPolicyDefinition>();
   foreach(var policy in values){if(policy==null)throw new EngineeringDistanceApprovalAuthorityPolicyRegistryValidationException("policy registry contains null policy");var copy=Copy(policy);copy.ProjectDirectory=EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(copy.ProjectDirectory);if(string.IsNullOrWhiteSpace(copy.PolicyId)||!ids.Add(copy.PolicyId))Fail("PolicyId is required and unique");if(string.IsNullOrWhiteSpace(copy.CandidateId)||!EngineeringDistanceApprovalRecordValidator.IsSha(copy.ApprovalRecordId)||string.IsNullOrWhiteSpace(copy.ApprovalReference)||string.IsNullOrWhiteSpace(copy.AuthorityBasisReference)||string.IsNullOrWhiteSpace(copy.Rationale))Fail("policy binding fields are invalid");if(copy.EnforcementMode!=StandardEnforcementModes.Deterministic&&copy.EnforcementMode!=StandardEnforcementModes.ConditionalReview)Fail("policy EnforcementMode is invalid");if(copy.LegacyConflictDisposition!=EngineeringDistanceApprovalLegacyConflictDispositions.Unresolved&&copy.LegacyConflictDisposition!=EngineeringDistanceApprovalLegacyConflictDispositions.ResolvedByProjectDecision)Fail("policy LegacyConflictDisposition is invalid");if(!keys.Add(Key(copy)))Fail("duplicate policy lookup key");accepted.Add(copy);}
   return new EngineeringDistanceApprovalAuthorityPolicyRegistry(accepted.OrderBy(x=>x.ProjectDirectory,StringComparer.Ordinal).ThenBy(x=>x.CandidateId,StringComparer.Ordinal).ThenBy(x=>x.ApprovalRecordId,StringComparer.Ordinal).ThenBy(x=>x.PolicyId,StringComparer.Ordinal).ToList());
  }
  public bool TryGet(string project,string candidate,string approval,out EngineeringDistanceApprovalAuthorityPolicyDefinition? policy){policy=policies.FirstOrDefault(x=>x.ProjectDirectory==project&&x.CandidateId==candidate&&x.ApprovalRecordId==approval);if(policy==null)return false;policy=Copy(policy);return true;}
  private static string Key(EngineeringDistanceApprovalAuthorityPolicyDefinition x)=>x.ProjectDirectory+"\u001f"+x.CandidateId+"\u001f"+x.ApprovalRecordId;
  private static void Fail(string m)=>throw new EngineeringDistanceApprovalAuthorityPolicyRegistryValidationException(m);
  private static EngineeringDistanceApprovalAuthorityPolicyDefinition Copy(EngineeringDistanceApprovalAuthorityPolicyDefinition x)=>new EngineeringDistanceApprovalAuthorityPolicyDefinition{PolicyId=x.PolicyId,ProjectDirectory=x.ProjectDirectory,CandidateId=x.CandidateId,ApprovalRecordId=x.ApprovalRecordId,ApprovalReference=x.ApprovalReference,AuthorityBasisReference=x.AuthorityBasisReference,EnforcementMode=x.EnforcementMode,LegacyConflictDisposition=x.LegacyConflictDisposition,Rationale=x.Rationale};
 }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using EngiSentry.Contracts;
using EngiSentry.PlotPlan;
using EngiSentry.Rules;
using EngiSentry.Standards;
using EngiSentry.AutoCAD2021;
using EngiSentry.AutoCAD2021.Scanning;

namespace EngiSentry.Tests
{
    /// <summary>
    /// P11.1 production INTAKE acceptance for the signed Standard Authority Policy. The one-time audit established
    /// that NO existing signed/trusted artifact can establish a <see cref="StandardAuthorityPolicyDefinition"/>
    /// explicitly: the approval authority-policy artifact (<see cref="EngineeringDistanceApprovalAuthorityPolicyArtifact"/>)
    /// carries no SourceId, no ClauseId and no AuthorityClass at all (it is approval-record authority for a distance
    /// candidate, not standards authority over a source/clause), the trusted-issuer store proves only signer trust,
    /// and a <see cref="ProjectRuleAuthorityBindingArtifact"/>'s SourceId/PolicyId/AuthorityClass fields are
    /// provenance REFERENCES the chain must already have resolved — building a policy from them would infer the very
    /// mapping that must stay explicit. So the minimum signed standards-authority policy artifact was added, reusing
    /// the existing trusted-issuer / RSA_PKCS1_SHA256 infrastructure verbatim, and the runtime context now merges
    /// verified external policies into the SAME authority-policy registry the resolver and the execution gate already
    /// read from. There is no second authority path.
    ///
    /// These tests prove the intake contract end to end against the REAL built-in authority chain: the deterministic
    /// clause, source and gate identities below are the real registered ones, so an external signed policy can only
    /// ever produce a gate by entering the normal registry → resolver → execution-gate path. Every fixture is
    /// synthetic, isolated under a disposable temp directory and signed by a TEST_ONLY throwaway RSA key registered in
    /// a TEST_ONLY trust store. No production catalog, source, clause, approval, authority, hash or distance is
    /// created or altered; the machine-wide standard-authority-policies directory is never written to; no DWG is
    /// touched.
    /// </summary>
    internal static class StandardAuthorityPolicyIntakeTests
    {
        private const string InsertionUnits = "Meters";
        private const double MetersPerDrawingUnit = 1.0d;
        private const string PlacementId = "placement:equipment-1";
        private const string EquipmentEntityId = "equipment-1";
        private const string NormalizedTag = "T-1001";
        private const string RoadHandle = "road:geometry";
        private const double EquipmentEastingMeters = 100d;

        // ---- The REAL built-in authority chain identities an external policy may lawfully target -------------
        // PCD-PD-135 Rev.1 clause 3.3.9(a) is resolved by the real authority rule "pcd.general". It has NO built-in
        // clause-level policy (only the source-wide CONDITIONAL_REVIEW baseline), so an explicit signed clause
        // DETERMINISTIC policy is exactly the legitimate way to obtain a deterministic gate for it.
        private const string RealSourceId = "trc.pcd-pd-135.rev1";
        private const string RealClauseId = "trc.pcd-pd-135.3.3.9.a";
        private const string RealAuthorityRuleId = "runtime.plotplan.authority.pcd.general";
        private const string QuarantinedClauseId = "trc.pcd-pd-135.3.4.13.b";
        private const string QuarantinedAuthorityRuleId = "runtime.plotplan.authority.pcd.flare";
        private const string LegacyAuthorityRuleId = "runtime.plotplan.authority.epr.legacy";

        private const string TestPolicyId = "test.standard-authority.pcd.3.3.9.a";

        /// <summary>The approval record id the TEST_ONLY binding records, so the approval-authority evidence
        /// checks have something to compare against.</summary>
        private const string BindingApprovalRecordId = "1111111111111111111111111111111111111111111111111111111111111111";

        private const string TestIssuer = "issuer.standard-policy.test-only";
        private const string TestKey = "key.standard-policy.test-only";
        private static readonly DateTime TestIssuedAt = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

        internal static void Run()
        {
            NoExternalPolicyYieldsZeroDeterministicGates();          // 1
            UnsignedTamperedUntrustedIsRejected();                    // 2
            TamperedArtifactIsRejected();                             // 3
            UnknownIssuerOrBadSignatureIsRejected();                  // 4
            WrongProjectScopeIsInactive();                            // 5
            InvalidSourceOrClauseIsRejected();                        // 6
            RevokedPolicyIsInactive();                                // 7
            DuplicateOrConflictingPoliciesFailClosed();               // 8
            NeverAutoEnforceStaysNonExecutable();                     // 9
            QuarantinedAuthorityStaysNonExecutable();                 // 10
            LegacyConflictStaysNonExecutable();                       // 11
            GovernedValueWithoutPolicyStaysNonExecutable();           // 12
            ValidSignedPolicyYieldsExactlyOneDeterministicGate();     // 13
            GateWithoutBindingYieldsZeroExecutableRules();            // 14
            ValidBindingToGateYieldsExactlyOneExecutableRule();       // 15
            MismatchedBindingYieldsZeroExecutableRules();             // 16
            StaleOrUntrustedEvidenceYieldsZeroExecutableRules();      // 17
            SyntheticGovernedCompositionYieldsOneProposal();          // 18
            SameFixtureWithoutPolicyYieldsZeroProposals();            // 19
            SameFixtureWithoutBindingYieldsZeroProposals();           // 20
            CurrentProductionInstallationRemainsZero();               // 21
            Console.WriteLine("P11.1 standard authority policy production intake assertions passed: " + _assertions);
        }

        // ---- 1. No external authority policy -> 0 deterministic gates -----------------------------------------

        private static void NoExternalPolicyYieldsZeroDeterministicGates()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            try
            {
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);

                    // 1a. A directory that does not exist at all is the ordinary production state: empty, not an error.
                    PlotPlanStandardsAuthorityRuntimeContext missing = Context(Path.Combine(root, "never-created"), project, trusted);
                    Assert(missing.ExecutionGate.Rules.Count > 0, "The real authority gate has no rules.");
                    Assert(DeterministicGates(missing) == 0, "A missing policy directory produced a deterministic gate.");

                    // 1b. An existing but EMPTY directory is identical: fail-closed at zero deterministic gates.
                    string empty = Path.Combine(root, "empty"); Directory.CreateDirectory(empty);
                    PlotPlanStandardsAuthorityRuntimeContext emptyContext = Context(empty, project, trusted);
                    Assert(DeterministicGates(emptyContext) == 0, "An empty policy directory produced a deterministic gate.");

                    // 1c. Files that do not match the intake suffix are ignored (not treated as policy material).
                    string foreign = Path.Combine(root, "foreign"); Directory.CreateDirectory(foreign);
                    File.WriteAllText(Path.Combine(foreign, "notes.txt"), "not a policy");
                    File.WriteAllText(Path.Combine(foreign, "other.json"), "{}");
                    Assert(DeterministicGates(Context(foreign, project, trusted)) == 0, "A non-policy file produced a deterministic gate.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 2. Unsigned / wrong-signature -> rejected --------------------------------------------------------

        private static void UnsignedTamperedUntrustedIsRejected()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string store = Path.Combine(root, "store"); Directory.CreateDirectory(store);
            try
            {
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);

                    // Unsigned: the signature field carries no real signature.
                    StandardAuthorityPolicyArtifact unsignedArtifact = SignedPolicy(project, signer);
                    unsignedArtifact.SignatureBase64 = string.Empty;
                    AssertLoadFailsClosed(store, project, trusted, unsignedArtifact, "An unsigned policy produced a deterministic gate.");

                    // A well-formed base64 signature that does not verify against the payload.
                    StandardAuthorityPolicyArtifact wrongSignature = SignedPolicy(project, signer);
                    wrongSignature.SignatureBase64 = Convert.ToBase64String(new byte[256]);
                    AssertLoadFailsClosed(store, project, trusted, wrongSignature, "A wrong-signature policy produced a deterministic gate.");

                    // Wrong schema version.
                    StandardAuthorityPolicyArtifact badSchema = SignedPolicy(project, signer);
                    badSchema.SchemaVersion = "0.9";
                    AssertLoadFailsClosed(store, project, trusted, badSchema, "A wrong-schema policy produced a deterministic gate.");

                    // Content id that does not match the signed payload.
                    StandardAuthorityPolicyArtifact badId = SignedPolicy(project, signer);
                    badId.PolicyArtifactId = "0".PadLeft(64, '0');
                    AssertLoadFailsClosed(store, project, trusted, badId, "A mismatched content id produced a deterministic gate.");

                    // Malformed signature encoding.
                    StandardAuthorityPolicyArtifact malformedSignature = SignedPolicy(project, signer);
                    malformedSignature.SignatureBase64 = "!!!not-base64!!!";
                    AssertLoadFailsClosed(store, project, trusted, malformedSignature, "A malformed signature produced a deterministic gate.");

                    // An unsupported signature algorithm.
                    StandardAuthorityPolicyArtifact badAlgorithm = SignedPolicy(project, signer);
                    badAlgorithm.SignatureAlgorithm = "RSA_PSS_SHA512";
                    AssertLoadFailsClosed(store, project, trusted, badAlgorithm, "An unsupported signature algorithm produced a deterministic gate.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 3. Tampered content -> rejected ------------------------------------------------------------------

        private static void TamperedArtifactIsRejected()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string store = Path.Combine(root, "store"); Directory.CreateDirectory(store);
            try
            {
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);

                    // Tampered enforcement mode: altering the signed policy after signing invalidates the signature.
                    StandardAuthorityPolicyArtifact tamperedMode = Deserialize(Serialize(SignedPolicy(project, signer)));
                    tamperedMode.Policy!.EnforcementMode = StandardEnforcementModes.NeverAutoEnforce;
                    AssertLoadFailsClosed(store, project, trusted, tamperedMode, "A tampered enforcement mode produced a deterministic gate.");

                    // Tampered authority class.
                    StandardAuthorityPolicyArtifact tamperedClass = Deserialize(Serialize(SignedPolicy(project, signer)));
                    tamperedClass.Policy!.AuthorityClass = StandardAuthorityClasses.Quarantined;
                    AssertLoadFailsClosed(store, project, trusted, tamperedClass, "A tampered authority class produced a deterministic gate.");

                    // Tampered clause identity.
                    StandardAuthorityPolicyArtifact tamperedClause = Deserialize(Serialize(SignedPolicy(project, signer)));
                    tamperedClause.Policy!.ClauseId = QuarantinedClauseId;
                    AssertLoadFailsClosed(store, project, trusted, tamperedClause, "A tampered clause identity produced a deterministic gate.");

                    // Tampered project scope: retargeting the signed ProjectDirectory breaks the signature.
                    StandardAuthorityPolicyArtifact retargeted = Deserialize(Serialize(SignedPolicy(project, signer)));
                    retargeted.ProjectDirectory = Normalize(Path.Combine(root, "project-b"));
                    AssertLoadFailsClosed(store, project, trusted, retargeted, "A retargeted policy produced a deterministic gate.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 4. Unknown issuer / key or invalid signature -> rejected -------------------------------------------

        private static void UnknownIssuerOrBadSignatureIsRejected()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string store = Path.Combine(root, "store"); Directory.CreateDirectory(store);
            try
            {
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);

                    // Untrusted signer: a valid signature by a key the trust store does not know.
                    using (RSA stranger = RSA.Create(2048))
                    {
                        AssertLoadFailsClosed(store, project, trusted, SignedPolicy(project, stranger), "An untrusted-signer policy produced a deterministic gate.");
                    }

                    // Unknown key id on a known issuer.
                    StandardAuthorityPolicyArtifact unknownKey = SignedPolicy(project, signer);
                    unknownKey.KeyId = "key.unknown";
                    AssertLoadFailsClosed(store, project, trusted, unknownKey, "An unknown key id produced a deterministic gate.");

                    // Unknown issuer id.
                    StandardAuthorityPolicyArtifact unknownIssuer = SignedPolicy(project, signer);
                    unknownIssuer.IssuerId = "issuer.unknown";
                    AssertLoadFailsClosed(store, project, trusted, unknownIssuer, "An unknown issuer id produced a deterministic gate.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 5. Wrong project scope -> inactive for the current project --------------------------------------------

        private static void WrongProjectScopeIsInactive()
        {
            string root = NewRoot();
            string projectA = ProjectDir(root, "project-a");
            string projectB = ProjectDir(root, "project-b");
            string store = Path.Combine(root, "store"); Directory.CreateDirectory(store);
            try
            {
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);

                    // A perfectly signed, trusted, valid policy scoped to project B.
                    Write(store, "b.json", SignedPolicy(projectB, signer));

                    // ...loaded for project A contributes nothing; project A stays at 0 deterministic gates.
                    PlotPlanStandardsAuthorityRuntimeContext forA = Context(store, projectA, trusted);
                    Assert(DeterministicGates(forA) == 0, "An out-of-scope policy produced a deterministic gate in another project.");

                    // ...loaded for project B it is exactly the one deterministic gate.
                    PlotPlanStandardsAuthorityRuntimeContext forB = Context(store, projectB, trusted);
                    Assert(DeterministicGates(forB) == 1, "An in-scope signed policy did not produce exactly one deterministic gate.");
                    Assert(forB.ExecutionGate.Rules.Single(g => g.AutoExecutionAllowed).RuleId == RealAuthorityRuleId,
                        "The deterministic gate was not the targeted real authority rule.");

                    // Casing is part of the normalized scope: a differently-cased path is the same project.
                    Assert(DeterministicGates(Context(store, projectB.ToLowerInvariant(), trusted)) == 1,
                        "A lower-cased project path failed to load its own upper-normalized policy.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 6. Invalid SourceId / invalid SourceId-ClauseId relationship -> rejected ------------------------------

        private static void InvalidSourceOrClauseIsRejected()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string store = Path.Combine(root, "store"); Directory.CreateDirectory(store);
            try
            {
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);

                    // Unknown SourceId.
                    AssertLoadFailsClosed(store, project, trusted, SignedPolicy(project, signer, sourceId: "source.does.not.exist"),
                        "An unknown SourceId produced a deterministic gate.");

                    // A ClauseId that exists but belongs to a different source.
                    AssertLoadFailsClosed(store, project, trusted, SignedPolicy(project, signer, clauseId: "ips.e-pr-190.1.1"),
                        "A ClauseId from a different source produced a deterministic gate.");

                    // A ClauseId the chain never registered at all.
                    AssertLoadFailsClosed(store, project, trusted, SignedPolicy(project, signer, clauseId: "trc.pcd-pd-135.9.9.9.z"),
                        "An unknown ClauseId produced a deterministic gate.");

                    // A source-wide policy that duplicates the built-in baseline SourceId fails closed: the safe
                    // baseline can never be replaced from outside.
                    AssertLoadFailsClosed(store, project, trusted, SignedPolicy(project, signer, sourceWide: true),
                        "A source-wide external policy overriding the built-in baseline was accepted.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 7. Revoked artifact -> inactive ----------------------------------------------------------------------

        private static void RevokedPolicyIsInactive()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string store = Path.Combine(root, "store"); Directory.CreateDirectory(store);
            try
            {
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);

                    // A signed revocation retires the policy; the audit artifact stays on disk but grants nothing.
                    Write(store, "revoked.json", SignedPolicy(project, signer, revokedAtUtc: TestIssuedAt.AddHours(1)));
                    PlotPlanStandardsAuthorityRuntimeContext revoked = Context(store, project, trusted);
                    Assert(DeterministicGates(revoked) == 0, "A revoked policy produced a deterministic gate.");

                    // The same policy without the revocation produces exactly one.
                    Clear(store);
                    Write(store, "active.json", SignedPolicy(project, signer));
                    Assert(DeterministicGates(Context(store, project, trusted)) == 1, "A non-revoked policy did not produce a deterministic gate.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 8. Duplicate / conflicting active policies -> fail closed ---------------------------------------------

        private static void DuplicateOrConflictingPoliciesFailClosed()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string store = Path.Combine(root, "store"); Directory.CreateDirectory(store);
            try
            {
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);

                    // 8a. Two identical artifacts (same content id) is duplicate material.
                    Write(store, "a1.json", SignedPolicy(project, signer));
                    Write(store, "a2.json", SignedPolicy(project, signer));
                    AssertFailsClosed(() => Context(store, project, trusted),
                        "Duplicate policy material did not fail closed.");

                    // 8b. Two CONFLICTING artifacts for the same clause (different issuers, different rationale):
                    // never pick a winner.
                    Clear(store);
                    Write(store, "c1.json", SignedPolicy(project, signer));
                    using (RSA otherSigner = RSA.Create(2048))
                    {
                        Write(store, "c2.json", SignedPolicy(project, otherSigner, policyId: "test.standard-authority.pcd.3.3.9.a.other"));
                        AssertFailsClosed(() => Context(store, project, trusted),
                            "Conflicting active policy material did not fail closed.");
                    }

                    // 8c. An external attempt to override a built-in QUARANTINED clause with a DETERMINISTIC policy
                    //     fails closed: the quarantine can never be lifted from outside.
                    Clear(store);
                    Write(store, "quarantine.json", SignedPolicy(project, signer, clauseId: QuarantinedClauseId,
                        authorityClass: StandardAuthorityClasses.GoverningProject, enforcementMode: StandardEnforcementModes.Deterministic,
                        policyId: "test.standard-authority.flare.override"));
                    AssertFailsClosed(() => Context(store, project, trusted),
                        "An external policy that lifted the built-in quarantine was accepted.");

                    // 8d. After clearing, exactly one policy loads again.
                    Clear(store);
                    Write(store, "ok.json", SignedPolicy(project, signer));
                    Assert(DeterministicGates(Context(store, project, trusted)) == 1, "A single valid policy did not load after conflicts were cleared.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 9. NEVER_AUTO_ENFORCE stays non-executable -------------------------------------------------------------

        private static void NeverAutoEnforceStaysNonExecutable()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string store = Path.Combine(root, "store"); Directory.CreateDirectory(store);
            try
            {
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);

                    // A signed policy that explicitly states NEVER_AUTO_ENFORCE for the targeted clause. Under the
                    // accepted authority/mode pairing that is only lawful for a quarantined or legacy-conflict class.
                    Write(store, "never.json", SignedPolicy(project, signer,
                        authorityClass: StandardAuthorityClasses.Quarantined, enforcementMode: StandardEnforcementModes.NeverAutoEnforce,
                        policyId: "test.standard-authority.pcd.3.3.9.a.never"));
                    PlotPlanStandardsAuthorityRuntimeContext never = Context(store, project, trusted);
                    Assert(DeterministicGates(never) == 0, "A NEVER_AUTO_ENFORCE policy produced a deterministic gate.");
                    StandardRuleExecutionGateDecision? gate = never.ExecutionGate.Rules.FirstOrDefault(x => x.RuleId == RealAuthorityRuleId);
                    Assert(gate != null && !gate.AutoExecutionAllowed, "A NEVER_AUTO_ENFORCE gate allowed auto-execution.");
                    Assert(gate!.EffectiveEnforcementMode == StandardEnforcementModes.NeverAutoEnforce, "The gate lost its NEVER_AUTO_ENFORCE mode.");
                    Assert(gate.DecisionState == StandardDecisionStates.SourceQuarantined, "A NEVER_AUTO_ENFORCE gate lost its decision state.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 10. QUARANTINED authority stays non-executable -----------------------------------------------------------

        private static void QuarantinedAuthorityStaysNonExecutable()
        {
            // The built-in baseline already quarantines the flare clauses; that authority can never execute.
            PlotPlanStandardsAuthorityRuntimeContext real = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create();
            StandardRuleExecutionGateDecision? flare = real.ExecutionGate.Rules.FirstOrDefault(x => x.RuleId == QuarantinedAuthorityRuleId);
            Assert(flare != null, "The real authority gate has no flare rule.");
            Assert(!flare!.AutoExecutionAllowed, "A QUARANTINED authority gate allowed auto-execution.");
            Assert(flare.EffectiveEnforcementMode == StandardEnforcementModes.NeverAutoEnforce, "A QUARANTINED authority lost its NEVER_AUTO_ENFORCE mode.");

            // A signed external QUARANTINED policy for another clause likewise stays non-executable.
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string store = Path.Combine(root, "store"); Directory.CreateDirectory(store);
            try
            {
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);
                    Write(store, "quarantined.json", SignedPolicy(project, signer,
                        authorityClass: StandardAuthorityClasses.Quarantined, enforcementMode: StandardEnforcementModes.NeverAutoEnforce,
                        policyId: "test.standard-authority.pcd.3.3.9.a.quarantined"));
                    PlotPlanStandardsAuthorityRuntimeContext quarantined = Context(store, project, trusted);
                    Assert(DeterministicGates(quarantined) == 0, "A QUARANTINED external policy produced a deterministic gate.");
                    StandardRuleExecutionGateDecision? gate = quarantined.ExecutionGate.Rules.FirstOrDefault(x => x.RuleId == RealAuthorityRuleId);
                    Assert(gate != null && !gate!.AutoExecutionAllowed, "A QUARANTINED external policy gate allowed auto-execution.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 11. LEGACY_CONFLICT authority stays non-executable -------------------------------------------------------

        private static void LegacyConflictStaysNonExecutable()
        {
            // The built-in baseline marks IPS E-PR-190 as LEGACY_CONFLICT / NEVER_AUTO_ENFORCE.
            PlotPlanStandardsAuthorityRuntimeContext real = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create();
            StandardRuleExecutionGateDecision? legacy = real.ExecutionGate.Rules.FirstOrDefault(x => x.RuleId == LegacyAuthorityRuleId);
            Assert(legacy != null, "The real authority gate has no legacy rule.");
            Assert(!legacy!.AutoExecutionAllowed, "A LEGACY_CONFLICT authority gate allowed auto-execution.");
            Assert(legacy.EffectiveEnforcementMode == StandardEnforcementModes.NeverAutoEnforce, "A LEGACY_CONFLICT authority lost its NEVER_AUTO_ENFORCE mode.");
            Assert(legacy.DecisionState == StandardDecisionStates.ExpertReviewRequired, "A LEGACY_CONFLICT gate lost its review-required decision state.");
        }

        // ---- 12. A governed-compatible distance value without an authority policy -> non-executable -------------------

        private static void GovernedValueWithoutPolicyStaysNonExecutable()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string store = Path.Combine(root, "store"); Directory.CreateDirectory(store);
            try
            {
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);
                    PlotPlanStandardsAuthorityRuntimeContext context = Context(store, project, trusted);

                    // A governed resolution carrying an APPROVED minimum distance for the road rule.
                    ProjectDistanceGovernanceAggregateResult governance = GovernanceWithApprovedValue(project);
                    ProjectDistanceCriteria criteria = TestCriteria(project);

                    List<StandardsPackRule> projected = PlacementProposalGovernedRuleProjection.Build(criteria, governance, context, null).ToList();
                    Assert(projected.Count > 0, "The projection produced no rules.");

                    StandardsPackRule? road = projected.FirstOrDefault(x => x.RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId);
                    Assert(road != null, "The road rule was not projected.");
                    // The approved value is carried, but it is NOT authority: the rule stays non-executable.
                    Assert(road!.MinimumDistanceMeters == 20m, "The approved governed value was not carried onto the rule.");
                    Assert(!road.AutoExecutionAllowed, "A governed-compatible value without an authority policy became executable.");
                    Assert(projected.All(x => !x.AutoExecutionAllowed), "A governed-compatible value produced an executable rule without authority.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 13. Valid isolated TEST_ONLY signed DETERMINISTIC policy -> exactly 1 deterministic gate ------------------

        private static void ValidSignedPolicyYieldsExactlyOneDeterministicGate()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string store = Path.Combine(root, "store"); Directory.CreateDirectory(store);
            try
            {
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);
                    Write(store, "valid.json", SignedPolicy(project, signer));

                    PlotPlanStandardsAuthorityRuntimeContext context = Context(store, project, trusted);
                    Assert(DeterministicGates(context) == 1, "A valid signed policy did not yield exactly one deterministic gate.");
                    StandardRuleExecutionGateDecision gate = context.ExecutionGate.Rules.Single(x => x.AutoExecutionAllowed);
                    Assert(gate.RuleId == RealAuthorityRuleId, "The deterministic gate was not the targeted real authority rule.");
                    Assert(gate.EffectiveEnforcementMode == StandardEnforcementModes.Deterministic, "The gate lost its DETERMINISTIC mode.");
                    Assert(string.IsNullOrEmpty(gate.DecisionState), "A deterministic gate carried a non-clean decision state.");

                    // The resolved clause evidence carries the external policy identity verbatim.
                    StandardRuleAuthorityResolution authority = context.AuthorityResolution.Rules.Single(x => x.RuleId == RealAuthorityRuleId);
                    StandardClauseAuthorityResolution clause = authority.Clauses.Single();
                    Assert(clause.ClauseId == RealClauseId, "The resolved clause identity was lost.");
                    Assert(clause.SourceId == RealSourceId, "The resolved source identity was lost.");
                    Assert(clause.PolicyId == TestPolicyId, "The resolved policy identity was not the external policy.");
                    Assert(clause.AuthorityClass == StandardAuthorityClasses.GoverningProject, "The resolved authority class was lost.");
                    Assert(clause.EnforcementMode == StandardEnforcementModes.Deterministic, "The resolved enforcement mode was lost.");

                    // Every other gate stays non-executable.
                    Assert(context.ExecutionGate.Rules.Count(x => !x.AutoExecutionAllowed) == context.ExecutionGate.Rules.Count - 1,
                        "More than one gate became executable.");

                    // The loader returns exactly that one verified policy.
                    StandardAuthorityPolicyVerifiedCatalog loaded = StandardAuthorityPolicyArtifactCatalog.Load(store, project, trusted,
                        context.SourceRegistry, context.ClauseRegistry);
                    Assert(loaded.Artifacts.Count == 1, "Exactly one policy artifact should have loaded, got " + loaded.Artifacts.Count);
                    Assert(loaded.Policies.Count == 1 && loaded.Policies[0].PolicyId == TestPolicyId, "The loaded policy identity was lost.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 14. A deterministic gate without a binding -> 0 executable project rules ----------------------------------

        private static void GateWithoutBindingYieldsZeroExecutableRules()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string store = Path.Combine(root, "store"); Directory.CreateDirectory(store);
            try
            {
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);
                    Write(store, "valid.json", SignedPolicy(project, signer));
                    PlotPlanStandardsAuthorityRuntimeContext context = Context(store, project, trusted);
                    Assert(DeterministicGates(context) == 1, "The fixture did not produce a deterministic gate.");

                    // With no binding installed, the deterministic gate reaches no project rule.
                    Assert(ExecutableProjectRules(context, project, null) == 0,
                        "A deterministic gate without a binding left a project rule executable.");

                    // An EMPTY binding directory is the same fail-closed state.
                    string emptyBindings = Path.Combine(root, "empty-bindings"); Directory.CreateDirectory(emptyBindings);
                    IReadOnlyList<ProjectRuleAuthorityBinding> none = ProjectRuleAuthorityBindingCatalog.Load(emptyBindings, project, trusted);
                    Assert(none.Count == 0, "An empty binding directory produced bindings.");
                    Assert(ExecutableProjectRules(context, project, none) == 0, "An empty binding directory left a project rule executable.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 15. A valid signed TEST_ONLY binding to that gate -> exactly 1 executable project rule ----------------------

        private static void ValidBindingToGateYieldsExactlyOneExecutableRule()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string policyStore = Path.Combine(root, "policies"); Directory.CreateDirectory(policyStore);
            string bindingStore = Path.Combine(root, "bindings"); Directory.CreateDirectory(bindingStore);
            try
            {
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);
                    Write(policyStore, "valid.json", SignedPolicy(project, signer));
                    PlotPlanStandardsAuthorityRuntimeContext context = Context(policyStore, project, trusted);

                    WriteBinding(bindingStore, "road.json", SignedBinding(context, project, signer), signer);
                    IReadOnlyList<ProjectRuleAuthorityBinding> loaded = ProjectRuleAuthorityBindingCatalog.Load(bindingStore, project, trusted);
                    Assert(loaded.Count == 1, "A valid signed binding was not loaded.");

                    List<StandardsPackRule> projected = PlacementProposalGovernedRuleProjection.Build(
                        TestCriteria(project), EmptyGovernance(project), context, loaded).ToList();
                    Assert(projected.Count(x => x.AutoExecutionAllowed) == 1, "A valid binding to a deterministic gate did not yield exactly one executable rule.");
                    Assert(projected.Single(x => x.AutoExecutionAllowed).RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
                        "The executable rule was not the governed road rule.");
                    Assert(projected.Count(x => !x.AutoExecutionAllowed) == projected.Count - 1, "More than one project rule became executable.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 16. A mismatched binding -> 0 executable project rules --------------------------------------------------------

        private static void MismatchedBindingYieldsZeroExecutableRules()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string policyStore = Path.Combine(root, "policies"); Directory.CreateDirectory(policyStore);
            string bindingStore = Path.Combine(root, "bindings"); Directory.CreateDirectory(bindingStore);
            try
            {
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);
                    Write(policyStore, "valid.json", SignedPolicy(project, signer));
                    PlotPlanStandardsAuthorityRuntimeContext context = Context(policyStore, project, trusted);

                    // Signature + trust + scope are all genuine, but the binding points at authority evidence the
                    // chain did not resolve that way. Each variant is authentic material that fail-closes at the
                    // projection.
                    string realSourceSha = RegisteredSourceSha256(context);
                    foreach (ProjectRuleAuthorityBinding mismatched in new[]
                    {
                        WithBinding(ValidBinding(context, project), authorityRuleId: "authority.does.not.exist"),
                        WithBinding(ValidBinding(context, project), authorityClass: StandardAuthorityClasses.ActivePrimary),
                        WithBinding(ValidBinding(context, project), sourceSha256: "B".PadLeft(64, 'B')),
                        WithBinding(ValidBinding(context, project), sourceSha256: realSourceSha, sourceId: "ips.e-pr-190.rev1.2009"),
                        WithBinding(ValidBinding(context, project), policyId: "some.other.policy"),
                        WithBinding(ValidBinding(context, project), clauseIds: new List<string> { QuarantinedClauseId })
                    })
                    {
                        Clear(bindingStore);
                        WriteBinding(bindingStore, "m.json", SignedBinding(context, project, signer, overrideBinding: mismatched), signer);
                        IReadOnlyList<ProjectRuleAuthorityBinding> loaded = ProjectRuleAuthorityBindingCatalog.Load(bindingStore, project, trusted);
                        Assert(loaded.Count == 1, "A signed mismatched binding was not loaded as authentic material.");
                        Assert(ExecutableProjectRules(context, project, loaded) == 0, "A mismatched binding left a project rule executable.");
                    }

                    // A binding to a REAL authority rule that is not deterministic grants nothing either.
                    Clear(bindingStore);
                    ProjectRuleAuthorityBinding legacyBinding = WithBinding(ValidBinding(context, project),
                        authorityRuleId: LegacyAuthorityRuleId, clauseIds: new List<string> { "ips.e-pr-190.1.1" },
                        policyId: "real.ips-e-pr-190.rev1.2009.default", sourceId: "ips.e-pr-190.rev1.2009",
                        authorityClass: StandardAuthorityClasses.LegacyConflict, enforcementMode: StandardEnforcementModes.NeverAutoEnforce);
                    WriteBinding(bindingStore, "legacy.json", SignedBinding(context, project, signer, overrideBinding: legacyBinding), signer);
                    IReadOnlyList<ProjectRuleAuthorityBinding> legacyLoaded = ProjectRuleAuthorityBindingCatalog.Load(bindingStore, project, trusted);
                    Assert(legacyLoaded.Count == 1, "A signed binding to a real legacy authority rule was not loaded as authentic material.");
                    Assert(ExecutableProjectRules(context, project, legacyLoaded) == 0, "A binding to a LEGACY_CONFLICT authority left a project rule executable.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 17. Stale / untrusted approval evidence -> 0 executable project rules -------------------------------------------

        private static void StaleOrUntrustedEvidenceYieldsZeroExecutableRules()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string policyStore = Path.Combine(root, "policies"); Directory.CreateDirectory(policyStore);
            string bindingStore = Path.Combine(root, "bindings"); Directory.CreateDirectory(bindingStore);
            try
            {
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);
                    Write(policyStore, "valid.json", SignedPolicy(project, signer));
                    PlotPlanStandardsAuthorityRuntimeContext context = Context(policyStore, project, trusted);
                    WriteBinding(bindingStore, "road.json", SignedBinding(context, project, signer), signer);
                    IReadOnlyList<ProjectRuleAuthorityBinding> loaded = ProjectRuleAuthorityBindingCatalog.Load(bindingStore, project, trusted);
                    Assert(ExecutableProjectRules(context, project, loaded) == 1, "The fixture did not yield one executable rule.");

                    ProjectDistanceCriteria criteria = TestCriteria(project);

                    // 17a. A STALE approval revokes the grant even for authentic, deterministic material.
                    var stale = new Dictionary<string, EngineeringDistanceApprovalCurrentnessResult>(StringComparer.Ordinal)
                    {
                        { ProjectDistanceRuleBinder.EquipmentToRoadRuleId, new EngineeringDistanceApprovalCurrentnessResult { Status = EngineeringDistanceApprovalCurrentnessStates.Stale, Reasons = new List<string> { EngineeringDistanceApprovalCurrentnessReasons.CandidateDefinitionChanged } } }
                    };
                    Assert(StandardsPackProjectionBuilder.Build(context, ProjectDistanceRuleBinder.Create(criteria), null, stale, null, null, loaded)
                        .Count(x => x.AutoExecutionAllowed) == 0, "A stale approval left a deterministic binding executable.");

                    // 17b. An approval authority verdict that is NOT deterministic blocks execution.
                    var notApproved = new Dictionary<string, EngineeringDistanceApprovalAuthorityResult>(StringComparer.Ordinal)
                    {
                        { ProjectDistanceRuleBinder.EquipmentToRoadRuleId, new EngineeringDistanceApprovalAuthorityResult { State = EngineeringDistanceApprovalAuthorityStates.NotApproved } }
                    };
                    Assert(StandardsPackProjectionBuilder.Build(context, ProjectDistanceRuleBinder.Create(criteria), null, null, notApproved, null, loaded)
                        .Count(x => x.AutoExecutionAllowed) == 0, "A non-deterministic approval authority left a binding executable.");

                    // 17c. A mismatched approval record id blocks execution.
                    var mismatchedRecord = new Dictionary<string, EngineeringDistanceApprovalAuthorityResult>(StringComparer.Ordinal)
                    {
                        { ProjectDistanceRuleBinder.EquipmentToRoadRuleId, new EngineeringDistanceApprovalAuthorityResult { State = EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic, ApprovalRecordId = "0".PadLeft(64, '0') } }
                    };
                    Assert(StandardsPackProjectionBuilder.Build(context, ProjectDistanceRuleBinder.Create(criteria), null, null, mismatchedRecord, null, loaded)
                        .Count(x => x.AutoExecutionAllowed) == 0, "A mismatched approval record id left a binding executable.");

                    // 17d. A matching approval record id with a CURRENT approval keeps the grant.
                    var matchedRecord = new Dictionary<string, EngineeringDistanceApprovalAuthorityResult>(StringComparer.Ordinal)
                    {
                        { ProjectDistanceRuleBinder.EquipmentToRoadRuleId, new EngineeringDistanceApprovalAuthorityResult { State = EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic, ApprovalRecordId = BindingApprovalRecordId } }
                    };
                    Assert(StandardsPackProjectionBuilder.Build(context, ProjectDistanceRuleBinder.Create(criteria), null, null, matchedRecord, null, loaded)
                        .Count(x => x.AutoExecutionAllowed) == 1, "A matching deterministic approval record wrongly blocked a valid binding.");

                    // 17e. A CURRENT approval keeps the grant.
                    var current = new Dictionary<string, EngineeringDistanceApprovalCurrentnessResult>(StringComparer.Ordinal)
                    {
                        { ProjectDistanceRuleBinder.EquipmentToRoadRuleId, new EngineeringDistanceApprovalCurrentnessResult { Status = EngineeringDistanceApprovalCurrentnessStates.Current } }
                    };
                    Assert(StandardsPackProjectionBuilder.Build(context, ProjectDistanceRuleBinder.Create(criteria), null, current, null, null, loaded)
                        .Count(x => x.AutoExecutionAllowed) == 1, "A CURRENT approval wrongly revoked a valid deterministic binding.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 18. Complete synthetic governed composition -> ExecutableViolatedCount = 1, ProposalCount = 1 --------------------

        private static void SyntheticGovernedCompositionYieldsOneProposal()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string approvalDirectory = Path.Combine(project, "qa", "plotplan"); Directory.CreateDirectory(approvalDirectory);
            string policyStore = Path.Combine(root, "policies"); Directory.CreateDirectory(policyStore);
            string bindingStore = Path.Combine(root, "bindings"); Directory.CreateDirectory(bindingStore);
            try
            {
                ProjectDistanceCriteria criteria = TestCriteria(project, roadMinimumMeters: 15m);
                PlotPlanModel plotPlan = PlotPlanWithOneViolatedRoadPlacement(observedMeters: 10d);
                ProjectDistanceGovernanceAggregateResult governance = ProjectDistanceGovernanceRuntimeLoader.LoadCanonical(criteria, approvalDirectory);

                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);
                    Write(policyStore, "policy.json", SignedPolicy(project, signer));
                    PlotPlanStandardsAuthorityRuntimeContext context = Context(policyStore, project, trusted);
                    WriteBinding(bindingStore, "road.json", SignedBinding(context, project, signer), signer);
                    IReadOnlyList<ProjectRuleAuthorityBinding> installed = ProjectRuleAuthorityBindingCatalog.Load(bindingStore, project, trusted);

                    PlacementProposalRunResult result = PlacementProposalRuntime.Build(
                        plotPlan, PlacementProposalGovernedRuleProjection.Build(criteria, governance, context, installed), governance, out PlacementProposalRunDiagnostics diagnostics);

                    Assert(diagnostics.ExecutableViolatedCount == 1, "The synthetic governed case did not count one executable violation.");
                    Assert(diagnostics.ProposalCount == 1, "The synthetic governed case did not produce exactly one proposal.");
                    Assert(result.Proposals.Count == 1, "The synthetic governed case emitted " + result.Proposals.Count + " proposals.");
                    Assert(result.Proposals[0].ViolatedRule != null && result.Proposals[0].ViolatedRule!.RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
                        "The proposal did not carry the governed road rule identity.");
                    Assert(result.Proposals[0].RequiredDistanceMeters == 15m, "The proposal lost the 15 m governed criterion.");
                    Assert(result.Proposals[0].EngineeringReviewRequired, "A generated proposal stopped requiring engineering review.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 19. Same fixture with the authority policy removed -> ProposalCount = 0 --------------------------------------------

        private static void SameFixtureWithoutPolicyYieldsZeroProposals()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string approvalDirectory = Path.Combine(project, "qa", "plotplan"); Directory.CreateDirectory(approvalDirectory);
            string policyStore = Path.Combine(root, "policies"); Directory.CreateDirectory(policyStore);
            string bindingStore = Path.Combine(root, "bindings"); Directory.CreateDirectory(bindingStore);
            try
            {
                ProjectDistanceCriteria criteria = TestCriteria(project, roadMinimumMeters: 15m);
                PlotPlanModel plotPlan = PlotPlanWithOneViolatedRoadPlacement(observedMeters: 10d);
                ProjectDistanceGovernanceAggregateResult governance = ProjectDistanceGovernanceRuntimeLoader.LoadCanonical(criteria, approvalDirectory);

                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);
                    // The binding is installed and valid, but NO authority policy is present: the gate is not
                    // deterministic, so the binding validates against nothing and nothing executes.
                    WriteBinding(bindingStore, "road.json", SignedBindingWithoutPolicyContext(project, signer), signer);
                    PlotPlanStandardsAuthorityRuntimeContext context = Context(policyStore, project, trusted);
                    IReadOnlyList<ProjectRuleAuthorityBinding> installed = ProjectRuleAuthorityBindingCatalog.Load(bindingStore, project, trusted);

                    PlacementProposalRunResult result = PlacementProposalRuntime.Build(
                        plotPlan, PlacementProposalGovernedRuleProjection.Build(criteria, governance, context, installed), governance, out PlacementProposalRunDiagnostics diagnostics);

                    Assert(diagnostics.ProposalCount == 0, "Removing the authority policy still produced a proposal.");
                    Assert(result.Proposals.Count == 0, "Removing the authority policy still emitted a proposal.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 20. Same fixture with the binding removed -> ProposalCount = 0 ----------------------------------------------

        private static void SameFixtureWithoutBindingYieldsZeroProposals()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string approvalDirectory = Path.Combine(project, "qa", "plotplan"); Directory.CreateDirectory(approvalDirectory);
            string policyStore = Path.Combine(root, "policies"); Directory.CreateDirectory(policyStore);
            try
            {
                ProjectDistanceCriteria criteria = TestCriteria(project, roadMinimumMeters: 15m);
                PlotPlanModel plotPlan = PlotPlanWithOneViolatedRoadPlacement(observedMeters: 10d);
                ProjectDistanceGovernanceAggregateResult governance = ProjectDistanceGovernanceRuntimeLoader.LoadCanonical(criteria, approvalDirectory);

                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);
                    // The deterministic authority policy is present, but NO binding connects it to the project rule.
                    Write(policyStore, "policy.json", SignedPolicy(project, signer));
                    PlotPlanStandardsAuthorityRuntimeContext context = Context(policyStore, project, trusted);

                    PlacementProposalRunResult result = PlacementProposalRuntime.Build(
                        plotPlan, PlacementProposalGovernedRuleProjection.Build(criteria, governance, context, null), governance, out PlacementProposalRunDiagnostics diagnostics);

                    Assert(diagnostics.ProposalCount == 0, "Removing the binding still produced a proposal.");
                    Assert(result.Proposals.Count == 0, "Removing the binding still emitted a proposal.");
                    Assert(DeterministicGates(context) == 1, "The fixture lost its deterministic gate.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 21. The current real production installation remains 0 gates / 0 executable rules -----------------------------

        private static void CurrentProductionInstallationRemainsZero()
        {
            // 21a. The built-in-only baseline grants nothing today.
            PlotPlanStandardsAuthorityRuntimeContext real = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create();
            Assert(real.ExecutionGate.Rules.Count > 0, "The real authority gate has no rules.");
            Assert(DeterministicGates(real) == 0, "The built-in-only baseline produced a deterministic gate.");

            // 21b. The real production intake path: production trust store + the machine-wide standard authority
            //     policy directory. Whatever is installed, only verified material can survive; rejected material
            //     fails closed (no context, therefore 0 gates).
            EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = EngineeringDistanceApprovalAuthorityTrustStore.LoadCanonical();
            string project = Path.Combine(Path.GetTempPath(), "EngiSentry-PolicyIntakeProduction-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(project);
            try
            {
                PlotPlanStandardsAuthorityRuntimeContext production;
                try { production = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create(project, trusted); }
                catch (StandardAuthorityPolicyArtifactCatalogException) { return; } // unverifiable material => fail closed
                Assert(DeterministicGates(production) == 0, "The current production installation produced a deterministic gate.");

                ProjectDistanceCriteria criteria = TestCriteria(project);
                IReadOnlyList<ProjectRuleAuthorityBinding> bindings;
                try { bindings = ProjectRuleAuthorityBindingCatalog.LoadCanonical(Path.Combine(Path.GetTempPath(), "EngiSentry-PolicyIntakeBindings-" + Guid.NewGuid().ToString("N")), trusted); }
                catch (ProjectRuleAuthorityBindingCatalogException) { bindings = new List<ProjectRuleAuthorityBinding>(); } // unverifiable => fail closed

                List<StandardsPackRule> projected = PlacementProposalGovernedRuleProjection.Build(
                    criteria, EmptyGovernance(project), production, bindings).ToList();
                Assert(projected.Count > 0, "The production projection produced no rules.");
                Assert(projected.All(x => !x.AutoExecutionAllowed), "The current production installation yielded an auto-executable rule.");
                Assert(projected.All(x => x.Clauses.Count == 0), "The production projection synthesized clause evidence.");
            }
            finally { try { Directory.Delete(project, true); } catch (IOException) { } }

            // 21c. The production policy directory location is the documented program-data root.
            Assert(StandardAuthorityPolicyArtifactCatalog.CanonicalDirectory.IndexOf("EngiSentry", StringComparison.OrdinalIgnoreCase) >= 0,
                "The production standard authority policy directory left the EngiSentry program-data root.");
            Assert(StandardAuthorityPolicyArtifactCatalog.CanonicalDirectory.EndsWith("standard-authority-policies", StringComparison.OrdinalIgnoreCase),
                "The production standard authority policy directory changed its canonical name.");
        }

        // ---- intake / signing helpers (TEST_ONLY) ---------------------------------------------------------------------------

        private static string NewRoot() => Path.Combine(Path.GetTempPath(), "EngiSentry-PolicyIntakeTests-" + Guid.NewGuid().ToString("N"));
        private static void DeleteRoot(string root) { if (Directory.Exists(root)) { try { Directory.Delete(root, true); } catch (IOException) { } } }
        private static string ProjectDir(string root, string name) { string p = Path.Combine(root, name); Directory.CreateDirectory(p); return p; }
        private static string Normalize(string projectDirectory) => EngineeringDistanceApprovalRecordValidator.NormalizeProjectDirectory(projectDirectory);
        private static void Clear(string directory) { foreach (string f in Directory.GetFiles(directory)) File.Delete(f); }

        private static EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry TrustedRegistry(RSA signer)
        {
            RSAParameters p = signer.ExportParameters(false);
            return EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.Create(new[]
            {
                new EngineeringDistanceApprovalAuthorityTrustedKeyDefinition
                {
                    IssuerId = TestIssuer,
                    KeyId = TestKey,
                    SignatureAlgorithm = EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.SignatureAlgorithm,
                    RsaModulusBase64 = Convert.ToBase64String(p.Modulus!),
                    RsaExponentBase64 = Convert.ToBase64String(p.Exponent!)
                }
            });
        }

        /// <summary>The real production composition with an explicit (temp) external policy directory, so the
        /// real built-in baseline + verified external policies flow through the real registry, resolver and gate.</summary>
        private static PlotPlanStandardsAuthorityRuntimeContext Context(string policyDirectory, string projectDirectory, EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted)
            => PlotPlanStandardsAuthorityRuntimeContextCatalog.Create(projectDirectory, trusted, policyDirectory);

        private static int DeterministicGates(PlotPlanStandardsAuthorityRuntimeContext context)
            => context.ExecutionGate.Rules.Count(x => x.AutoExecutionAllowed);

        /// <summary>Builds a genuine signed standards-authority policy artifact for the given project, targeting a
        /// real registered source/clause by default. The returned artifact is authentic material for the TEST_ONLY
        /// trust store built from the same key. Pass <paramref name="sourceWide"/> to state a source-wide policy
        /// (no ClauseId) instead.</summary>
        private static StandardAuthorityPolicyArtifact SignedPolicy(string projectDirectory, RSA signer,
            string? sourceId = null, string? clauseId = null, string? authorityClass = null, string? enforcementMode = null,
            string? policyId = null, DateTime? revokedAtUtc = null, bool sourceWide = false)
        {
            var artifact = new StandardAuthorityPolicyArtifact
            {
                SchemaVersion = StandardAuthorityPolicyArtifactValidator.SchemaVersion,
                IssuerId = TestIssuer,
                KeyId = TestKey,
                IssuedAtUtc = new DateTime(TestIssuedAt.Ticks - TestIssuedAt.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc),
                SignatureAlgorithm = EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.SignatureAlgorithm,
                ProjectDirectory = Normalize(projectDirectory),
                RevokedAtUtc = revokedAtUtc.HasValue ? new DateTime(revokedAtUtc.Value.Ticks - revokedAtUtc.Value.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc) : (DateTime?)null,
                Policy = new StandardAuthorityPolicyDefinition
                {
                    PolicyId = policyId ?? TestPolicyId,
                    SourceId = sourceId ?? RealSourceId,
                    ClauseId = sourceWide ? null : (clauseId ?? RealClauseId),
                    AuthorityClass = authorityClass ?? StandardAuthorityClasses.GoverningProject,
                    EnforcementMode = enforcementMode ?? StandardEnforcementModes.Deterministic,
                    Rationale = "TEST_ONLY: signed standards-authority policy intake acceptance."
                }
            };
            artifact.PolicyArtifactId = StandardAuthorityPolicyArtifactCanonicalizer.ComputePolicyArtifactId(artifact);
            artifact.SignatureBase64 = Convert.ToBase64String(signer.SignData(StandardAuthorityPolicyArtifactCanonicalizer.CreateUnsignedPayloadBytes(artifact), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            return artifact;
        }

        private static byte[] Serialize(StandardAuthorityPolicyArtifact artifact) { using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(StandardAuthorityPolicyArtifact)).WriteObject(stream, artifact); return stream.ToArray(); } }
        private static StandardAuthorityPolicyArtifact Deserialize(byte[] bytes) { using (var stream = new MemoryStream(bytes)) { return (StandardAuthorityPolicyArtifact)new DataContractJsonSerializer(typeof(StandardAuthorityPolicyArtifact)).ReadObject(stream)!; } }
        private static void Write(string directory, string fileName, StandardAuthorityPolicyArtifact artifact)
        {
            // The loader only recognizes the canonical intake suffix, so every fixture is written under it.
            string path = Path.Combine(directory, Path.GetFileNameWithoutExtension(fileName) + StandardAuthorityPolicyArtifactCatalog.Suffix);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)) new DataContractJsonSerializer(typeof(StandardAuthorityPolicyArtifact)).WriteObject(stream, artifact);
        }

        // ---- bindings: a signed TEST_ONLY binding whose evidence matches the deterministic gate exactly ---------------------

        private static ProjectRuleAuthorityBinding ValidBinding(PlotPlanStandardsAuthorityRuntimeContext context, string projectDirectory)
        {
            return new ProjectRuleAuthorityBinding
            {
                ProjectRuleId = ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
                AuthorityRuleId = RealAuthorityRuleId,
                ClauseIds = new List<string> { RealClauseId },
                PolicyId = TestPolicyId,
                SourceId = RealSourceId,
                SourceSha256 = RegisteredSourceSha256(context),
                AuthorityClass = StandardAuthorityClasses.GoverningProject,
                EffectiveEnforcementMode = StandardEnforcementModes.Deterministic,
                ApprovalRecordId = BindingApprovalRecordId,
                Rationale = "TEST_ONLY: signed binding to the deterministic authority gate.",
                ProjectDirectory = Normalize(projectDirectory)
            };
        }

        // Used where no policy (hence no resolved evidence) exists yet; the recorded evidence does not matter
        // because the gate is not deterministic there.
        private static ProjectRuleAuthorityBindingArtifact SignedBindingWithoutPolicyContext(string projectDirectory, RSA signer)
            => SignedBinding(PlotPlanStandardsAuthorityRuntimeContextCatalog.Create(), projectDirectory, signer, overrideBinding: null);

        private static string RegisteredSourceSha256(PlotPlanStandardsAuthorityRuntimeContext context)
        {
            return context.SourceRegistry.Sources.First(x => x.SourceId == RealSourceId).SourceSha256!;
        }

        private static ProjectRuleAuthorityBinding WithBinding(ProjectRuleAuthorityBinding b,
            string? projectRuleId = null, string? authorityRuleId = null, string? authorityClass = null,
            string? enforcementMode = null, string? sourceId = null, string? sourceSha256 = null,
            string? policyId = null, IList<string>? clauseIds = null, string? projectDirectory = null)
        {
            return new ProjectRuleAuthorityBinding
            {
                ProjectRuleId = projectRuleId ?? b.ProjectRuleId,
                AuthorityRuleId = authorityRuleId ?? b.AuthorityRuleId,
                ClauseIds = clauseIds == null ? b.ClauseIds : new List<string>(clauseIds),
                PolicyId = policyId ?? b.PolicyId,
                SourceId = sourceId ?? b.SourceId,
                SourceSha256 = sourceSha256 ?? b.SourceSha256,
                AuthorityClass = authorityClass ?? b.AuthorityClass,
                EffectiveEnforcementMode = enforcementMode ?? b.EffectiveEnforcementMode,
                ApprovalRecordId = b.ApprovalRecordId,
                AuthorityState = b.AuthorityState,
                Rationale = b.Rationale,
                ProjectDirectory = projectDirectory ?? b.ProjectDirectory
            };
        }

        /// <summary>Signs a binding artifact (optionally overriding the carried binding) with the TEST_ONLY key.</summary>
        private static ProjectRuleAuthorityBindingArtifact SignedBinding(PlotPlanStandardsAuthorityRuntimeContext context, string projectDirectory, RSA signer, ProjectRuleAuthorityBinding? overrideBinding = null)
        {
            ProjectRuleAuthorityBinding binding = overrideBinding ?? ValidBinding(context, projectDirectory);
            var artifact = new ProjectRuleAuthorityBindingArtifact
            {
                SchemaVersion = ProjectRuleAuthorityBindingArtifactValidator.SchemaVersion,
                IssuerId = TestIssuer,
                KeyId = TestKey,
                IssuedAtUtc = new DateTime(TestIssuedAt.Ticks - TestIssuedAt.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc),
                SignatureAlgorithm = EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.SignatureAlgorithm,
                Binding = binding
            };
            artifact.BindingArtifactId = ProjectRuleAuthorityBindingArtifactCanonicalizer.ComputeBindingArtifactId(artifact);
            artifact.SignatureBase64 = Convert.ToBase64String(signer.SignData(ProjectRuleAuthorityBindingArtifactCanonicalizer.CreateUnsignedPayloadBytes(artifact), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            return artifact;
        }

        private static void WriteBinding(string directory, string fileName, ProjectRuleAuthorityBindingArtifact artifact, RSA signer)
        {
            string path = Path.Combine(directory, Path.GetFileNameWithoutExtension(fileName) + ProjectRuleAuthorityBindingCatalog.Suffix);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)) new DataContractJsonSerializer(typeof(ProjectRuleAuthorityBindingArtifact)).WriteObject(stream, artifact);
        }

        // ---- projection helper: how many PROJECT rules the loaded bindings make executable ----------------------------

        private static int ExecutableProjectRules(PlotPlanStandardsAuthorityRuntimeContext context, string projectDirectory,
            IReadOnlyList<ProjectRuleAuthorityBinding>? bindings)
        {
            ProjectDistanceCriteria criteria = TestCriteria(projectDirectory);
            return PlacementProposalGovernedRuleProjection.Build(criteria, EmptyGovernance(projectDirectory), context, bindings)
                .Count(x => x.AutoExecutionAllowed && x.RuleId.StartsWith("project.plotplan.", StringComparison.Ordinal));
        }

        private static ProjectDistanceGovernanceAggregateResult EmptyGovernance(string projectDirectory)
            => new ProjectDistanceGovernanceAggregateResult { ProjectDirectory = Normalize(projectDirectory) };

        // ---- a governed aggregate carrying an APPROVED minimum distance (a value, NOT authority) -------------------------

        private static ProjectDistanceGovernanceAggregateResult GovernanceWithApprovedValue(string projectDirectory)
        {
            return new ProjectDistanceGovernanceAggregateResult
            {
                ProjectDirectory = Normalize(projectDirectory),
                Items = new List<ProjectDistanceGovernanceAggregateItem>
                {
                    new ProjectDistanceGovernanceAggregateItem
                    {
                        CandidateId = "test.candidate.road",
                        CandidateRuleId = ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
                        ProjectRuleId = ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
                        Resolution = new ProjectDistanceGovernedRuleResolution
                        {
                            State = ProjectDistanceGovernedRuleResolutionStates.GovernedCompatible,
                            ProjectRuleId = ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
                            CandidateId = "test.candidate.road",
                            AuthorityState = EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic,
                            GovernanceProvenance = new ProjectDistanceGovernanceProvenance
                            {
                                ProjectRuleId = ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
                                CandidateId = "test.candidate.road",
                                CandidateRuleId = ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
                                ApprovalRecordId = "0".PadLeft(64, '0'),
                                PolicyId = TestPolicyId,
                                AuthorityBasisReference = "TEST_ONLY basis",
                                AuthorityState = EngineeringDistanceApprovalAuthorityStates.GoverningProjectDeterministic,
                                ApprovedMinimumDistanceMeters = 20m
                            }
                        }
                    }
                }
            };
        }

        private static ProjectDistanceCriteria TestCriteria(string projectDirectory, decimal roadMinimumMeters = 15m)
        {
            return new ProjectDistanceCriteria
            {
                SchemaVersion = ProjectDistanceCriteriaDefaults.CurrentSchemaVersion,
                ProjectDirectory = projectDirectory,
                AccessRoadEdgeMinimumMeters = roadMinimumMeters,
                SecurityFenceMinimumMeters = 12m,
                UnitBoundaryMinimumMeters = 9m
            };
        }

        // ---- synthetic plot-plan fixture: one executable road-clearance violation -------------------------------------------

        private static PlotPlanModel PlotPlanWithOneViolatedRoadPlacement(double observedMeters)
        {
            PlotPlanModel model = new PlotPlanModel
            {
                Drawing = new SemanticDrawing { FileName = "synthetic-policy-intake.dwg", InsertionUnits = InsertionUnits },
                Geometry = FullGeometry(),
                Measurements = new PlotSpatialMeasurementModel()
            };
            model.Measurements.EquipmentSpatialMeasurements.Add(RoadMeasurement(observedMeters));
            model.EquipmentPlacements.Add(PlacementFor(PlacementId));
            return model;
        }

        private static PlotGeometryModel FullGeometry()
        {
            PlotGeometryModel model = new PlotGeometryModel();
            model.Primitives.Add(HorizontalPrimitive(RoadHandle, 0d, -1000d, 1000d));
            return model;
        }

        private static PlotGeometryPrimitive HorizontalPrimitive(string handle, double northingMeters, double westMeters, double eastMeters)
        {
            return new PlotGeometryPrimitive
            {
                SourceHandle = handle,
                SourceLayer = "Synthetic",
                GeometryKind = "Polyline",
                Closed = false,
                Vertices = new List<PlotGeometryVertex>
                {
                    new PlotGeometryVertex { X = MetersToDrawingUnits(westMeters), Y = MetersToDrawingUnits(northingMeters), Z = 0d },
                    new PlotGeometryVertex { X = MetersToDrawingUnits(eastMeters), Y = MetersToDrawingUnits(northingMeters), Z = 0d }
                }
            };
        }

        private static EquipmentPlacement PlacementFor(string placementId)
        {
            return new EquipmentPlacement
            {
                PlacementId = placementId,
                EquipmentEntityId = EquipmentEntityId,
                TagSourceHandle = "tag:equipment-1",
                PlacementStatus = "Complete",
                EngineeringApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment,
                Easting = new CoordinateAssociation { Axis = "E", Status = "Resolved", SelectedValue = 100m },
                Northing = new CoordinateAssociation { Axis = "N", Status = "Resolved", SelectedValue = 10m }
            };
        }

        private static EquipmentSpatialMeasurement RoadMeasurement(double observedMeters)
        {
            return new EquipmentSpatialMeasurement
            {
                MeasurementId = "measurement:road-1",
                PlacementId = PlacementId,
                EquipmentEntityId = EquipmentEntityId,
                NormalizedTag = NormalizedTag,
                EquipmentEasting = Convert.ToDecimal(MetersToDrawingUnits(EquipmentEastingMeters), CultureInfo.InvariantCulture),
                EquipmentNorthing = Convert.ToDecimal(MetersToDrawingUnits(observedMeters), CultureInfo.InvariantCulture),
                ConstraintId = "bounded-constraint:" + ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
                SubjectKind = "Road",
                LabelSourceHandle = RoadHandle + ":label",
                GeometrySourceHandle = RoadHandle,
                GeometrySourceLayer = "Synthetic",
                GeometryKind = "Polyline",
                ClosestPointX = MetersToDrawingUnits(EquipmentEastingMeters),
                ClosestPointY = MetersToDrawingUnits(0d),
                Distance2D = MetersToDrawingUnits(observedMeters),
                EngineeringMeasurementBasisKey = EngineeringDistanceEvidenceKeys.AccessRoadEdge,
                EngineeringApplicabilityKey = EngineeringDistanceEvidenceKeys.Equipment
            };
        }

        private static double MetersToDrawingUnits(double meters) => meters / MetersPerDrawingUnit;

        // ---- assertions ----------------------------------------------------------------------------------------

        private static void AssertLoadFailsClosed(string store, string project, EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted,
            StandardAuthorityPolicyArtifact artifact, string message)
        {
            Clear(store);
            Write(store, "bad.json", artifact);
            // Unverifiable or conflicting material must be rejected, not silently ignored. A conflict can be
            // detected either by the intake loader (bad/ambiguous artifact) or by the policy registry merge with
            // the built-in baseline; both are fail-closed refusals to produce a context.
            AssertFailsClosed(() => Context(store, project, trusted), message);
            // And the built-in baseline stays fail-closed regardless.
            Assert(DeterministicGates(PlotPlanStandardsAuthorityRuntimeContextCatalog.Create()) == 0, message);
        }

        private static void AssertFailsClosed(Action action, string message)
        {
            try { action(); }
            catch (StandardAuthorityPolicyArtifactCatalogException) { return; }
            catch (StandardAuthorityPolicyRegistryValidationException) { return; }
            Assert(false, message);
        }

        private static void AssertThrows<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            Assert(false, message);
        }

        private static int _assertions;
        private static void Assert(bool condition, string message)
        {
            _assertions++;
            if (!condition) throw new InvalidOperationException("P11.1 STANDARD AUTHORITY POLICY INTAKE ACCEPTANCE FAILED: " + message);
        }
    }
}

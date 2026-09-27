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
    /// P11.1 production INTAKE acceptance for ProjectRuleAuthorityBinding. The audit established that no existing
    /// signed/trusted artifact (approval records, authority-policy artifacts, trusted-issuer store, currentness/
    /// authority verdicts) states BOTH the project rule id and the authority-chain rule id — the project↔authority
    /// mapping is precisely what was missing and must never be inferred from naming similarity. So the minimum signed
    /// binding artifact was added, reusing the existing trusted-issuer / RSA_PKCS1_SHA256 infrastructure verbatim, and
    /// ES_SUGGEST now sources production bindings ONLY from the verified intake loader
    /// (<see cref="ProjectRuleAuthorityBindingCatalog"/>).
    ///
    /// These tests prove the intake contract end to end. Every fixture is synthetic, isolated under a disposable temp
    /// directory and signed by a TEST_ONLY throwaway RSA key registered in a TEST_ONLY trust store. No production
    /// catalog, source, clause, approval, authority, hash or distance is created or altered; no DWG is touched.
    /// </summary>
    internal static class ProjectRuleAuthorityBindingIntakeTests
    {
        private const string InsertionUnits = "Meters";
        private const double MetersPerDrawingUnit = 1.0d;
        private const string PlacementId = "placement:equipment-1";
        private const string EquipmentEntityId = "equipment-1";
        private const string NormalizedTag = "T-1001";
        private const string RoadHandle = "road:geometry";
        private const double EquipmentEastingMeters = 100d;

        // ---- TEST_ONLY deterministic governed authority (composed through the REAL authority chain) ----
        private const string TestSourceId = "test.governed.source.rev1";
        private const string TestClauseId = "test.governed.road.clearance";
        private const string TestPolicyId = "test.governed.policy.road";
        private const string TestAuthorityRuleId = "test.authority.governed.road.minimum-clearance";
        private const string TestSourceSha256 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        private const string TestIssuer = "issuer.intake.test-only";
        private const string TestKey = "key.intake.test-only";
        private static readonly DateTime TestIssuedAt = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

        internal static void Run()
        {
            NoBindingMaterialYieldsZeroExecutable();              // 1
            UnsignedTamperedUntrustedYieldsZero();                // 2
            WrongProjectScopeYieldsZero();                        // 3
            MismatchedAuthorityEvidenceYieldsZero();              // 4
            DuplicateConflictingBindingsFailClosed();             // 5
            StaleOrRevokedAuthorityYieldsZero();                  // 6
            ValidIsolatedTestOnlySignedBindingGrantsExactlyOne(); // 7
            SuggestSyntheticGovernedCaseYieldsOneProposal();      // 8
            CurrentProductionInstallationRemainsZero();           // 9
            Console.WriteLine("P11.1 authority binding production intake assertions passed: " + _assertions);
        }

        // ---- 1. No binding material -> 0 executable ------------------------------------------------------

        private static void NoBindingMaterialYieldsZeroExecutable()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            try
            {
                PlotPlanStandardsAuthorityRuntimeContext context = DeterministicTestAuthorityContext();

                // 1a. A directory that does not exist at all is the ordinary production state: empty, not an error.
                IReadOnlyList<ProjectRuleAuthorityBinding> missing = ProjectRuleAuthorityBindingCatalog.Load(
                    Path.Combine(root, "never-created"), project, TrustedRegistry(out RSA _));
                Assert(missing.Count == 0, "A missing binding directory produced bindings.");
                Assert(ExecutableRules(context, project, missing) == 0, "A missing binding directory left a rule executable.");

                // 1b. An existing but EMPTY directory is identical: fail-closed at zero.
                string empty = Path.Combine(root, "empty"); Directory.CreateDirectory(empty);
                IReadOnlyList<ProjectRuleAuthorityBinding> emptyLoad = ProjectRuleAuthorityBindingCatalog.Load(empty, project, TrustedRegistry(out RSA _));
                Assert(emptyLoad.Count == 0, "An empty binding directory produced bindings.");
                Assert(ExecutableRules(context, project, emptyLoad) == 0, "An empty binding directory left a rule executable.");

                // 1c. Files that do not match the intake suffix are ignored (not treated as binding material).
                string foreign = Path.Combine(root, "foreign"); Directory.CreateDirectory(foreign);
                File.WriteAllText(Path.Combine(foreign, "notes.txt"), "not a binding");
                File.WriteAllText(Path.Combine(foreign, "other.json"), "{}");
                IReadOnlyList<ProjectRuleAuthorityBinding> foreignLoad = ProjectRuleAuthorityBindingCatalog.Load(foreign, project, TrustedRegistry(out RSA _));
                Assert(foreignLoad.Count == 0, "A non-binding file was treated as binding material.");
            }
            finally { DeleteRoot(root); }
        }

        // ---- 2. Unsigned / tampered / untrusted -> 0 --------------------------------------------------------

        private static void UnsignedTamperedUntrustedYieldsZero()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string store = Path.Combine(root, "store"); Directory.CreateDirectory(store);
            try
            {
                PlotPlanStandardsAuthorityRuntimeContext context = DeterministicTestAuthorityContext();

                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);

                    // 2a. Unsigned: the signature field carries no real signature.
                    ProjectRuleAuthorityBindingArtifact unsignedArtifact = Signed(ValidBinding(project), signer);
                    unsignedArtifact.SignatureBase64 = string.Empty;
                    AssertLoadFailsClosed(store, project, trusted, unsignedArtifact, context, "An unsigned binding granted authority.");

                    // 2b. Wrong signature: well-formed base64 that does not verify against the payload.
                    ProjectRuleAuthorityBindingArtifact wrongSignature = Signed(ValidBinding(project), signer);
                    wrongSignature.SignatureBase64 = Convert.ToBase64String(new byte[256]);
                    AssertLoadFailsClosed(store, project, trusted, wrongSignature, context, "A wrong-signature binding granted authority.");

                    // 2c. Tampered content: the binding is altered AFTER signing, so the signature no longer covers it.
                    ProjectRuleAuthorityBindingArtifact tampered = Deserialize(Serialize(Signed(ValidBinding(project), signer)));
                    tampered.Binding!.ProjectRuleId = ProjectDistanceRuleBinder.EquipmentToPlantRuleId;
                    AssertLoadFailsClosed(store, project, trusted, tampered, context, "A tampered binding granted authority.");

                    // 2d. Tampered project scope: retargeting the signed ProjectDirectory breaks the signature.
                    ProjectRuleAuthorityBindingArtifact retargeted = Deserialize(Serialize(Signed(ValidBinding(project), signer)));
                    retargeted.Binding!.ProjectDirectory = Normalize(Path.Combine(root, "project-b"));
                    AssertLoadFailsClosed(store, project, trusted, retargeted, context, "A retargeted binding granted authority.");

                    // 2e. Untrusted signer: a valid signature by a key the trust store does not know.
                    using (RSA stranger = RSA.Create(2048))
                    {
                        ProjectRuleAuthorityBindingArtifact untrusted = Signed(ValidBinding(project), stranger);
                        AssertLoadFailsClosed(store, project, trusted, untrusted, context, "An untrusted-signer binding granted authority.");
                    }

                    // 2f. Wrong schema version.
                    ProjectRuleAuthorityBindingArtifact badSchema = Signed(ValidBinding(project), signer);
                    badSchema.SchemaVersion = "0.9";
                    AssertLoadFailsClosed(store, project, trusted, badSchema, context, "A wrong-schema binding granted authority.");

                    // 2g. Content id that does not match the signed payload.
                    ProjectRuleAuthorityBindingArtifact badId = Signed(ValidBinding(project), signer);
                    badId.BindingArtifactId = "0".PadLeft(64, '0');
                    AssertLoadFailsClosed(store, project, trusted, badId, context, "A mismatched content id granted authority.");

                    // 2h. A binding that does not state both identities cannot be signed into authority.
                    ProjectRuleAuthorityBindingArtifact noAuthorityRule = Signed(With(ValidBinding(project), authorityRuleId: string.Empty), signer);
                    AssertLoadFailsClosed(store, project, trusted, noAuthorityRule, context, "A binding with no AuthorityRuleId granted authority.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 3. Wrong project scope -> 0 ---------------------------------------------------------------------

        private static void WrongProjectScopeYieldsZero()
        {
            string root = NewRoot();
            string projectA = ProjectDir(root, "project-a");
            string projectB = ProjectDir(root, "project-b");
            string store = Path.Combine(root, "store"); Directory.CreateDirectory(store);
            try
            {
                PlotPlanStandardsAuthorityRuntimeContext context = DeterministicTestAuthorityContext();
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);

                    // A perfectly signed, trusted, valid binding scoped to project B.
                    Write(store, "b.json", Signed(ValidBinding(projectB), signer));

                    // ...loaded for project A contributes nothing; project A stays at 0 executable.
                    IReadOnlyList<ProjectRuleAuthorityBinding> forA = ProjectRuleAuthorityBindingCatalog.Load(store, projectA, trusted);
                    Assert(forA.Count == 0, "An out-of-scope binding was loaded for another project.");
                    Assert(ExecutableRules(context, projectA, forA) == 0, "An out-of-scope binding left a rule executable in another project.");

                    // ...loaded for project B it is exactly the one governed binding.
                    IReadOnlyList<ProjectRuleAuthorityBinding> forB = ProjectRuleAuthorityBindingCatalog.Load(store, projectB, trusted);
                    Assert(forB.Count == 1, "An in-scope signed binding was not loaded for its own project.");
                    Assert(ExecutableRules(context, projectB, forB) == 1, "A valid in-scope signed binding did not grant exactly one executable rule.");

                    // Casing is part of the normalized scope: a differently-cased path is the same project.
                    Assert(ProjectRuleAuthorityBindingCatalog.Load(store, projectA.ToLowerInvariant(), trusted).Count == 0,
                        "A lower-cased project path loaded an upper-normalized out-of-scope binding.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 4. Wrong / mismatched authority evidence -> 0 ----------------------------------------------------

        private static void MismatchedAuthorityEvidenceYieldsZero()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string store = Path.Combine(root, "store"); Directory.CreateDirectory(store);
            try
            {
                PlotPlanStandardsAuthorityRuntimeContext context = DeterministicTestAuthorityContext();
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);

                    // Signature + trust + scope are all genuine, but the binding points at an authority rule the
                    // chain never resolved. Intake accepts the authentic artifact; the projection fail-closes.
                    foreach (ProjectRuleAuthorityBinding mismatched in new[]
                    {
                        With(ValidBinding(project), authorityRuleId: "authority.does.not.exist"),
                        With(ValidBinding(project), authorityClass: StandardAuthorityClasses.ActivePrimary),
                        With(ValidBinding(project), sourceSha256: "B".PadLeft(64, 'B')),
                        With(ValidBinding(project), policyId: "some.other.policy"),
                        With(ValidBinding(project), clauseIds: new List<string> { "clause.never.resolved" })
                    })
                    {
                        Clear(store);
                        Write(store, "m.json", Signed(mismatched, signer));
                        IReadOnlyList<ProjectRuleAuthorityBinding> loaded = ProjectRuleAuthorityBindingCatalog.Load(store, project, trusted);
                        Assert(loaded.Count == 1, "A signed mismatched binding was not loaded as authentic material.");
                        Assert(ExecutableRules(context, project, loaded) == 0, "A signed binding with mismatched authority evidence left a rule executable.");
                    }

                    // A binding to a REAL authority rule that is not deterministic grants nothing either.
                    PlotPlanStandardsAuthorityRuntimeContext real = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create();
                    string realAuthorityRule = real.ExecutionGate.Rules.First().RuleId;
                    Clear(store);
                    Write(store, "real.json", Signed(With(ValidBinding(project), authorityRuleId: realAuthorityRule), signer));
                    IReadOnlyList<ProjectRuleAuthorityBinding> realLoaded = ProjectRuleAuthorityBindingCatalog.Load(store, project, trusted);
                    Assert(realLoaded.Count == 1, "A signed binding to a real authority rule was not loaded as authentic material.");
                    Assert(ExecutableRules(real, project, realLoaded) == 0, "A signed binding to a real non-deterministic authority rule left a rule executable.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 5. Duplicate / conflicting bindings -> fail closed ----------------------------------------------

        private static void DuplicateConflictingBindingsFailClosed()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string store = Path.Combine(root, "store"); Directory.CreateDirectory(store);
            try
            {
                PlotPlanStandardsAuthorityRuntimeContext context = DeterministicTestAuthorityContext();
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);

                    // 5a. Two identical artifacts (same content id) for one project rule.
                    Write(store, "a1.json", Signed(ValidBinding(project), signer));
                    Write(store, "a2.json", Signed(ValidBinding(project), signer));
                    AssertThrows<ProjectRuleAuthorityBindingCatalogException>(() => ProjectRuleAuthorityBindingCatalog.Load(store, project, trusted),
                        "Duplicate binding material did not fail closed.");
                    Assert(ExecutableRules(context, project, null) == 0, "Duplicate binding material left a rule executable.");

                    // 5b. Two CONFLICTING artifacts: same project rule, different authority rules. Never pick a winner.
                    Clear(store);
                    Write(store, "c1.json", Signed(ValidBinding(project), signer));
                    Write(store, "c2.json", Signed(With(ValidBinding(project), authorityRuleId: "test.authority.governed.other"), signer));
                    AssertThrows<ProjectRuleAuthorityBindingCatalogException>(() => ProjectRuleAuthorityBindingCatalog.Load(store, project, trusted),
                        "Conflicting binding material did not fail closed.");
                    Assert(ExecutableRules(context, project, null) == 0, "Conflicting binding material left a rule executable.");

                    // 5c. Two distinct valid artifacts for two DIFFERENT project rules coexist without conflict.
                    Clear(store);
                    Write(store, "d1.json", Signed(ValidBinding(project), signer));
                    Write(store, "d2.json", Signed(With(ValidBinding(project), projectRuleId: ProjectDistanceRuleBinder.EquipmentToPlantRuleId,
                        authorityRuleId: TestAuthorityRuleId), signer));
                    IReadOnlyList<ProjectRuleAuthorityBinding> two = ProjectRuleAuthorityBindingCatalog.Load(store, project, trusted);
                    Assert(two.Count == 2, "Two distinct non-conflicting bindings were not loaded.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 6. Stale / revoked authority -> 0 ----------------------------------------------------------------

        private static void StaleOrRevokedAuthorityYieldsZero()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string store = Path.Combine(root, "store"); Directory.CreateDirectory(store);
            try
            {
                PlotPlanStandardsAuthorityRuntimeContext context = DeterministicTestAuthorityContext();
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);

                    // 6a. A signed revocation retires the binding; the audit artifact stays on disk but grants nothing.
                    Write(store, "revoked.json", Signed(ValidBinding(project), signer, revokedAtUtc: TestIssuedAt.AddHours(1)));
                    IReadOnlyList<ProjectRuleAuthorityBinding> revoked = ProjectRuleAuthorityBindingCatalog.Load(store, project, trusted);
                    Assert(revoked.Count == 0, "A revoked binding was loaded.");
                    Assert(ExecutableRules(context, project, revoked) == 0, "A revoked binding left a rule executable.");

                    // 6b. A CURRENT governing approval does not by itself revoke, so a valid signed binding still grants.
                    Clear(store);
                    Write(store, "ok.json", Signed(ValidBinding(project), signer));
                    string currentProject = project;
                    var current = new Dictionary<string, EngineeringDistanceApprovalCurrentnessResult>(StringComparer.Ordinal)
                    {
                        { ProjectDistanceRuleBinder.EquipmentToRoadRuleId, new EngineeringDistanceApprovalCurrentnessResult { Status = EngineeringDistanceApprovalCurrentnessStates.Current } }
                    };
                    IReadOnlyList<ProjectRuleAuthorityBinding> loaded = ProjectRuleAuthorityBindingCatalog.Load(store, currentProject, trusted);
                    Assert(loaded.Count == 1, "A valid non-revoked signed binding was not loaded.");
                    Assert(ExecutableRules(context, currentProject, loaded, currentnessByRuleId: current) == 1,
                        "A CURRENT approval wrongly revoked a valid binding.");

                    // 6c. A STALE governing approval revokes the grant at the projection, even for authentic material.
                    var stale = new Dictionary<string, EngineeringDistanceApprovalCurrentnessResult>(StringComparer.Ordinal)
                    {
                        { ProjectDistanceRuleBinder.EquipmentToRoadRuleId, new EngineeringDistanceApprovalCurrentnessResult { Status = EngineeringDistanceApprovalCurrentnessStates.Stale, Reasons = new List<string> { EngineeringDistanceApprovalCurrentnessReasons.CandidateDefinitionChanged } } }
                    };
                    Assert(ExecutableRules(context, currentProject, loaded, currentnessByRuleId: stale) == 0,
                        "A stale approval left an authentic signed binding executable.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 7. Valid isolated TEST_ONLY signed binding -> exactly 1 executable rule ---------------------------

        private static void ValidIsolatedTestOnlySignedBindingGrantsExactlyOne()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string store = Path.Combine(root, "store"); Directory.CreateDirectory(store);
            try
            {
                PlotPlanStandardsAuthorityRuntimeContext context = DeterministicTestAuthorityContext();
                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);
                    Write(store, "valid.json", Signed(ValidBinding(project), signer));

                    IReadOnlyList<ProjectRuleAuthorityBinding> loaded = ProjectRuleAuthorityBindingCatalog.Load(store, project, trusted);
                    Assert(loaded.Count == 1, "Exactly one signed binding should have loaded, got " + loaded.Count);
                    Assert(loaded[0].ProjectRuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId, "The loaded binding lost its project rule id.");
                    Assert(loaded[0].AuthorityRuleId == TestAuthorityRuleId, "The loaded binding lost its authority rule id.");
                    Assert(loaded[0].ProjectDirectory == Normalize(project), "The loaded binding lost its signed project scope.");

                    // The loaded binding is a verified COPY, not the artifact's instance.
                    Assert(loaded[0] != null, "The loader returned a null binding.");

                    ProjectDistanceCriteria criteria = TestCriteria(project);
                    List<StandardsPackRule> projected = StandardsPackProjectionBuilder.Build(
                        context, ProjectDistanceRuleBinder.Create(criteria), null, null, null, null, loaded).ToList();

                    Assert(projected.Count(x => x.AutoExecutionAllowed) == 1, "A valid signed binding did not yield exactly one executable rule.");
                    Assert(projected.Single(x => x.AutoExecutionAllowed).RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
                        "The executable rule was not the governed road rule.");
                    Assert(projected.All(x => x.AuthorityClass == null || x.RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId),
                        "Authority provenance leaked onto an unbound rule.");

                    // Only the road rule is executable; the sibling rules stay fail-closed.
                    Assert(projected.Count(x => !x.AutoExecutionAllowed) == projected.Count - 1, "More than one rule became executable.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 8. ES_SUGGEST synthetic governed case -> ProposalCount = 1 -----------------------------------------

        private static void SuggestSyntheticGovernedCaseYieldsOneProposal()
        {
            string root = NewRoot();
            string project = ProjectDir(root, "project-a");
            string approvalDirectory = Path.Combine(project, "qa", "plotplan"); Directory.CreateDirectory(approvalDirectory);
            string store = Path.Combine(root, "store"); Directory.CreateDirectory(store);
            try
            {
                ProjectDistanceCriteria criteria = TestCriteria(project, roadMinimumMeters: 15m);
                PlotPlanModel plotPlan = PlotPlanWithOneViolatedRoadPlacement(observedMeters: 10d);
                ProjectDistanceGovernanceAggregateResult governance = ProjectDistanceGovernanceRuntimeLoader.LoadCanonical(criteria, approvalDirectory);
                PlotPlanStandardsAuthorityRuntimeContext context = DeterministicTestAuthorityContext();

                using (RSA signer = RSA.Create(2048))
                {
                    EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = TrustedRegistry(signer);

                    // 8a. No installed binding material -> the exact ES_SUGGEST composition yields 0 proposals.
                    IReadOnlyList<ProjectRuleAuthorityBinding> none = ProjectRuleAuthorityBindingCatalog.Load(store, project, trusted);
                    PlacementProposalRunResult blocked = PlacementProposalRuntime.Build(
                        plotPlan, PlacementProposalGovernedRuleProjection.Build(criteria, governance, context, none), governance, out PlacementProposalRunDiagnostics blockedDiag);
                    Assert(blockedDiag.ProposalCount == 0, "ES_SUGGEST produced proposals with no installed binding material.");

                    // 8b. Install ONE valid TEST_ONLY signed binding for this project.
                    Write(store, "road.json", Signed(ValidBinding(project), signer));
                    IReadOnlyList<ProjectRuleAuthorityBinding> installed = ProjectRuleAuthorityBindingCatalog.Load(store, project, trusted);
                    Assert(installed.Count == 1, "The installed valid binding was not loaded.");

                    // 8c. The same composition now yields exactly one executable proposal.
                    PlacementProposalRunResult granted = PlacementProposalRuntime.Build(
                        plotPlan, PlacementProposalGovernedRuleProjection.Build(criteria, governance, context, installed), governance, out PlacementProposalRunDiagnostics grantedDiag);
                    Assert(grantedDiag.ProposalCount == 1, "The synthetic governed case did not produce exactly one proposal: " + grantedDiag.ProposalCount);
                    Assert(grantedDiag.ExecutableViolatedCount == 1, "The synthetic governed case did not count one executable violation.");
                    Assert(granted.Proposals.Count == 1, "The synthetic governed case emitted " + granted.Proposals.Count + " proposals.");
                    Assert(granted.Proposals[0].ViolatedRule != null && granted.Proposals[0].ViolatedRule.RuleId == ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
                        "The proposal did not carry the governed road rule identity.");
                    Assert(granted.Proposals[0].RequiredDistanceMeters == 15m, "The proposal lost the 15 m governed criterion.");
                    Assert(granted.Proposals[0].EngineeringReviewRequired, "A generated proposal stopped requiring engineering review.");
                }
            }
            finally { DeleteRoot(root); }
        }

        // ---- 9. The current real production installation remains 0 auto-executable -------------------------------

        private static void CurrentProductionInstallationRemainsZero()
        {
            PlotPlanStandardsAuthorityRuntimeContext real = PlotPlanStandardsAuthorityRuntimeContextCatalog.Create();

            // 9a. The real authority gate grants nothing today.
            Assert(real.ExecutionGate.Rules.Count > 0, "The real authority gate has no rules.");
            Assert(real.ExecutionGate.Rules.All(g => !g.AutoExecutionAllowed), "The real authority gate granted auto-execution.");

            // 9b. The real production intake: production trust store + production binding directory. Whatever is
            //     installed, only verified material can survive; rejected material fails closed (0 executable).
            EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted = EngineeringDistanceApprovalAuthorityTrustStore.LoadCanonical();
            IReadOnlyList<ProjectRuleAuthorityBinding> installed;
            try { installed = ProjectRuleAuthorityBindingCatalog.LoadCanonical(Path.Combine(Path.GetTempPath(), "EngiSentry-ProductionScope-" + Guid.NewGuid().ToString("N")), trusted); }
            catch (ProjectRuleAuthorityBindingCatalogException) { installed = new List<ProjectRuleAuthorityBinding>(); } // unverifiable material => fail closed

            ProjectDistanceCriteria criteria = TestCriteria(Path.Combine(Path.GetTempPath(), "EngiSentry-IntakeProduction-" + Guid.NewGuid().ToString("N")));
            ProjectDistanceGovernanceAggregateResult emptyGovernance = new ProjectDistanceGovernanceAggregateResult { ProjectDirectory = criteria.ProjectDirectory };
            List<StandardsPackRule> projected = PlacementProposalGovernedRuleProjection.Build(criteria, emptyGovernance, real, installed).ToList();

            Assert(projected.Count > 0, "The production projection produced no rules.");
            Assert(projected.All(x => !x.AutoExecutionAllowed), "The current production installation yielded an auto-executable rule.");
            Assert(projected.All(x => x.Clauses.Count == 0), "The production projection synthesized clause evidence.");

            // 9c. The production binding directory location is the documented program-data root.
            Assert(ProjectRuleAuthorityBindingCatalog.CanonicalDirectory.IndexOf("EngiSentry", StringComparison.OrdinalIgnoreCase) >= 0,
                "The production binding directory left the EngiSentry program-data root.");
        }

        // ---- intake / signing helpers (TEST_ONLY) -----------------------------------------------------------

        private static string NewRoot() => Path.Combine(Path.GetTempPath(), "EngiSentry-IntakeTests-" + Guid.NewGuid().ToString("N"));
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
        private static EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry TrustedRegistry(out RSA signer) { signer = RSA.Create(2048); return TrustedRegistry(signer); }

        /// <summary>Builds a genuine signed artifact for the given binding. The returned artifact is authentic
        /// material for the TEST_ONLY trust store built from the same key.</summary>
        private static ProjectRuleAuthorityBindingArtifact Signed(ProjectRuleAuthorityBinding binding, RSA signer, DateTime? revokedAtUtc = null)
        {
            var artifact = new ProjectRuleAuthorityBindingArtifact
            {
                SchemaVersion = ProjectRuleAuthorityBindingArtifactValidator.SchemaVersion,
                IssuerId = TestIssuer,
                KeyId = TestKey,
                IssuedAtUtc = new DateTime(TestIssuedAt.Ticks - TestIssuedAt.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc),
                SignatureAlgorithm = EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.SignatureAlgorithm,
                Binding = binding,
                RevokedAtUtc = revokedAtUtc.HasValue ? new DateTime(revokedAtUtc.Value.Ticks - revokedAtUtc.Value.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc) : (DateTime?)null
            };
            artifact.BindingArtifactId = ProjectRuleAuthorityBindingArtifactCanonicalizer.ComputeBindingArtifactId(artifact);
            artifact.SignatureBase64 = Convert.ToBase64String(signer.SignData(ProjectRuleAuthorityBindingArtifactCanonicalizer.CreateUnsignedPayloadBytes(artifact), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            return artifact;
        }

        private static byte[] Serialize(ProjectRuleAuthorityBindingArtifact artifact) { using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(ProjectRuleAuthorityBindingArtifact)).WriteObject(stream, artifact); return stream.ToArray(); } }
        private static ProjectRuleAuthorityBindingArtifact Deserialize(byte[] bytes) { using (var stream = new MemoryStream(bytes)) { return (ProjectRuleAuthorityBindingArtifact)new DataContractJsonSerializer(typeof(ProjectRuleAuthorityBindingArtifact)).ReadObject(stream)!; } }
        private static void Write(string directory, string fileName, ProjectRuleAuthorityBindingArtifact artifact)
        {
            // The loader only recognizes the canonical intake suffix, so every fixture is written under it.
            string path = Path.Combine(directory, Path.GetFileNameWithoutExtension(fileName) + ProjectRuleAuthorityBindingCatalog.Suffix);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)) new DataContractJsonSerializer(typeof(ProjectRuleAuthorityBindingArtifact)).WriteObject(stream, artifact);
        }

        // ---- a binding whose recorded evidence matches the TEST_ONLY deterministic chain exactly, scoped -----

        private static ProjectRuleAuthorityBinding ValidBinding(string projectDirectory) => new ProjectRuleAuthorityBinding
        {
            ProjectRuleId = ProjectDistanceRuleBinder.EquipmentToRoadRuleId,
            AuthorityRuleId = TestAuthorityRuleId,
            ClauseIds = new List<string> { TestClauseId },
            PolicyId = TestPolicyId,
            SourceId = TestSourceId,
            SourceSha256 = TestSourceSha256,
            AuthorityClass = StandardAuthorityClasses.GoverningProject,
            EffectiveEnforcementMode = StandardEnforcementModes.Deterministic,
            Rationale = "TEST_ONLY: signed intake acceptance binding.",
            ProjectDirectory = Normalize(projectDirectory)
        };

        private static ProjectRuleAuthorityBinding With(ProjectRuleAuthorityBinding b,
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

        // ---- projection helper: how many rules the loaded bindings make executable ----------------------------

        private static int ExecutableRules(PlotPlanStandardsAuthorityRuntimeContext context, string projectDirectory,
            IReadOnlyList<ProjectRuleAuthorityBinding>? bindings,
            IReadOnlyDictionary<string, EngineeringDistanceApprovalCurrentnessResult>? currentnessByRuleId = null)
        {
            ProjectDistanceCriteria criteria = TestCriteria(projectDirectory);
            return StandardsPackProjectionBuilder.Build(context, ProjectDistanceRuleBinder.Create(criteria), null, currentnessByRuleId, null, null, bindings)
                .Count(x => x.AutoExecutionAllowed);
        }

        // ---- assertions ----------------------------------------------------------------------------------------

        private static void AssertLoadFailsClosed(string store, string project, EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry trusted,
            ProjectRuleAuthorityBindingArtifact artifact, PlotPlanStandardsAuthorityRuntimeContext context, string message)
        {
            Clear(store);
            Write(store, "bad.json", artifact);
            // Unverifiable material must be rejected, not silently ignored.
            AssertThrows<ProjectRuleAuthorityBindingCatalogException>(() => ProjectRuleAuthorityBindingCatalog.Load(store, project, trusted), message);
            // And the composition stays fail-closed regardless.
            Assert(ExecutableRules(context, project, new List<ProjectRuleAuthorityBinding>()) == 0, message);
        }

        private static void AssertThrows<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            Assert(false, message);
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

        // ---- TEST_ONLY deterministic authority context, composed through the REAL authority chain --------------

        private static PlotPlanStandardsAuthorityRuntimeContext DeterministicTestAuthorityContext()
        {
            StandardSourceDefinition source = new StandardSourceDefinition
            {
                SourceId = TestSourceId,
                DocumentNumber = "TEST-GOV-001",
                Title = "TEST_ONLY governed spacing authority",
                Issuer = "TEST_ONLY intake harness",
                RevisionOrEdition = "1",
                SourceFileName = "test-governed.pdf",
                SourceSha256 = TestSourceSha256
            };
            StandardSourceRegistry sources = StandardSourceRegistry.Create(new[] { source });

            StandardClauseDefinition clause = new StandardClauseDefinition
            {
                ClauseId = TestClauseId,
                SourceId = TestSourceId,
                ClauseReference = "1",
                ClauseText = "TEST_ONLY deterministic minimum equipment-to-road clearance.",
                PageNumber = 1
            };
            StandardClauseRegistry clauses = StandardClauseRegistry.Create(sources, new[] { clause });

            StandardAuthorityPolicyRegistry policies = StandardAuthorityPolicyRegistry.Create(sources, clauses, new[]
            {
                new StandardAuthorityPolicyDefinition
                {
                    PolicyId = TestPolicyId,
                    SourceId = TestSourceId,
                    ClauseId = TestClauseId,
                    AuthorityClass = StandardAuthorityClasses.GoverningProject,
                    EnforcementMode = StandardEnforcementModes.Deterministic,
                    Rationale = "TEST_ONLY: explicit deterministic governing authority used only by the intake harness."
                }
            });

            StandardRulePackDefinition pack = new StandardRulePackDefinition
            {
                PackId = "test.governed.pack",
                Name = "TEST_ONLY governed pack",
                Rules = new List<StandardRuleDefinition>
                {
                    new StandardRuleDefinition { RuleId = TestAuthorityRuleId, Title = "TEST_ONLY governed road clearance", ClauseIds = new List<string> { TestClauseId } }
                }
            };
            StandardRulePackResolution ruleResolution = StandardRulePackResolver.Resolve(
                StandardRulePackRegistry.Create(clauses, new[] { pack }),
                new[] { new StandardRulePackPrecedenceDefinition { PackId = "test.governed.pack", Precedence = 10 } });

            StandardAuthorityResolution authorityResolution = StandardAuthorityResolver.Resolve(ruleResolution, clauses, policies);
            StandardExecutionGateResolution gate = StandardAuthorityExecutionGate.Evaluate(authorityResolution);
            StandardAuthorityReviewProjection review = StandardAuthorityReviewProjector.Create(authorityResolution, sources, clauses);

            StandardRuleExecutionGateDecision? gateDecision = gate.Rules.FirstOrDefault(x => string.Equals(x.RuleId, TestAuthorityRuleId, StringComparison.Ordinal));
            Assert(gateDecision != null && gateDecision.AutoExecutionAllowed && gateDecision.EffectiveEnforcementMode == StandardEnforcementModes.Deterministic,
                "The TEST_ONLY deterministic authority chain did not produce an auto-executable deterministic gate decision.");

            return new PlotPlanStandardsAuthorityRuntimeContext(sources, clauses, ruleResolution, policies, authorityResolution, gate, review);
        }

        // ---- synthetic plot-plan fixture: one executable road-clearance violation -----------------------------

        private static PlotPlanModel PlotPlanWithOneViolatedRoadPlacement(double observedMeters)
        {
            PlotPlanModel model = new PlotPlanModel
            {
                Drawing = new SemanticDrawing { FileName = "synthetic-intake.dwg", InsertionUnits = InsertionUnits },
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

        private static int _assertions;
        private static void Assert(bool condition, string message)
        {
            _assertions++;
            if (!condition) throw new InvalidOperationException("P11.1 INTAKE ACCEPTANCE FAILED: " + message);
        }
    }
}

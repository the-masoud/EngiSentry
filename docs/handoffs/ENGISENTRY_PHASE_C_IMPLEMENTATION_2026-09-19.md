# EngiSentry Phase C Implementation Handoff — 2026-09-19

Governed CHECK path: PlantModel v1 → existing P6/P7/P8/P9 governance → StandardsPackRule →
BoundedConstraint → existing EngineeringDistanceRuleEvaluator semantics → read-only CHECK result.

Scope: Equipment → Road, Equipment → Plant Fence, Equipment → Unit Boundary only.

Approved baseline HEAD: `0c927caaac06e519841867e07a1ca475dd4dacae` (unchanged; no commit, no push).

## 1. Status

**COMPLETE — build PASS, full test harness PASS.** All Phase C partial work present in the worktree at
handoff start was verified, the single compile blocker was fixed, the missing test coverage was added, and
the minimal harness registration was performed. Phase D / SUGGEST was not started.

## 2. Root cause and fix for the initial StringComparer build failure

- **Failure:** `src\EngiSentry.Standards\StandardsPackPackCatalog.cs` lines 38, 39, 43, 58 —
  `error CS0103: The name 'StringComparer' does not exist in the current context` (4 errors, 0 warnings).
- **Root cause:** the file used `StringComparer.Ordinal` but imported only `System.Collections.Generic`
  and `System.Linq`. `StringComparer` lives in `System`. The project sets
  `<DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>`, so no implicit
  `System` import was available — the reference could not resolve.
- **Fix (smallest correct change):** added `using System;` as the first using directive in
  `src\EngiSentry.Standards\StandardsPackPackCatalog.cs`. No logic changed.

Two follow-on defects in Phase C code were fixed in the same pass, both to keep the build clean at
zero warnings (the project enables `<Nullable>enable</Nullable>`):

1. `src\EngiSentry.Standards\StandardsPackPackCatalog.cs` — `warning CS8601 Possible null reference
   assignment`. The `GroupBy` key selector returns `string?` because `StandardsPackRule.PackId` is
   nullable, even though `string.IsNullOrEmpty(x.PackId)` proves the value is non-null past the check.
   Fixed with the null-forgiving operator (`x.PackId!`), matching the existing idiom already used in
   `StandardsPackProjectionBuilder.cs` (`x.RuleId!`).
2. `src\EngiSentry.Rules\BoundedCheckEvaluator.cs` — an unresolvable `<see cref="EngineeringDistanceQaIntegrator"/>`
   doc comment. That type lives in `EngiSentry.Standards`, which `EngiSentry.Rules` must not reference
   (see dependency direction, §7). Adding a project reference would have inverted the accepted dependency
   direction, so the `cref` was replaced with the plain text form
   `EngiSentry.Standards.EngineeringDistanceQaIntegrator`. No behavior changed.

## 3. Files created (exact)

| File | Role |
|---|---|
| `src\EngiSentry.Contracts\BoundedRelationshipModels.cs` | `BoundedRelationship` — closed bounded taxonomy |
| `src\EngiSentry.Standards\StandardsPackProjectionBuilder.cs` | Governed pack rule projection (P6→P9 read-only view) |
| `src\EngiSentry.Standards\StandardsPackPackCatalog.cs` | Deterministic pack grouping + bounded rule selection |
| `src\EngiSentry.Rules\BoundedConstraintBuilder.cs` | Governed rule → bounded constraint mapping |
| `src\EngiSentry.Rules\BoundedCheckEvaluator.cs` | Read-only bounded CHECK evaluator |
| `tests\StandardsPackProjectionTests.cs` | Phase C test coverage (19 required behaviors) |
| `docs\handoffs\ENGISENTRY_PHASE_C_IMPLEMENTATION_2026-09-19.md` | This handoff |

The five source files were already present as untracked Phase C partial work at handoff start and were
completed/verified, not discarded. `tests\StandardsPackProjectionTests.cs` and this handoff are new.

## 4. Files modified (exact)

| File | Change |
|---|---|
| `tests\EngiSentry.Tests.csproj` | Added `<Compile Include="StandardsPackProjectionTests.cs" />` (classic non-SDK project with an explicit compile list; SDK globbing does not apply) |
| `tests\UnitTests.cs` | Added `StandardsPackProjectionTests.Run();` to `Main`, immediately after `ProposalContractTests.Run();` |

Plus the two in-place edits to Phase C files described in §2 (`StandardsPackPackCatalog.cs`,
`BoundedCheckEvaluator.cs`).

`Commands.cs`, `VisualResultsPalette.cs`, the Results UI, and all sample drawings were **not** touched —
no compile dependency on them arose. No DWG was modified.

## 5. Production types added (exact, 12)

`EngiSentry.Contracts`
- `BoundedRelationship` (static) — `EquipmentToRoad`, `EquipmentToPlantFence`, `EquipmentToUnitBoundary`,
  plus `IsSupported` / `TryResolve`.

`EngiSentry.Standards`
- `StandardsPackProjectionBuilder` (static) — `Build(...)`.
- `StandardsPackPackCatalog` (static) — `Group(...)`, `BoundedRules(...)`, `SupportedRelationships`.
- `StandardsPackGroup` (sealed).
- `PackIds` (static) — `ProjectCriteria = "project.plotplan.criteria"`.

`EngiSentry.Rules`
- `BoundedConstraintBuilder` (static) — `Build(...)`.
- `BoundedConstraintSet` (sealed).
- `BoundedConstraintDiagnostics` (sealed).
- `BoundedCheckEvaluator` (static) — `Evaluate(...)`.
- `BoundedConstraintCheck` (sealed).
- `BoundedCheckResult` (sealed).
- `BoundedCheckDiagnostics` (sealed).

`BoundedConstraint` and `ConstraintTreatment` already existed in `EngiSentry.Contracts` and were reused
unchanged. Test-only type: `EngiSentry.Tests.StandardsPackProjectionTests` (not production).

## 6. Governance components reused (no new authority created)

The projection is a **read-only view** composed entirely of existing authoritative types. Nothing below
was modified; nothing was added alongside them:

- Authority runtime: `PlotPlanStandardsAuthorityRuntimeContext`,
  `PlotPlanStandardsAuthorityRuntimeContextCatalog`.
- Registries/resolution: `StandardSourceRegistry`, `StandardClauseRegistry`, `StandardRulePackRegistry`,
  `StandardRulePackResolver`, `StandardAuthorityPolicyRegistry`, `StandardAuthorityResolver`,
  `StandardAuthorityExecutionGate`, `StandardAuthorityReviewProjector`.
- Authority results: `StandardExecutionGateResolution`, `StandardRuleExecutionGateDecision`,
  `StandardAuthorityResolution`, `StandardRuleAuthorityResolution`, `StandardClauseAuthorityResolution`,
  `StandardRulePackResolution`, `EffectiveStandardRuleDefinition`.
- State vocabularies: `StandardAuthorityClasses`, `StandardDecisionStates`, `StandardEnforcementModes`.
- P9 project governance: `ProjectDistanceGovernedRuleResolver`, `ProjectDistanceGovernedRuleResolution`,
  `ProjectDistanceGovernanceProvenance`, `ProjectDistanceGovernedRuleResolutionStates`.
- P8/P9 approval chain: `EngineeringDistanceApprovalRecordValidator`,
  `EngineeringDistanceApprovalRecordBuilder`, `EngineeringDistanceApprovalRecord`,
  `EngineeringDistanceCandidateBinding`, `EngineeringDistanceApprovalEvidenceBinding`,
  `EngineeringDistanceApprovalDecision`, `EngineeringDistanceCandidateDefinition`,
  `EngineeringDistanceApprovalAuthorityResult`, `EngineeringDistanceApprovalAuthorityStates`,
  `EngineeringDistanceApprovalCurrentnessResult`, `EngineeringDistanceApprovalCurrentnessStates`.
- Distance rules/evidence: `EngineeringDistanceRuleDefinition`, `EngineeringDistanceRuleRegistry`,
  `EngineeringDistanceCriteriaProvenance`, `EngineeringDistanceEvidenceKeys`,
  `PlotSpatialMeasurementModel`, `EquipmentSpatialMeasurement`.
- Contract state: `StandardsPackRule`, `ConstraintTreatment`, `ProposalConstraintState`.

## 7. Dependency direction result

**Preserved. No circular project reference.** Verified by test
(`StandardsPackProjectionTests.ContractSafetyAndLayering`) using assembly reference graphs:

- `EngiSentry.Standards` references `EngiSentry.Contracts` only — it does **not** reference
  `EngiSentry.Rules`.
- `EngiSentry.Rules` references `EngiSentry.Contracts` and `EngiSentry.PlotPlan` — it does **not**
  reference `EngiSentry.Standards`.
- There is no edge in either direction between `EngiSentry.Standards` and `EngiSentry.Rules`.

This is why `BoundedRelationship` lives in `EngiSentry.Contracts`: both the governed projection
(Standards) and the constraint engine (Rules) share the closed taxonomy without either project
referencing the other. `StandardsPackProjectionBuilder` accordingly accepts the rule set as the
contract-level `IReadOnlyList<EngineeringDistanceRuleDefinition>` (callers pass `registry.Rules`) rather
than the `EngineeringDistanceRuleRegistry` service, which lives in `EngiSentry.Rules`. No project
reference was added by Phase C.

Both Phase C assemblies were also asserted to reference no Autodesk/host assembly, and every new contract
type was asserted to expose no host-typed member.

## 8. PROJECT_RULE_ONLY behavior

- A project criterion with no approval and no authority resolves through the existing P9
  `ProjectDistanceGovernedRuleResolver` to `PROJECT_RULE_ONLY` with **no** `GovernanceProvenance` and an
  empty `AuthorityState`.
- In the projection, such a rule carries `ConstraintTreatment = PROJECT_REQUIREMENT` and is grouped under
  the explicit `PackIds.ProjectCriteria` pack (`project.plotplan.criteria`). Its `PackId` stays null and
  `Company` stays null, so it is never presented as a company standard.
- A genuine `GOVERNING_PROJECT_DETERMINISTIC` approval supplies governed provenance and the approved
  criterion; the projection carries that value through (`GOVERNED_COMPATIBLE` when it matches the
  configured value, `GOVERNED_CONDITIONAL` when it differs). A conditional approval's differing approved
  value **replaces** the configured value in the projection rather than being invented.
- Critically, an approved criterion does **not** grant execution authority. `AutoExecutionAllowed` comes
  only from the existing execution gate. A rule the gate did not mark auto-executable stays
  non-executable even with approved provenance.
- When the authority chain *does* mark a project rule auto-executable, the bounded CHECK executes and the
  result still reports `PROJECT_REQUIREMENT` provenance — project provenance remains explicit end to end.

## 9. Source-required / non-executable behavior

- `BoundedConstraintBuilder.IsExecutableEvidence` requires the gate to have granted
  `AutoExecutionAllowed` **and** a non-empty `RuleId`. Any rule failing either degrades to a
  `REVIEW_REQUIRED` constraint with an explanatory `UnitContext`, counted in
  `BoundedConstraintDiagnostics.NonExecutableAuthorityCount`.
- `ResolveTreatment` can only ever *record* treatment; it can never promote a non-executable rule. A
  recorded treatment that is unsupported, or is `OPTIMIZATION_OBJECTIVE` /
  `ENGINEERING_PREFERENCE` (which must not yield a numerical verdict), degrades to `REVIEW_REQUIRED` or
  `HARD_CONSTRAINT` respectively — never to an executing preference.
- Quarantined and legacy-conflict authority classes stay `REVIEW_REQUIRED`.
- Staleness blocks execution: a `STALE` currentness result forces `AutoExecutionAllowed = false`,
  `EngineeringReviewRequired = true` and `REVIEW_REQUIRED` treatment.
- Approval authority outside `GOVERNING_PROJECT_DETERMINISTIC` (inactive, not approved, stale approval,
  no policy, policy mismatch, unresolved legacy conflict, source quarantined) forces the same block.
- Downstream, `BoundedCheckEvaluator` never converts `REVIEW_REQUIRED` into `SATISFIED` or `VIOLATED`,
  and reports `ObservedDistanceMeters = null` for it.

## 10. CHECK behavior / state mapping

`BoundedCheckEvaluator.Evaluate` produces, per constraint, one `ProposalConstraintState`:

| Condition | State | Observed distance |
|---|---|---|
| Measurement basis outside the bounded taxonomy | `NOT_APPLICABLE` | null, `Relationship = null`, reported in diagnostics |
| `ConstraintTreatment == REVIEW_REQUIRED` (any governance block) | `REVIEW_REQUIRED` | null |
| No resolved measurement for the required basis | `INSUFFICIENT_EVIDENCE` | null, with reason |
| `Min(Distance2D) >= MinimumDistanceMeters` | `SATISFIED` | reported, with portable evidence handles |
| `Min(Distance2D) < MinimumDistanceMeters` | `VIOLATED` | reported, with portable evidence handles |

The minimum observed 2D distance for the basis is the value compared, matching the accepted
`EngineeringDistanceRuleEvaluator` selection semantics. `>=` means an exact-threshold measurement
satisfies the constraint; the threshold boundary is not a violation. Evidence handles
(`GeometrySourceHandle`, `LabelSourceHandle`) are the only host-side identifiers carried, de-duplicated
and ordinally ordered. No QA finding, rule result, PASS or FAIL is created; no existing status altered.

## 11. Deterministic behavior

Every stage orders internally and never depends on input order:

- `StandardsPackProjectionBuilder.Build` — rules ordered by `RuleId` (ordinal).
- `StandardsPackPackCatalog.Group` — grouped by pack identity, packs ordered by `PackId`, rules within a
  pack ordered by `RuleId`.
- `StandardsPackPackCatalog.BoundedRules` — ordered by `RuleId`.
- `BoundedConstraintBuilder.Build` — constraints ordered by `ConstraintId`, diagnostic reasons ordered.
- `BoundedCheckEvaluator.Evaluate` — constraints processed and emitted ordered by `ConstraintId`,
  evidence handles de-duplicated and ordered, diagnostic reasons ordered.

Proven by test: identical input yields an identical full-pipeline snapshot, and **reversed** input
(rules and measurements both reversed) yields the byte-identical snapshot. The same is asserted for pack
grouping and bounded rule selection independently.

## 12. Build result

```
MSBuild.exe .\EngiSentry.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64
```

- **BUILD: PASS**
- **Warnings: 0**
- **Errors: 0**
- All 10 solution projects build: Contracts, Semantics, Core, PlotPlan, Rules, Standards,
  CrossDocument, Revision, AutoCAD2021 (.NET Framework 4.8), Tests (v4.8 x64 Exe).

## 13. Test result

```
.\tests\bin\x64\Release\EngiSentry.Tests.exe
```

- **TESTS: PASS**
- **Exit code: 0**
- **Final output line:** `All EngiSentry M2.2 through M5.0 tests passed.`

## 14. New Phase C test coverage

`tests\StandardsPackProjectionTests.cs` — one harness entry (`Run`) across eight sections, following the
existing `internal static Run + Assert` convention. Every required behavior is asserted:

1. **Contract safety and layering** — no host reference in either Phase C assembly; no host-typed member
   on any new contract type; `EngiSentry.Rules` does not reference `EngiSentry.Standards`;
   `EngiSentry.Standards` does not reference `EngiSentry.Rules`; both reference `EngiSentry.Contracts`.
2. **Governed projection preserves provenance** — the accepted baseline (authority/gate 4/4, review
   projection present, **0 auto-executable**) is unchanged; clause and source-hash provenance flow
   through verbatim; `Company` is never synthesized; nothing projected becomes auto-executable; a
   quarantined source surfaces as `REVIEW_REQUIRED`; pack grouping is explicit and ordered.
3. **Missing authority stays blocked** — no gate decision → `REVIEW_REQUIRED`, no observed distance,
   `ExecutableConstraintCount == 0`.
4. **Quarantined source stays blocked** — both in the real authority projection and at the CHECK layer.
5. **Expert-review-required stays blocked** — `EXPERT_REVIEW_REQUIRED` decision state → `REVIEW_REQUIRED`.
6. **Stale approval stays blocked** — `STALE_APPROVAL` at the CHECK layer; `STALE` currentness blocks via
   the projection; `STALE_APPROVAL` authority resolves to `PROJECT_RULE_ONLY` with no provenance.
7. **P8 REVIEW_ACCEPTED does not substitute for P9 APPROVED** — two independent proofs: the existing P9
   `ProjectDistanceGovernedRuleResolver` *rejects* a `REVIEW_ACCEPTED` authority state outright
   (`ProjectDistanceGovernedRuleResolverValidationException`, rather than silently promoting it), and a
   `REVIEW_ACCEPTED` decision state at the CHECK layer stays `REVIEW_REQUIRED`. By contrast a genuine
   `GOVERNING_PROJECT_DETERMINISTIC` approval resolves to `GOVERNED_COMPATIBLE` **with** provenance —
   only that path supplies it.
8. **PROJECT_RULE_ONLY current approved rule can execute** — proven through the full P9 chain (resolver →
   projection → constraint builder → CHECK) and, for the executable case, with a rule the authority chain
   marked auto-executable: the bounded constraint executes, yields `SATISFIED`, and still reports
   `PROJECT_REQUIREMENT` provenance.
9. **PROJECT_RULE_ONLY provenance remains explicit** — grouped under `PackIds.ProjectCriteria`,
   `PROJECT_REQUIREMENT` treatment end to end, `PackId`/`Company` null.
10. **Equipment → Road mapping** — both `AccessRoadEdge` and `UnitPeripheralRoadEdge` resolve to
    `EquipmentToRoad`; end-to-end CHECK relationship asserted.
11. **Equipment → PlantFence mapping** — `SecurityFence` → `EquipmentToPlantFence`.
12. **Equipment → UnitBoundary mapping** — `UnitBoundary` → `EquipmentToUnitBoundary`.
13. **Unsupported / reversed relationship does not execute** — `SiteBoundary` (an accepted measurement
    basis, but outside the bounded taxonomy) and reversed forms (`RoadToEquipment`,
    `PlantFenceToEquipment`) resolve to null; the constraint builder emits
    `unsupported-relationship` diagnostics and produces **no** constraint; fed directly to the
    evaluator, an out-of-taxonomy constraint is `NOT_APPLICABLE`.
14. **Satisfied CHECK** — observed above criterion → `SATISFIED` with observed/required distances and
    ordered evidence handles.
15. **Violated CHECK** — observed below criterion → `VIOLATED`.
16. **Exact-threshold behavior** — observed == criterion → `SATISFIED` (`>=`), not a violation.
17. **Insufficient-evidence behavior** — no measurement for the basis → `INSUFFICIENT_EVIDENCE`, null
    observed distance, reason reported, no verdict invented.
18. **Non-executable governance behavior** — review-required rule with full evidence present stays
    `REVIEW_REQUIRED` with null observed distance.
19. **Deterministic repeated / reordered behavior** — full-pipeline snapshots identical for repeated and
    for reversed input; grouping and bounded selection order-independent.
20. **No existing evaluator / governance regression** — `StandardsPackRule` contract defaults unchanged;
    `EngineeringDistanceRuleRegistry.CreateProjectCriteria` still accepts and deterministically orders
    project criteria and still resolves/misses lookups; the accepted authority/gate/review cardinality
    and zero-auto-executable baseline are unchanged; empty and null inputs yield empty results, never
    fabricated criteria.

All numerical values used in tests are synthetic and remain inside the test class. No production catalog
holds a synthetic value. P9 approval fixtures are genuine artifacts — they are built through the existing
`EngineeringDistanceApprovalRecordBuilder` and accepted by the existing
`EngineeringDistanceApprovalRecordValidator` unchanged.

## 15. Acceptance caveats (read before acceptance)

1. **Unit consistency prerequisite.** The accepted `EngineeringDistanceRuleEvaluator` resolves drawing
   units internally (`MetersToDrawingUnits`) and compares `Distance2D` in drawing units.
   `BoundedCheckEvaluator` compares `Min(Distance2D)` against `MinimumDistanceMeters` and reports
   `ObservedDistanceMeters`. For a bounded CHECK to be meaningful, the measurements supplied must be
   expressed in meters consistent with `MinimumDistanceMeters`, or the production wiring must supply a
   unit resolution alongside them. **This was deliberately not "fixed" by changing the accepted
   evaluator's semantics or by adding a second distance algorithm** — both are forbidden. The
   `BoundedCheckEvaluator.Evaluate` contract states measurements are read only and never recomputed;
   production callers must honor the meter-consistency prerequisite. This is the one integration
   decision Phase D wiring must make explicit.
2. **Execution authority is not synthesized.** In the accepted runtime context, zero rules are
   auto-executable, so a real bounded CHECK against today's governance yields only `REVIEW_REQUIRED`,
   `INSUFFICIENT_EVIDENCE` and `NOT_APPLICABLE` — never `SATISFIED`/`VIOLATED`. That is the correct
   governed outcome, not a gap. A `SATISFIED`/`VIOLATED` result requires the authority chain to mark a
   rule auto-executable; the tests cover that path with an explicit fixture.
3. `StandardsPackRule.Company` remains null unless a registered pack name is supplied via
   `packNamesByPackId`. Phase C never invents a company name.

## 16. Worktree safety

- `git diff --check`: no whitespace errors. Output contained only repo-wide pre-existing CRLF/LF
  normalization notices that apply to virtually every file in the repository; no trailing whitespace,
  space-before-tab or indentation errors were reported.
- HEAD unchanged: `0c927caaac06e519841867e07a1ca475dd4dacae` before and after.
- No commit, no stage, no push.
- Not touched or staged: `.vscode/`, `artifacts/`,
  `docs\handoffs\P10.3-SUPERVISED-ENGINEER-PILOT-RECORD.md`, `packaging\installer/`.
- No DWG modified. No user sample drawing modified.

## 17. Files proposed for the Phase C commit

```
src/EngiSentry.Contracts/BoundedRelationshipModels.cs
src/EngiSentry.Standards/StandardsPackPackCatalog.cs
src/EngiSentry.Standards/StandardsPackProjectionBuilder.cs
src/EngiSentry.Rules/BoundedConstraintBuilder.cs
src/EngiSentry.Rules/BoundedCheckEvaluator.cs
tests/StandardsPackProjectionTests.cs
tests/EngiSentry.Tests.csproj
tests/UnitTests.cs
docs/handoffs/ENGISENTRY_PHASE_C_IMPLEMENTATION_2026-09-19.md
```

Explicitly **excluded** from the proposed commit (pre-existing untracked, protected):
`.vscode/`, `artifacts/`, `docs/handoffs/P10.3-SUPERVISED-ENGINEER-PILOT-RECORD.md`, `packaging/installer/`.

## 18. Not started (Phase D scope)

Candidate position generation, ranking, optimization, SUGGEST path, Results UI changes. No Phase D
artifact was created.

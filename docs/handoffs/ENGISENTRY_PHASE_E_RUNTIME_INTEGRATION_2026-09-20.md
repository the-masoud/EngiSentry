# EngiSentry Phase E Implementation Handoff — 2026-09-20

Read-only proposal runtime integration (Phase A section K, especially K.3): the accepted Phase D
`PlacementProposal` pipeline is exposed through AutoCAD 2021 as one additive command, and the accepted
proposal set is emitted as a separate sidecar artifact. **No DWG is modified. No UI is added.**

Accepted baseline HEAD: `fe584757b55fd7d73d478a9d634720e836d4fbe5` ("feat: add canonical Phase D placement
proposals"). Phases A–D are closed; the Phase D engine is not redesigned. No commit, no stage, no push.

## 1. Status

**PASS_CANDIDATE.** Build PASS (0 errors, 0 warnings under `/warnaserror`); full test harness PASS (exit 0,
"All EngiSentry M2.2 through M5.0 tests passed.", 16 Phase E integration tests). Both reported production
defects are fixed in the Phase E integration surface only. No accepted Phase A–D behavior, contract,
serialized shape, unit path or DWG was changed.

Three components are delivered: the K.3 sidecar writer, the `ES_SUGGEST` command, and the governed
per-placement evaluator bridge.

- **Unit consistency — FIXED** (former defect 5.1 / Section 5 defect 5): the CHECK now receives a
  metre-consistent projection through the accepted `DrawingUnitResolver`, so it is dimensionally correct for
  any supported insertion unit. Millimetre regression passing.
- **Per-placement violations — FIXED** under the authorized architecture decision (Section 3): one additive
  governed-constraint entry point on `EngineeringDistanceRuleEvaluator` lets the proposal runtime reuse the
  *same* accepted selection implementation. One rule/basis now yields one violation per `PlacementId`, so two
  violated placements become two proposals and a satisfied placement contributes none. No measurement
  selection was duplicated and none is performed in the runtime.

**Runtime acceptance (Section 9): BLOCKED on a host/UI prerequisite, not on a software defect.** The plugin
loaded, `ES_SUGGEST` was registered and invoked inside AutoCAD Core Console, and then blocked on the
interactive Project Distance Criteria confirmation dialog — a modal `Form.ShowDialog()` that an unattended
console run cannot dismiss. Per the task's own provision this is recorded as a host blocker and classified
separately from the software; no corrective retry was warranted because it is not a fixable Phase E defect.
Every software-side verification that does not require dismissing that dialog is complete and passing.

No prohibited scope was entered.

## 2. Components delivered

### 2.1 `PlacementProposalSnapshotWriter` — the K.3 sidecar

`src/EngiSentry.AutoCAD2021/Scanning/PlacementProposalSnapshotWriter.cs` (new, `public static`).

Emits the accepted proposal set as a **separate** artifact alongside the accepted Plot Plan QA report and the
standards review package, following the accepted `SnapshotFileWriter` sidecar pattern exactly:

- Path: `<artifacts>/qa/plotplan/<drawing>.<snapshot timestamp UTC>.placement-proposal.json`.
- `DataContractJsonSerializer` over `List<PlacementProposal>`, the accepted serialization stack.
- `FileMode.CreateNew, FileAccess.Write, FileShare.None` — a second write to the same path is refused rather
  than silently overwriting, exactly like every accepted sidecar writer.
- **Deterministic ordering is the writer's job**, not the caller's: proposals are re-ordered by
  `ProposalId` with `StringComparer.Ordinal` before serialization, so identical evidence always yields a
  byte-identical artifact regardless of construction order. Reverse-ordered input produces the identical file.
- Null entries are skipped, never serialized.
- `Read(string path)` round-trips the artifact in the deterministic order it was written.
- Reuses `SnapshotFileWriter.ResolveArtifactsRoot()` and `SnapshotFileWriter.SanitizeFileName()` so root
  resolution and sanitization stay single-sourced.

**No field was added to `PlotPlanQaReport`.** Its serialized shape is unchanged (still exactly three
`[DataMember]` properties: `PlotPlan`, `Qa`, `StandardsAuthority`). `ReviewProjection` remains
`[IgnoreDataMember]` and stays out of the PlotQA JSON. The proposal sidecar is the only proposal carrier.

The sidecar is a pure projection: written after the CHECK+SUGGEST pipeline completed, never opens a document,
never mutates a DWG.

### 2.2 `ES_SUGGEST` — one additive command

`src/EngiSentry.AutoCAD2021/Commands.cs` — a single new command method plus one private helper. It follows
the accepted `ES_PLOTQA` pattern end to end:

```
resolve active document → Project Distance Criteria → DrawingScanner.Scan (read-only)
  → SemanticExtractor.Extract → EntityIndexer.Build → PlotPlanBuilder.Build
  → ProjectDistanceGovernanceRuntimeLoader.LoadCanonical (accepted P9 aggregate)
  → StandardsPackProjectionBuilder.Build (accepted governed Phase C pack)
  → PlacementProposalRuntime.Build (governed CHECK diagnostics + governed per-placement evaluation
      → Phase D generation)
  → DBMOD guard → PlacementProposalSnapshotWriter.Write → DBMOD guard → report result
```

Read-only protections, both present and unchanged from the accepted convention:

- `DrawingScanner.Scan(document, true, ...)` — the read-only scan mode, as in `ES_PLOTQA`.
- **DBMOD is captured before processing and compared twice**: immediately after the pipeline runs and again
  after the sidecar is written. Either mismatch throws
  `InvalidOperationException("DBMOD changed during placement suggestion ...")`. The success line reports
  `DBMOD unchanged: YES` / `Status: READ-ONLY`.
- The command writes only to the editor and to the sidecar file. It calls no document, transaction, entity,
  block-table or write API.

`VisualResultsPalette.cs` was **not** modified — byte-identical to HEAD (blob
`d40964121b25fe6c55111ced799c838f59fdac48`, verified). No new UI. The success path does not touch the palette
at all; only the pre-existing `ShowFailure` / `ShowCancelled` entry points are reused on the failure and
cancel paths, unchanged.

The proposal set remains advisory evidence: `PlacementProposal.EngineeringReviewRequired` is always true in
v1, and the report line carries the full CHECK ledger (governed rules, bounded constraints, violated,
review-required, insufficient-evidence, executable violations, proposal count) so a zero-proposal run is
reported as a result, never as a failure.

## 3. The governed per-placement evaluator bridge (authorized architecture decision)

This resolves the per-placement defect that was previously recorded as an architectural blocker. The
resolution was authorized as exactly one additive entry point; it does not duplicate the accepted measurement
selection and it does not synthesize governance evidence.

### 3.1 The additive entry point

`src/EngiSentry.Rules/EngineeringDistanceRuleEvaluator.cs` — one new public method:

```csharp
public static EngineeringDistanceRuleEvaluation EvaluateGovernedConstraint(
    BoundedConstraint constraint, PlotPlanModel plotPlan)
```

It evaluates **one already-governed** `BoundedConstraint` against the accepted plot plan and returns the
accepted per-placement violation vocabulary for it. It consumes only the fields a governed bounded constraint
already carries — `Rule.RuleId`, `MeasurementKind`, `MinimumDistanceMeters`, `RequiredMeasurementBasisKey`,
`RequiredApplicabilityKey`. No `EngineeringDistanceRuleRegistry` is created or required, and nothing is
synthesized: no `CriteriaProvenance`, no `ProjectDirectory`, no source, clause, approval, authority, pack or
rule value. The evaluation records `CriteriaProvenance` as `null`, because a bounded constraint carries no
provenance and inventing one would fabricate governance evidence.

### 3.2 One implementation, not two

The accepted rule-evaluation implementation was factored into a private `EvaluateRuleCore(...)` whose
parameters are exactly the scalar fields both callers have. Both public entry points reach it:

- `Evaluate(EngineeringDistanceRuleRegistry, PlotPlanModel)` → `EvaluateRule(rule, ...)` adapter →
  `EvaluateRuleCore(...)`, carrying the rule's own provenance.
- `EvaluateGovernedConstraint(BoundedConstraint, PlotPlanModel)` → `EvaluateRuleCore(...)`, provenance null.

The accepted selection semantics — filter by `SubjectKind` + basis key + applicability key, `GroupBy(PlacementId)`,
minimum `Distance2D` per placement, `MeasurementId` ordinal tie-break, `DrawingUnitResolver` conversion,
exact-threshold comparison, `EngineeringDistanceViolation` construction, deterministic ordering — live in
exactly one place. Nothing was copied into a second method.

**Existing `Evaluate(...)` public behavior is unchanged**: same signature, same argument validation, same
per-rule ordering, same evaluation and violation construction. `EngineeringDistanceRuleRegistry` and its
validation are entirely unmodified (not present in the working-tree diff).

### 3.3 What the runtime no longer does

`PlacementProposalRuntime.Build` was rewritten around the bridge:

- **`GoverningMeasurements` is gone.** The global collapse — one governing measurement per bounded constraint
  — is removed, not worked around.
- No `GroupBy(PlacementId)` and no minimum-measurement selection happens in the runtime; both are the
  evaluator's job.
- `BoundedCheckEvaluator` is retained **only** for the aggregate CHECK diagnostics (the report ledger). Its
  single global violation row is never used to choose which placements receive a proposal.
- For each executable bounded constraint: evaluate it through the bridge, consume its
  `EngineeringDistanceViolation` list, and generate one Phase D proposal per violated `PlacementId`.

### 3.4 `BuildViolationContext` — wiring only, never recomputation

The violation itself is never recomputed: the accepted evaluator already selected the governing measurement,
recorded both distances and constructed the evidence handles. What the wiring adds is only the accepted plot
plan evidence the generator needs and the violation vocabulary does not carry — the equipment position (matched
to the selected measurement by `MeasurementId`, so position and violation can never disagree), the placement
status, the resolved geometry primitive (matched by `GeometrySourceHandle`), and the unit resolution. The
constraint identity on the context's violation is the governed bounded constraint, since that is the
constraint the proposal addresses; every measured field stays the accepted evaluator's value.

Each proposal therefore preserves `PlacementId`, `EquipmentEntityId`, `NormalizedTag`, `MeasurementId`,
`MeasurementBasisKey`, `ApplicabilityKey`, `GeometrySourceHandle`, `LabelSourceHandle`, `ClosestPointX/Y`,
`ActualDistanceDrawingUnits`, `ActualDistanceMeters`, the equipment position and the governing
rule/constraint identity.

## 4. Integration tests

`tests/PlacementProposalRuntimeTests.cs` (new, registered in `tests/EngiSentry.Tests.csproj`, invoked from
`UnitTests.Main()` after `PlacementProposalTests.Run()`). Sixteen focused integration behaviors, all synthetic
fixtures local to the class:

1. `ContractLayering` — the runtime and sidecar writer sit in the accepted host assembly, still reference the
   accepted contracts/rules/standards/revision layers, expose no host-typed member, return the accepted
   `List<PlacementProposal>`, and `PlotPlanQaReport` still has exactly its three serialized members.
2. `ZeroProposalGovernedResult` — no governed rules, and rules with no measurement evidence, both yield zero
   proposals with matching diagnostics.
3. `ReviewRequiredYieldsNoProposal` — a non-auto-executable rule produces no proposal and surfaces as
   `ReviewRequired`, never `Violated`; the same evidence with an executable rule does reach generation.
4. `EligibleFixtureProducesProposals` — an eligible fixture produces proposals, each carrying the violated
   governed rule identity, the required and observed distances, the mandatory
   assumptions/known-limitations/unchecked-constraints lists, `EngineeringReviewRequired == true`, a proposed
   position that actually achieves the required clearance, and deterministic identity ordering.
5. `MillimeterDrawingProducesViolatedProposal` — the identical physical case in millimetre insertion units
   (actual 10 m recorded as `Distance2D = 10000`, required 15 m). The CHECK must read `Violated`, not
   `Satisfied`, and must still reach the eligible proposal path; every proposal reports its observed distance
   in metres, and the metre and millimetre fixtures agree in proposal count, violated count and observed
   distance. Regression test for the unit defect in Section 5.
6. `GovernedEvaluatorParity` — a fixture representable through both paths yields equivalent violations from
   `EvaluateGovernedConstraint` and from the registry path: compared field-by-field on `MeasurementId`,
   `PlacementId`, `EquipmentEntityId`, `NormalizedTag`, `SubjectKind`, `ActualDistanceDrawingUnits`,
   `ActualDistanceMeters`, `GeometrySourceHandle`, `LabelSourceHandle`, `ClosestPointX/Y`,
   `MeasurementBasisKey`, `ApplicabilityKey`; plus `governed.CriteriaProvenance == null` while the registry
   path carries it. This is the direct assertion that the bridge shares, not duplicates, the implementation.
7. `MultiPlacementPerPlacementProposals` — two violated placements under one governed constraint produce
   **exactly two** proposals with two distinct observed measurement identities (Section 6 case A).
8. `MixedViolatedAndSatisfiedPlacements` — one violated placement plus one satisfied placement under the same
   constraint produce **exactly one** proposal, for the violated placement (Section 6 case B).
9. `DuplicateMeasurementPerPlacement` — the same `PlacementId` with two measurements is governed by the
   minimum, yields **exactly one** proposal at the governing distance, and produces no duplicate (Section 6
   case C).
10. `ReorderedMeasurementInput` — reversed measurement input yields the same proposals in the same order
    (Section 6 case D).
11. `MultiPlacementSidecarRoundTrip` — a multi-placement proposal set round-trips through the sidecar with
    per-placement fidelity preserved, one entry per violated placement, deterministic order.
12. `SidecarDeterminismAndCreateNew` — identical evidence yields byte-identical artifacts across distinct
    paths; reversed construction order yields the identical artifact; the round-trip preserves the accepted
    contract and the deterministic order; the filename matches the K.3 contract and lands under `qa/plotplan`;
    a second write to the same path is refused (`FileMode.CreateNew`); a zero-proposal set round-trips as an
    empty JSON array; null proposals are skipped.
13. `PlotPlanQaReportSerializationUnchanged` — the report serializes as a JSON object carrying no
    placement-proposal reference, the accepted compactor still produces the accepted shape, the proposal
    sidecar name is the K.3 name and never the `.plotplan.qa.json` name, and the report round-trips unchanged.
14. `ReviewProjectionAbsentFromPlotQaJson` — `ReviewProjection` stays out of the PlotQA JSON even when set,
    and keeps its `[IgnoreDataMember]` attribute.
15. `NoDrawingMutationSurface` — neither type exposes a document/transaction/modify/move/erase/set method, no
    result, diagnostics or proposal type carries a host-typed member, and a run leaves the evidence intact.
16. `CoreConsoleHostSafety` — the accepted `AutoCADHostBoundary.IsCoreConsole` boundary still exists with its
    accepted shape, the harness process is not `accoreconsole` (so the palette short-circuit is reachable only
    inside the real console), and `Commands.Suggest` keeps the accepted command contract (public, instance,
    parameterless, `void`, on `Commands`), while the palette type stays in the host assembly and the runtime
    stays host-free.

Test sidecars are written into the accepted artifacts root and removed in a `finally`; the pre-existing
`artifacts/qa/plotplan` contents were not touched (verified: no `phase-e-*` residue, existing files
unchanged).

These tests deliberately do **not** repeat the accepted Phase D candidate/ranking/re-evaluation coverage —
that belongs to `PlacementProposalTests` — nor the governance chain itself.

### 4.1 Fixture unit basis

The default fixtures use **metre insertion units** (`InsertionUnits = "Meters"`, 1 drawing unit = 1 m), and
`MillimeterDrawingProducesViolatedProposal` adds a **millimetre** drawing (`"Millimeters"`, 1 drawing unit =
1 mm) for the same physical case. The fixture builders take the insertion units and the metres-per-drawing-unit
factor, so geometry, equipment position and `Distance2D` are all expressed consistently in the drawing's own
unit. The millimetre fixture is the load-bearing one: in metre units the unit defect is invisible, because
drawing units happen to equal metres.

For the parity and multi-placement fixtures, `RegistryRoadRule` carries a real
`CriteriaProvenance` (`SourceKind == "ProjectDistanceCriteria"`, absolute `ProjectDirectory`, basis-compatible
`CriteriaSettingKey`, `ConfiguredMinimumDistanceMeters == MinimumDistanceMeters`), so the registry path in the
parity test exercises the accepted `CreateProjectCriteria` validation rather than a shortcut.

### 4.2 Two verification boundaries, stated

- The `CommandMethod("ES_SUGGEST")` attribute **cannot be read out of process**. Both
  `GetCustomAttributes(true)` and metadata-only `GetCustomAttributesData()` resolve the attribute's
  `accoremgd` constructor, and the harness intentionally does not ship the AutoCAD runtime (Autodesk
  references stay Copy Local = false, per the project guardrails). The test therefore asserts the compiled
  command contract; the registration string itself is verified inside the host, where the attribute is
  loadable. Section 9 records that in-host verification.
- `Commands.Suggest` is a host-bound method, so its DBMOD guard is reviewed in code
  (`Commands.cs:373-377`) rather than executed by the harness; the harness has no document, no `DBMOD`
  system variable and no AutoCAD runtime.

Neither boundary is a vacuous placeholder: the assertions that are executable out of process are made, and
the two that require the host are identified so they are verified during in-host acceptance.

## 5. Defects found and fixed in this phase

All defects were confined to the Phase E integration surface. No accepted Phase A–D behavior, contract,
serialized shape, unit path or DWG was changed.

1. **CS8601 in `PlacementProposalRuntime.cs`** (build-breaking under `/warnaserror`). A redundant
   `?? "<none>"` on the non-nullable `measurement.GeometrySourceHandle` poisoned Roslyn's nullable flow
   analysis for the rest of the method, surfacing as a possible-null assignment in the
   `EngineeringDistanceViolation` initializer. Fixed by removing the dead coalesce — the property is
   non-nullable, so the message is unchanged in every reachable case. No suppression was added.
2. **Stale `git` stat cache** reported `VisualResultsPalette.cs` as modified while the working tree was
   byte-identical to HEAD. Cleared with `git update-index --refresh`; the blob hash was re-verified against
   HEAD. The file was never edited.
3. **Test-side compile defects** (all in the new test file, none in production code): missing
   `System.Runtime.Serialization` import; `string.Contains(string, StringComparison)` (not available on
   .NET Framework 4.8) replaced with `IndexOf(...) < 0`; a nullable out-parameter dereference; an assertion
   on a `blocked:` reason that cannot be reached because the accepted upstream gate degrades non-executable
   rules to `ReviewRequired` before the runtime sees them (replaced with the correct
   `ReviewRequiredCount > 0` / `ViolatedCount == 0` assertions, plus an assertion that the runtime's blocked
   branch stays unreachable); and the `accoremgd` attribute-read failure described in 4.2.
4. **Scratch diagnosis files** (`contracts.il`, `contracts.res`) created during a nullable-analysis
   investigation were removed; they were never part of the deliverable.
5. **Unit inconsistency in the CHECK wiring** (production defect). `PlacementProposalRuntime.Build` passed the
   raw `plotPlan.Measurements` to `BoundedCheckEvaluator.Evaluate`. `EquipmentSpatialMeasurement.Distance2D`
   is recorded in **drawing units**, while the evaluator compares it against
   `BoundedConstraint.MinimumDistanceMeters` and, by its own contract, never converts. On any non-metre
   drawing the comparison is dimensionally wrong in the dangerous direction: a millimetre drawing records a
   10 m clearance as `10000`, which the CHECK reads as 10000 m and reports `SATISFIED`, hiding the violation
   and starving the eligible proposal path.

   This is exactly the case Phase C's acceptance caveat 15.1 reserved: "the production wiring must supply a
   unit resolution alongside them", having deliberately not "fixed" it inside the accepted evaluator or by
   adding a second distance algorithm. Fixed in the wiring only — `Build` resolves
   `DrawingUnitResolver.Resolve(plotPlan.Drawing.InsertionUnits)` once and presents the CHECK with
   `MeasurementsInMeters(...)`: a read-only projection whose spatial `Distance2D` is converted through the
   accepted resolver. Only the compared field is projected (pair measurements and statistics are carried by
   reference; the evaluator reads neither), and the original drawing-unit model remains the sole source of
   `ActualDistanceDrawingUnits`, so `ViolationContext` and the emitted proposals are unchanged in unit
   semantics. `BoundedCheckEvaluator` and every distance algorithm are untouched. Without the fix,
   `MillimeterDrawingProducesViolatedProposal` fails on its first assertion.

   The governed per-placement bridge converts through the same resolver *inside* the accepted evaluator, so
   the per-placement path was never affected by this defect; the fix keeps the aggregate CHECK dimensionally
   honest alongside it.

6. **Per-placement collapse in the proposal runtime** (production defect). `PlacementProposalRuntime.Build`
   collapsed measurements to one per bounded constraint: `BoundedCheckEvaluator` emits one
   `BoundedConstraintCheck` per constraint (its evidence selection is `Min(Distance2D)` over the whole basis,
   with no placement partition), and the removed `GoverningMeasurements` then picked one governing
   measurement per constraint. Two equipment placements violating the same Road rule therefore yielded exactly
   one proposal, and a placement that satisfied the rule could be the one selected as governing. The accepted
   per-placement semantics — `GroupBy(PlacementId)` → governing minimum per placement — were not on this path.

   Fixed by the authorized bridge in Section 3: the runtime now evaluates each executable bounded constraint
   through `EvaluateGovernedConstraint` and generates one proposal per violated `PlacementId`. The accepted
   selection semantics are reached, not duplicated. The former architectural blocker (no accepted registry
   constructor is reachable from governed inputs, and synthesizing provenance would fabricate governance
   evidence) is dissolved because the bridge requires no registry and no provenance at all.

## 6. Multi-placement behavior, stated per case

All four cases are asserted by the tests named in Section 4.

| Case | Fixture | Expected | Test |
| --- | --- | --- | --- |
| A — two violated placements | `PlotPlanWithTwoPlacements` | exactly 2 proposals, 2 distinct observed measurement identities | `MultiPlacementPerPlacementProposals` |
| B — one violated + one satisfied | `PlotPlanWithTwoPlacements` | exactly 1 proposal, for the violated placement | `MixedViolatedAndSatisfiedPlacements` |
| C — same `PlacementId`, two measurements | `PlotPlanWithDuplicateMeasurements` | exactly 1 proposal, governed by the minimum, no duplicate | `DuplicateMeasurementPerPlacement` |
| D — reversed measurement input | `PlotPlanWithTwoPlacementsReversed` | same proposals, same order | `ReorderedMeasurementInput` |

## 7. Non-regression evidence

- Build: `MSBuild.exe .\EngiSentry.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64 /warnaserror` →
  **0 errors, 0 warnings**, exit 0.
- Tests: `.\tests\bin\x64\Release\EngiSentry.Tests.exe` → **exit 0**, "All EngiSentry M2.2 through M5.0 tests
  passed." Every previously passing suite (cross-document, revision, unit, profile, measurement, clearance,
  proposal contract, standards pack projection, placement proposal) passes unchanged; the sixteen Phase E
  tests are additions.
- `git diff --check` clean; no whitespace damage.
- HEAD unchanged at `fe584757b55fd7d73d478a9d634720e836d4fbe5` before and after; no commit, no stage, no push.
- `VisualResultsPalette.cs` byte-identical to HEAD.
- `PlotPlanQaReport` serialized shape unchanged (3 `[DataMember]`, `ReviewProjection` ignored).
- `EngineeringDistanceRuleRegistry` and its validation, `BoundedCheckEvaluator`, `BoundedConstraintBuilder`,
  `ProjectDistanceRuleBinder`, `StandardsPackProjectionBuilder` and the authority catalog are all unmodified.
- The evaluator change is additive plus an internal refactor: existing `Evaluate(...)` public behavior
  unchanged, and both entry points share `EvaluateRuleCore`.
- `artifacts/qa/plotplan` pre-existing contents untouched; no test residue.

## 8. Files

Created (untracked):

- `src/EngiSentry.AutoCAD2021/Scanning/PlacementProposalRuntime.cs`
- `src/EngiSentry.AutoCAD2021/Scanning/PlacementProposalSnapshotWriter.cs`
- `tests/PlacementProposalRuntimeTests.cs`
- `docs/handoffs/ENGISENTRY_PHASE_E_RUNTIME_INTEGRATION_2026-09-20.md` (this document)

Modified (tracked, working tree only):

- `src/EngiSentry.Rules/EngineeringDistanceRuleEvaluator.cs` — the additive `EvaluateGovernedConstraint`
  entry point, the shared private `EvaluateRuleCore`, and the thin `EvaluateRule` adapter. 70 insertions,
  11 deletions.
- `src/EngiSentry.AutoCAD2021/Commands.cs` — `ES_SUGGEST` command + `BuildGovernedResolutions` helper.
- `src/EngiSentry.AutoCAD2021/EngiSentry.AutoCAD2021.csproj` — registers the two new source files.
- `tests/EngiSentry.Tests.csproj` — registers the new test file.
- `tests/UnitTests.cs` — invokes `PlacementProposalRuntimeTests.Run()`.

Not touched: `.vscode/`, `docs/handoffs/P10.3-SUPERVISED-ENGINEER-PILOT-RECORD.md`,
`packaging/installer/`, `VisualResultsPalette.cs`, every accepted Phase A–D type, every DWG.

## 9. AutoCAD 2021 runtime acceptance — PASS

**Method.** A disposable copy of the protected source drawing was opened in interactive AutoCAD 2021:
`artifacts/runtime-validation/phase-e/ES-SUGGEST-A0-phase-e.dwg`.
The deployed EngiSentry bundle was loaded and `ES_SUGGEST` was executed. The existing Project Distance
Criteria dialog was confirmed interactively; no fabricated approval, source, clause, or governed authority
was introduced. The protected source drawing was never opened.

**Result.**

| Check | Result |
| --- | --- |
| Plugin load | **PASS** |
| `ES_SUGGEST` registered and invoked | **PASS** |
| Criteria resolution | **PASS** — existing interactive confirmation completed |
| Command completion | **PASS** |
| Governed rules / bounded constraints | **3 / 3** |
| Review-required constraints | **3** |
| Insufficient-evidence constraints | **0** |
| Executable violations | **0** |
| Placement proposals | **0** — correct governed result because all three constraints remain review-required |
| DBMOD before == after | **PASS** — command reported `DBMOD unchanged: YES` |
| Runtime status | **PASS** — command reported `Status: READ-ONLY` |
| Sidecar emitted | **PASS** — `%LOCALAPPDATA%/EngiSentry/artifacts/qa/plotplan/ES-SUGGEST-A0-phase-e.dwg.20260921T060137271Z.placement-proposal.json` |
| Sidecar parse | **PASS** — valid JSON, exact content `[]`, proposal count 0 |
| Sidecar SHA256 | **PASS** — `4F53CDA18C2BAA0C0354BB5F9A3ECBE5ED12AB4D8E11BA873C2F11161202B945` |
| Protected source DWG SHA256 | **PASS** — `3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4`, unchanged |

This closes the previous Core Console host/UI prerequisite. The interactive host run reached the full
post-dialog pipeline, emitted the proposal sidecar, preserved the governance block as zero proposals, and
completed with the read-only DBMOD guard intact.

## 10. Acceptance checklist

- [x] K.3 sidecar written as a separate artifact, accepted serialization stack, deterministic order,
      `FileMode.CreateNew`.
- [x] `PlotPlanQaReport` unchanged; no proposal field added; `ReviewProjection` stays out of the JSON.
- [x] One additive command `ES_SUGGEST`, following the `ES_PLOTQA` pattern.
- [x] Command read-only: read-only scan, DBMOD guards before and after the artifact write, no mutation API.
- [x] `VisualResultsPalette.cs` unmodified; no new UI.
- [x] Accepted Phase B/C/D types and services reused; no new proposal model, governance resolver, distance or
      geometry algorithm; no invented rule, authority, distance, clause or approval.
- [x] Non-auto-executable / review-required rules generate no proposal.
- [x] Zero proposals reported as a legitimate result when governance blocks execution.
- [x] **Unit consistency:** the CHECK is presented with a metre-consistent projection through the accepted
      `DrawingUnitResolver`, so the comparison is dimensionally correct for any supported insertion unit;
      `BoundedCheckEvaluator` and `EngineeringDistanceRuleEvaluator`'s algorithm unchanged; millimetre
      regression passes. Metre 10 m vs 15 m → violation; millimetre `Distance2D = 10000` vs 15 m → the same
      logical violation.
- [x] **Per-placement violations:** one governed rule/basis yields one violation per `PlacementId` through the
      authorized additive entry point; the accepted `GroupBy(PlacementId)` selection is shared, not copied;
      no registry, provenance or criterion synthesized; `GoverningMeasurements` removed.
- [x] Evaluator parity: a fixture representable through both paths yields equivalent violations; governed
      provenance is null.
- [x] Multi-placement cases A/B/C/D all pass, plus a multi-placement sidecar round-trip.
- [x] Existing `Evaluate(...)` public behavior unchanged; `EngineeringDistanceRuleRegistry` validation
      unchanged.
- [x] Build PASS under `/warnaserror`; full harness PASS.
- [x] No DWG modified. No commit, no stage, no push.
- [x] Runtime: plugin load and `ES_SUGGEST` registration/invocation verified inside the host; DWG integrity
      verified. **Host blocker recorded** for the interactive criteria confirmation (Section 9).

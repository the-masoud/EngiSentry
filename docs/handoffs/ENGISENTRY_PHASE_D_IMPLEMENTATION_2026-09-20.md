# EngiSentry Phase D Implementation Handoff — 2026-09-20

Explainable SUGGEST path: governed executable violation → deterministic candidate generation →
re-evaluation of every candidate against **all** bounded constraints → deterministic ranking → read-only
`PlacementProposal` (Phase A architecture G, H, I, J, L.4, M).

Scope: Equipment → Road, Equipment → Plant Fence, Equipment → Unit Boundary only. Point-based geometry only.

Accepted Phase C baseline HEAD: `57dd2c056360524cedbe50827ce67847d3a69587` (unchanged; no commit, no push,
no stage). The Phase C handoff `docs/handoffs/ENGISENTRY_PHASE_C_IMPLEMENTATION_2026-09-19.md` was
restored to that HEAD and left untouched.

## 1. Status

**PASS_CANDIDATE — build PASS (0 errors, 0 warnings), full test harness PASS (exit 0).**

The four Phase D components are complete, the 25 required test behaviors are covered, and no prohibited
scope was entered. Phase E is **not** started.

## 1b. Semantic corrections applied (2026-09-20, same day)

Three semantic defects were identified after the first PASS_CANDIDATE report and fixed. Each is a
behaviour correction inside existing Phase D files; no file was added or removed, and no contract,
measurement algorithm, unit conversion path, P6/P7/P8/P9, Phase C, `Commands.cs`, `VisualResultsPalette.cs`
or DWG was touched.

1. **Candidate displacement direction.** Phase A H.1 step 3 requires the *exact* unit vector from the recorded
   closest point toward the equipment point. The normal case was quantizing that vector to the 8-direction set
   (`E`/`NE`/`N`/…) *before* computing `ProposedEasting`/`ProposedNorthing`, so an oblique clearance direction
   was snapped to the nearest 45°. Fixed: normal-case geometry now uses the exact normalized vector
   (`ViolationContext.UnitDisplacement()`); the 8-direction `UnitVector` table is used **only** by the
   degenerate zero-length fallback; `DisplacementDirection` remains a human-readable approximate label and no
   longer drives geometry. A new test (`ObliqueDisplacementVector`) uses a 3-4-5 triangle whose exact unit
   vector is (0.6, 0.8) — the pre-fix quantized path produced 3535.53 instead of 3000.0 drawing units of X
   displacement — and asserts the exact components, that the vector is neither axis-aligned nor diagonal, and
   that the proposed point achieves measured compliance.
2. **Generator must not mutate input state.** `ViolationContext.CandidateSequence` was a mutable counter
   incremented by each `Generate` call, so calling `Generate` twice on the same unchanged context produced
   different candidate ids (and therefore a different proposal id). Fixed: the property is removed;
   candidate numbering is local to each `Generate` call and assigned to the already-ordered set
   (`AssignCandidateIds`), so numbering depends on input only. A new test (`SameContextDeterminism`) asserts
   identical candidate ids, identical proposed positions and displacement vectors, identical diagnostics and
   an identical proposal snapshot across repeated calls on the *same* context object.
3. **Ranker rejection consistency.** A candidate could be recorded in
   `PlacementProposalRankingSummary.RejectedCandidates` while also being the `WinningCandidate`, because
   rejection was inferred from `MandatoryViolationCount > 0` alone without dropping anything. Fixed: the
   ranker keeps Phase A J.1 ordering unchanged (mandatory violations first) and *drops* a candidate — moving
   it out of the ordered result and recording the reason — **only** when the ranked set contains a candidate
   with no mandatory violation, i.e. only in favour of a compliant alternative. When every candidate has a
   mandatory violation nothing is dropped: the least-violating candidate is presented with its violations
   visible, and a rejected candidate can never be the winner. A new test
   (`AllMandatoryViolationsRanking`) covers both branches and asserts the invariant that no candidate id
   appears in both the presented order and the rejected list. `PlacementProposalGenerator` now resolves the
   rejected candidate objects from the generated set rather than the ranked order, since dropped candidates
   are no longer present in the ranked order.

All previously passing tests continue to pass unchanged; the three new tests are additions.

An earlier in-session attempt implemented a narrower "EXPLAINABLE_SUGGEST" surface
(`ExplainableSuggestionBuilder` / `ExplainableSuggestionModels` / `ExplainableSuggestionTests`) whose scope
did not match the accepted Phase A architecture. That attempt was removed in full before this work began:
the four files were deleted and the two tracked test-project files were restored to `57dd2c0`. No trace of
it remains in the worktree.

## 2. Files created (exact)

| File | Role |
|---|---|
| `src\EngiSentry.Rules\PlacementProposalCandidateGenerator.cs` | Phase A H — deterministic candidate generation |
| `src\EngiSentry.Rules\PlacementProposalReEvaluator.cs` | Phase A I — re-evaluate every candidate against all bounded constraints |
| `src\EngiSentry.Rules\PlacementProposalRanker.cs` | Phase A J — deterministic, order-only ranking |
| `src\EngiSentry.Rules\PlacementProposalGenerator.cs` | Phase A G — compose the three into a read-only `PlacementProposal` |
| `tests\PlacementProposalTests.cs` | Phase D test coverage (25 required behaviors) |
| `docs\handoffs\ENGISENTRY_PHASE_D_IMPLEMENTATION_2026-09-20.md` | This handoff |

## 3. Files modified (exact)

| File | Change |
|---|---|
| `tests\EngiSentry.Tests.csproj` | Added `<Compile Include="PlacementProposalTests.cs" />` (classic non-SDK project with an explicit compile list; SDK globbing does not apply) |
| `tests\UnitTests.cs` | Added `PlacementProposalTests.Run();` to `Main`, immediately after `StandardsPackProjectionTests.Run();` |

No other tracked file was modified. `Commands.cs`, `VisualResultsPalette.cs`, the Results UI, `.vscode\`,
`artifacts\`, `packaging\installer\`, `docs\handoffs\P10.3-SUPERVISED-ENGINEER-PILOT-RECORD.md` and all
sample drawings were **not** touched. No DWG was modified. No new command was added.

## 4. Production types added (exact)

All four components live in `EngiSentry.Rules`. **No new contract type was created** — Phase D consumes the
accepted Phase B `PlacementProposal*` contracts verbatim (Phase A L.4 / M: no new serialized members).

`PlacementProposalCandidateGenerator`
- `PlacementProposalCandidateGenerator` (static) — `Generate(ViolationContext, out GenerationDiagnostics)`.
- `ViolationContext` (sealed) — the accepted evidence bundle: violation, governed rule, equipment
  coordinate, placement status, violating primitive, unit resolution. Exposes the derived
  `DisplacementDirection` / `IsDegenerateDirection`.
- `GenerationResult` (sealed), `GenerationDiagnostics` (sealed).

`PlacementProposalReEvaluator`
- `PlacementProposalReEvaluator` (static) — `ReEvaluate(candidate, ReEvaluationContext, out ReEvaluationDiagnostics)`.
- `PlacementProposalReEvaluator.SubjectGeometry` (sealed) — an accepted subject record paired with the
  primitive its geometry handle resolves to, plus the measured distance.
- `ReEvaluationContext` (sealed) — the bounded constraint set, unit resolution and plot geometry;
  `SubjectsFor(basisKey)` resolves subjects by handle and excludes unresolved ones.
- `ReEvaluationDiagnostics` (sealed) — per-state counts, none collapsed.

`PlacementProposalRanker`
- `PlacementProposalRanker` (static) — `Rank(candidates, out RankingDiagnostics)`.
- `RankingResult` (sealed), `RankingDiagnostics` (sealed).

`PlacementProposalGenerator`
- `PlacementProposalGenerator` (static) — `Build(ViolationContext, ReEvaluationContext, out ProposalDiagnostics)`.
- `ProposalDiagnostics` (sealed).

Test-only type: `EngiSentry.Tests.PlacementProposalTests` (not production).

## 5. Candidate generation (Phase A H)

Direction is the **exact unit vector from the closest point on the violating geometry toward the equipment
point**, taken from the already-recorded closest-point evidence (`EngineeringDistanceViolation.ClosestPointX/Y`)
and normalized without quantization (`ViolationContext.UnitDisplacement()`). No geometry is re-projected and
no direction is invented. `DisplacementDirection` is a human-readable approximate label only and never drives
normal-case geometry; the 8-direction set is used exclusively by the degenerate zero-length fallback.

Minimum displacement is `RequiredDistanceMeters − ActualDistanceMeters`, the numerical shortfall already
established by the accepted CHECK path. The v1 margin is `MarginMeters = 0`, so the first candidate is the
exact-minimum movement. Additional candidates are the base displacement plus
`AdditionalIncrementsMeters` (`1`, `2`, `5` m) along the same direction, so the candidate set always offers
one compliant option plus strictly larger clearances.

Unit conversion goes **only** through `DrawingUnitResolver.MetersToDrawingUnits` /
`DrawingUnitsToMeters`. No other conversion path exists in Phase D.

A degenerate direction (equipment point coincides with the recorded closest point) does not refuse: the
generator falls back to the deterministic 8-direction cardinal set
(`CardinalDirections = { E, N, NE, SE, S, W, NW, SW }`) and retains only cardinal candidates that achieve
compliance, rejecting the rest with a recorded reason.

There is **no** general search, no optimizer, no objective function, no relaxation, no random sampling and
no time-derived value anywhere in the generator. Candidate identity is
`"proposal-candidate:" + PlacementId + ":" + ConstraintId + ":" + sequence`, with a monotonic sequence.

Refusal conditions (Phase A H.3) — each returns a deterministic `refusal:<RuleId>:<reason>` string and zero
candidates, and the proposal carries `REVIEW_REQUIRED` rather than a fabricated position:
the rule is not `AutoExecutionAllowed`; the treatment is `REVIEW_REQUIRED`; the authority class is
`Quarantined` or `LegacyConflict`; the violation has no geometry evidence handle; the violation has no
measurement identity; the placement status is not `Complete`; the violating geometry is unresolved; the
equipment has no resolved coordinate evidence; or the recorded violation is no longer numerical
(`ActualDistanceMeters >= MinimumDistanceMeters`).

## 6. Re-evaluation (Phase A I)

Every candidate is re-evaluated against **all** bounded constraints supplied in the `ReEvaluationContext` —
Road, Plant Fence and Unit Boundary — including constraints that were not the generating one. All outcomes
are retained on `CheckedConstraints` in `ConstraintId` (Ordinal) order.

Distance mathematics is the accepted `PointToPolylineDistance.PointToPolyline` helper, which reproduces the
accepted `PlotSpatialMeasurementBuilder.BuildEngineeringSpatial` algorithm exactly: Closed-aware segment
iteration, projection parameter clamped to `[0,1]`, minimum with the lowest-distance-then-lowest-index
tie-break. **No second point-to-geometry algorithm exists in Phase D.** Selection follows the accepted
`EngineeringDistanceRuleEvaluator` rule — the governing subject per basis is the minimum-distance subject,
compared against the rule minimum — applied to the candidate point instead of the original placement point.

Hard invariants, all asserted by test:

- **INSUFFICIENT_EVIDENCE never becomes SATISFIED.** A basis with no resolved subject geometry is reported
  as `INSUFFICIENT_EVIDENCE` with zero resolved subjects, never as a pass. Subjects whose primitive cannot
  be resolved by handle are excluded and counted as absent evidence.
- **Governance is never changed by re-evaluation.** A constraint whose rule is not auto-executable, is
  quarantined, is a legacy conflict, or carries a `REVIEW_REQUIRED` treatment stays `REVIEW_REQUIRED`
  regardless of any measured distance.
- A basis outside the closed bounded taxonomy is `NOT_APPLICABLE`, never a pass.
- Re-evaluation creates no measurement, no evidence and no authority. It reads geometry that already exists
  and records the outcome on the candidate. Evidence handles on an outcome are a subset of the existing
  source handles.

## 7. Ranking (Phase A J)

Deterministic, **order-only** ranking of already-computed outcomes. Policy applied in order:

1. Ascending count of `VIOLATED` outcomes among constraints whose treatment is `HARD_CONSTRAINT` or
   `SAFETY_CONSTRAINT`. A candidate with a violated mandatory constraint can **never** outrank one without,
   regardless of any other attribute.
2. Ascending unresolved count (`INSUFFICIENT_EVIDENCE` + `REVIEW_REQUIRED`), carried as a first-class
   secondary attribute — penalized in ordering, never collapsed to zero and never hidden.
3. Ascending `DisplacementDistance` — the declared minimum-movement objective.
4. Tie-break: `DisplacementDistance`, `ProposedEasting`, `ProposedNorthing`, `GeneratingConstraintId`,
   `CandidateId`, with `StringComparer.Ordinal` for strings.

Ranking changes ordering only. It never modifies a measurement, an evidence handle, a governance state or a
constraint state — asserted by test by comparing `CheckedConstraints` before and after ranking. A candidate
that violates a mandatory constraint is dropped from the ordered result and recorded in
`PlacementProposalRankingSummary.RejectedCandidates` with a deterministic reason **only** when the ranked set
contains a candidate with no mandatory violation; when every candidate has a mandatory violation nothing is
dropped, so a rejected candidate can never be the winning candidate. An empty candidate set yields an empty
ranking with no winner, not an exception.

## 8. Proposal composition (Phase A G)

`PlacementProposalGenerator.Build` composes generation → re-evaluation → ranking and populates every
`PlacementProposal` field from accepted evidence:

- `ProposalId` is a content-derived SHA-256 over `PlacementId|ConstraintId|candidateIds` (candidate ids
  Ordinal-ordered before hashing, so input order cannot affect the id). Never random, never time-derived.
- Observed / required distance, measurement basis, applicability, measurement identity and unit context come
  straight from the violation and governed rule.
- Original and proposed positions, movement distance and movement direction come from the winning
  candidate; a refused generation reports the original position and zero movement.
- `RoadReEvaluation`, `PlantFenceReEvaluation`, `UnitBoundaryReEvaluation` hold the winning candidate's
  outcome per relationship.
- Constraint partitioning is by state: `SatisfiedConstraints`, `ViolatedConstraints`, and
  `InsufficientEvidenceConstraints` — the last never collapsed into satisfied.
- `ClauseEvidence` is copied field-by-field from the governed rule's own `StandardClauseAuthorityResolution`
  list. Nothing is invented and no source, page or hash is synthesized.
- The mandatory v1 lists are non-empty: `Assumptions`, `UncheckedConstraints` and `KnownLimitations`, the
  last carrying `ProposalKnownLimitations.PointBasedLimitation` verbatim.
- `EngineeringReviewRequired` is always `true` in v1, with the recorded `EngineeringReviewReason`.

A refused violation produces a proposal with the same review-required contract — no candidates, no
rejected candidates, no fabricated position — so the refusal path and the success path are
indistinguishable in shape.

## 9. Dependency direction result

**Preserved. No circular project reference, and no project reference added by Phase D.** Verified by test
(`PlacementProposalTests.ContractSafetyAndLayering`) using assembly reference graphs:

- `EngiSentry.Rules` references `EngiSentry.Contracts` and `EngiSentry.PlotPlan` — it does **not** reference
  `EngiSentry.Standards`.
- `EngiSentry.Rules` references no Autodesk / host assembly.

Phase D needs nothing from `EngiSentry.Standards`: it consumes the governed result at the contract level
(`StandardsPackRule`, `StandardClauseAuthorityResolution`) and the already-built geometry at the
`EngiSentry.PlotPlan` level. Every new contract type used (`PlacementProposal`,
`PlacementProposalCandidate`, `PlacementProposalConstraintOutcome`, `PlacementProposalRankingSummary`,
`PlacementProposalRejectedCandidate`, `ProposalAssumptions`, `ProposalUncheckedConstraints`,
`ProposalKnownLimitations`, `ProposalConstraintState`) is an accepted Phase B type; each was asserted to
expose no host-typed member.

## 10. Non-regression against Phase A M

- **4/4/4 authority:** Phase D reads authority state; it resolves none. `AutoExecutionAllowed`,
  `AuthorityClass`, `ConstraintTreatment` and `DecisionState` are consumed as already resolved by the
  accepted P6/P7/P8/P9 chain and are never recomputed, promoted or overridden.
- **0 auto-executable promotions:** a non-executable, quarantined, legacy-conflicting or review-required rule
  produces no candidate and a `REVIEW_REQUIRED` outcome. Approval grants no execution authority here, exactly
  as in Phase C.
- **No new serialized members:** no `[DataMember]` was added to any accepted contract. The only types Phase D
  adds are non-serialized engine types in `EngiSentry.Rules` and the test class.
- `EngineeringDistanceRuleEvaluator`, `BoundedCheckEvaluator`, `BoundedConstraintBuilder`, the governed
  projection and the whole P6/P7/P8/P9 governance chain were **not** modified. No new standards resolver was
  added. No standard, distance or clause was invented.

## 11. Explicitly out of scope (Phase E or later)

No coordinate-free suggestion text, no movement-vector optimization, no side or bearing reasoning beyond the
8-direction cardinal label, no alternative-placement search, no ranking of distinct placement strategies, no
pipe routing, no maintenance access, no process layout, no equipment footprint reasoning, no occupied-building
siting, no flare or hazardous-area separation, and no UI wiring. These are declared visibly to the engineer in
`ProposalUncheckedConstraints.Defaults` and `ProposalKnownLimitations.PointBasedLimitation` rather than
silently omitted.

## 12. Test coverage (25 required behaviors)

`tests\PlacementProposalTests.cs`, seven sections, all synthetic values held inside the class. No production
catalog, source, clause, approval, authority, hash or distance is created or altered, and no company standard
is named or remembered. No P6/P7/P8/P9 test duplication.

| # | Behavior | Asserted in |
|---|---|---|
| 1 | Deterministic candidate generation | `CandidateGeneration`, `Determinism` |
| 2 | Correct minimum displacement (15 − 10 = 5 m) | `CandidateGeneration` |
| 3 | Correct normal direction from recorded closest-point evidence | `CandidateGeneration` |
| 4 | Cardinal fallback on a degenerate direction | `RefusalConditions` |
| 5 | Refusal for a non-auto-executable rule | `RefusalConditions` |
| 6 | Refusal for unresolved geometry | `RefusalConditions` |
| 7 | Refusal for an incomplete placement | `RefusalConditions` |
| 8 | Refusal for an ambiguous placement | `RefusalConditions` |
| 9 | Road re-evaluation | `ReEvaluation` |
| 10 | Plant Fence re-evaluation | `ReEvaluation` |
| 11 | Unit Boundary re-evaluation | `ReEvaluation` |
| 12 | SATISFIED preserved, never downgraded | `ReEvaluation`, `ProposalComposition` |
| 13 | VIOLATED preserved | `ReEvaluation`, `ProposalComposition` |
| 14 | INSUFFICIENT_EVIDENCE preserved, never becomes SATISFIED | `ReEvaluation`, `ProposalComposition` |
| 15 | REVIEW_REQUIRED preserved — governance unchanged by re-evaluation | `ReEvaluation` |
| 16 | All bounded constraints re-evaluated, including non-generating | `ReEvaluation` |
| 17 | Mandatory violation never outranks a compliant candidate | `Ranking` |
| 18 | Unresolved states stay visible, never collapsed | `Ranking`, `ReEvaluation` |
| 19 | Minimum-movement ranking among compliant candidates | `Ranking` |
| 20 | Deterministic tie-breaking (constraint id then candidate id) | `Ranking` |
| 21 | Repeated identical input yields an identical proposal | `Determinism` |
| 22 | Reordered constraint input yields an identical proposal | `Determinism` |
| 23 | KnownLimitations / Assumptions / UncheckedConstraints present | `ContractSafetyAndLayering`, `ProposalComposition` |
| 24 | EngineeringReviewRequired == true on candidates and proposals | `ContractSafetyAndLayering`, `CandidateGeneration`, `ProposalComposition` |
| 25 | No DWG / Autodesk dependency, and no host-typed contract member | `ContractSafetyAndLayering` |

Additional coverage beyond the 25: quarantine refusal, review-required-treatment refusal, missing evidence
handle refusal, no-longer-numerical refusal, out-of-taxonomy `NOT_APPLICABLE`, ranking leaves outcomes
untouched, empty candidate set, evidence handles are a subset of existing source handles, incremental
candidates ordered by ascending displacement along a constant direction, candidate identity derived from
placement and constraint, the proposal id is a content-derived hex digest, and the three correction tests of
§1b — the exact oblique displacement vector, same-context repeated-generation determinism, and ranker
rejection consistency when every candidate has a mandatory violation.

## 13. Build and test evidence

- Build: `MSBuild.exe .\EngiSentry.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64 /warnaserror`
  → **0 errors, 0 warnings**, all 10 projects including `EngiSentry.AutoCAD2021` (.NET Framework 4.8, x64).
  Re-run after the §1b corrections with the same result.
- Tests: `.\tests\bin\x64\Release\EngiSentry.Tests.exe` → `All EngiSentry M2.2 through M5.0 tests passed.`
  **exit 0**. The entire pre-existing suite ran unchanged alongside the new Phase D class, including the
  three correction tests of §1b.

## 14. Worktree state at handoff

Tracked modifications (exactly two, both minimal test-harness registration):

```
 M tests/EngiSentry.Tests.csproj
 M tests/UnitTests.cs
```

Untracked new files (six):

```
src/EngiSentry.Rules/PlacementProposalCandidateGenerator.cs
src/EngiSentry.Rules/PlacementProposalGenerator.cs
src/EngiSentry.Rules/PlacementProposalRanker.cs
src/EngiSentry.Rules/PlacementProposalReEvaluator.cs
tests/PlacementProposalTests.cs
docs/handoffs/ENGISENTRY_PHASE_D_IMPLEMENTATION_2026-09-20.md
```

Protected untracked paths present and untouched: `.vscode\`, `artifacts\`,
`docs\handoffs\P10.3-SUPERVISED-ENGINEER-PILOT-RECORD.md`, `packaging\installer\`.

`git diff --check` — clean (no whitespace errors, no conflict markers). Nothing staged, nothing committed,
nothing pushed. `57dd2c0` remains the accepted Phase C baseline HEAD.

## 15. Verdict

**PHASE D IMPLEMENTATION: PASS_CANDIDATE**

All four components complete; the build passes with zero errors and zero warnings; the full test harness
passes with exit 0; and no prohibited scope was entered.

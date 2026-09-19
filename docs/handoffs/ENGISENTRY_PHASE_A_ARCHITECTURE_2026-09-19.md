# EngiSentry — Phase A: Architecture / Governed Contracts
## Standards-Based Plot Plan Check / Suggest Assistant

Date: 2026-09-19
Phase: **A — Architecture / Governed Contracts (documentation only)**
Predecessor: `docs/handoffs/ENGISENTRY_STANDARDS_ASSISTANT_AUDIT_2026-09-19.md` (accepted)
Baseline: `master` @ `eb6efbc58825592f95cd1eb2b7b3372106ca8364` + working tree (state recorded, not modified)

**Phase A produces this document only.** No implementation files, no tests, no DWG, no git mutation are
created or authorized by this phase. Every statement about existing code is backed by repository evidence
(file / type / field). Future file names in Section L are *proposed* targets for Phase B/C/D approval.

Bounded product scope for this entire architecture: **Equipment + Road + Plant Fence + Unit Boundary**.

---

# A. Architectural boundary

## A.1 Verified dependency facts

Repository evidence (verified by project-reference inspection and `grep -rl Autodesk`):

```
src/EngiSentry.AutoCAD2021  → Contracts, Semantics, Core, Rules, PlotPlan, Standards, CrossDocument, Revision
src/EngiSentry.Core         → Contracts, Semantics
src/EngiSentry.CrossDocument→ Contracts
src/EngiSentry.PlotPlan     → Contracts
src/EngiSentry.Revision     → Contracts
src/EngiSentry.Rules        → Contracts, PlotPlan
src/EngiSentry.Semantics    → Contracts
src/EngiSentry.Standards    → Contracts
```

`grep -rl "Autodesk" src/EngiSentry.Contracts src/EngiSentry.Core src/EngiSentry.Semantics
src/EngiSentry.Rules src/EngiSentry.PlotPlan src/EngiSentry.Standards src/EngiSentry.CrossDocument
src/EngiSentry.Revision` → **no matches** (exit 1). The only project referencing Autodesk is
`EngiSentry.AutoCAD2021`.

**Conclusion: the host-independent design-engine boundary already exists and needs no new project.**
`AGENTS.md` states the intent directly: "Keep architecture ready for a future .NET 10 engine separated from
the AutoCAD process." All new design-engine types therefore go into the *existing* projects, not a new one.

## A.2 Concept placement

| New concept | Intended project | Intended namespace | Existing dependencies | Reusable components | Direction of dependency |
|---|---|---|---|---|---|
| PlantModel v1 + facility taxonomy contracts | `EngiSentry.Contracts` | `EngiSentry.Contracts` | none (leaf) | `PlotPlanModel`, `EngineeringSpatialSubject`, `EquipmentPlacement`, `PlotGeometryPrimitive`, `EngineeringDistanceEvidenceKeys` | depended **upon** by PlotPlan, Rules, Standards, AutoCAD2021 |
| Constraint treatment + proposal states contracts | `EngiSentry.Contracts` | `EngiSentry.Contracts` | none (leaf) | `StandardDecisionStates`, `StandardAuthorityClasses`, `StandardEnforcementModes` | depended upon by Rules, Standards |
| Spatial proposal candidate contracts | `EngiSentry.Contracts` | `EngiSentry.Contracts` | none (leaf) | `EngineeringDistanceViolation`, `EquipmentSpatialMeasurement`, `EngineeringDistanceRuleDefinition` | depended upon by Rules, Standards, AutoCAD2021 |
| StandardsPack governed projection contracts | `EngiSentry.Contracts` | `EngiSentry.Contracts` | none (leaf) | `EffectiveStandardRuleDefinition`, `StandardClauseAuthorityResolution`, `StandardRuleExecutionGateDecision`, `StandardSourceDefinition`, `StandardClauseDefinition` | depended upon by Standards, Rules |
| Facility element builder (PlantModel assembly) | `EngiSentry.PlotPlan` | `EngiSentry.PlotPlan` | `Contracts` | `PlotPlanModel`, `EngineeringSpatialSubjectBuilder`, `PlotGeometryBuilder` output | depends on Contracts only |
| Pure point-to-polyline distance helper | `EngiSentry.PlotPlan` | `EngiSentry.PlotPlan` | `Contracts` | algorithm currently private in `PlotSpatialMeasurementBuilder.BuildEngineeringSpatial` | depends on Contracts only; depended upon by Rules |
| Candidate generator, re-evaluator, ranker | `EngiSentry.Rules` | `EngiSentry.Rules` | `Contracts`, `PlotPlan` | `EngineeringDistanceRuleEvaluator`, `EngineeringDistanceRuleRegistry`, `EngineeringDistanceViolation`, `DrawingUnitResolver` | depends on Contracts + PlotPlan; **not** referenced by PlotPlan |
| StandardsPack projection service | `EngiSentry.Standards` | `EngiSentry.Standards` | `Contracts` | `PlotPlanStandardsAuthorityRuntimeContextCatalog`, `StandardRulePackResolver`, `StandardAuthorityResolver`, `StandardAuthorityExecutionGate`, `StandardSourceRegistry`, `StandardClauseRegistry` | depends on Contracts only |
| Read-only proposal projection (UI) | `EngiSentry.AutoCAD2021` | `EngiSentry.AutoCAD2021` | all of the above | `VisualResultsPalette`, `PlotPlanDistancePresentation`, `PlotPlanReportCompactor`, `SnapshotFileWriter` | terminal; the only project allowed to touch Autodesk |

## A.3 Hard prohibition

The following **must not** appear in `EngiSentry.Contracts`, `EngiSentry.Core`, `EngiSentry.Semantics`,
`EngiSentry.Rules`, `EngiSentry.PlotPlan`, `EngiSentry.Standards`, `EngiSentry.CrossDocument`, or
`EngiSentry.Revision`:

- any `using Autodesk.*` namespace, or any type from `Autodesk.AutoCAD.*` (currently: zero occurrences);
- any `Document`, `Database`, `Transaction`, `ObjectId`, `Handle`, `Point3d`, `Matrix3d`, or other host type;
- any file-system path resolution, `%LOCALAPPDATA%` logic, or UI class.

AutoCAD source/entity identity enters the design engine **only** as the existing portable string identifiers
already used throughout the contracts: `SourceHandle` (e.g. `PlotGeometryPrimitive.SourceHandle`,
`EquipmentPlacement.TagSourceHandle`), `SourceLayer`, `GeometryKind`, and `LabelSourceHandle`. These are plain
`string` values with no host coupling — the same identity scheme is already serialized into the accepted
PlotQA JSON and consumed headlessly by the test harness.

**Boundary enforcement mechanism (architectural, no code yet):** the proposal candidate contract carries
*only* these portable string evidence identifiers plus numeric geometry. It never carries a host entity. This
is the same discipline `PlotGeometryCapture` already uses: `DrawingScanner` (host) produces a pure
`[DataContract]` snapshot, and everything downstream is host-free.

---

# B. PlantModel v1

## B.1 Design principle — projection, not duplication

The audit established that `PlotPlanModel` (`PlotPlanModels.cs:7`) already holds everything the bounded use
case needs: `Drawing`, `EquipmentPlacements`, `Geometry` (with `Primitives`, `Associations`,
`SpatialConstraints`, `EngineeringSpatialSubjects`), `Measurements`, and `Statistics`. `PlotPlanBuilder.Build`
already assembles it, and `QaEngine`, `RevisionEngine`, `PlotPlanReportCompactor`, and every existing test
depend on that exact type.

**PlantModel v1 is therefore defined as a read-only *projection* over `PlotPlanModel`, not a parallel model.**
This satisfies "Do not duplicate existing concepts merely to rename them." No existing field is moved,
renamed, or re-typed.

## B.2 `PlantModelV1`

| Aspect | Definition |
|---|---|
| Purpose | Host-free, facility-oriented read-only view of one Plot Plan for the bounded use case; the single input to constraint evaluation and proposal generation |
| Container | Wraps an existing `PlotPlanModel` by reference; exposes typed facility elements and unit context |
| Fields | `PlotPlanModel Source` (existing type, by reference); `IReadOnlyList<FacilityElement> FacilityElements`; `PlantModelDiagnostics Diagnostics` |
| Source of data | `PlotPlanBuilder.Build(index, model, snapshot, geometryCapture)` — unchanged |
| Confidence/evidence requirement | Every `FacilityElement` carries its own classification confidence and evidence references; the model never synthesizes a facility element without an existing resolved subject or placement |
| Reusable existing type | `PlotPlanModel` (root), `EngineeringSpatialSubject` (Road/Fence/UnitBoundary actors), `EquipmentPlacement` (equipment), `PlotGeometryPrimitive` (geometry) |
| New code eventually required | Yes — one builder (`FacilityElementBuilder`) that reads only existing fields |

**Explicit non-goals of PlantModel v1** (per the bounded scope and the evidence model): plot/property
boundary, site north, equipment footprint, orientation, maintenance envelope, process layout, pipe racks,
buildings, substations, tank farms, gates, drainage, fire/flare/escape facilities, underground utilities,
wind/topography/elevation. None exist in the current extraction and none are invented.

## B.3 `FacilityElement`

| Aspect | Definition |
|---|---|
| Purpose | One facility actor in the bounded domain: an Equipment, a Road, a Plant Fence, or a Unit Boundary |
| Fields | `string ElementId` (deterministic, e.g. `facility-element:equipment:placement:<PlacementId>` / `facility-element:road:<SubjectId>`); `string FacilityElementKind` (see C); `string SourceHandle`; `string SourceLayer`; `string Classification`; `string ClassificationConfidence`; `string ClassificationBasis`; `IReadOnlyList<string> EvidenceHandles`; `double? PositionX/PositionY` (equipment point only); `string? GeometryKind` (Road/Fence/UnitBoundary only); `IReadOnlyList<PlotGeometryVertex>? Vertices` (Road/Fence/UnitBoundary only); `string? EngineeringMeasurementBasisKey`; `string? EngineeringApplicabilityKey` |
| Source of data | Equipment: `EquipmentPlacement` (PlacementId, EquipmentEntityId, NormalizedTag, TagSourceHandle/Layer, Easting/Northing selected values, PlacementStatus, EngineeringApplicabilityKey). Road/Fence/UnitBoundary: `EngineeringSpatialSubject` (SubjectId, SubjectKind, EngineeringMeasurementBasisKey, GeometrySourceHandle/Layer/Kind, LabelSourceHandles) + its `PlotGeometryPrimitive` vertices |
| Confidence/evidence requirement | Confidence and basis are **derived from existing statuses**, never invented (see B.4). An unresolved placement or association produces a `FacilityElement` with `ClassificationConfidence = NONE` rather than being dropped, so the limitation is visible |
| Reusable existing type | Composed from existing `EquipmentPlacement` / `EngineeringSpatialSubject` / `PlotGeometryPrimitive`; no new geometry type |
| New code eventually required | Yes — the builder only |

## B.4 Classification confidence — no invented precision

The audit established that **no numeric confidence field exists anywhere** in the codebase; confidence today is
the tri-state `Resolved` / `Missing` / `Ambiguous` on `CoordinateAssociation`, `GeometryAssociation`, and
`SpatialCoordinateConstraint`, plus `PlacementStatus` (Incomplete/Complete/Ambiguous) and `ParseStatus`
(Parsed/Partial).

`ClassificationConfidence` is therefore a small deterministic ordinal that **maps 1:1 onto existing statuses**
and is always accompanied by a human-readable `ClassificationBasis` stating why:

| Confidence | Produced when | Basis string records |
|---|---|---|
| `HIGH` | Equipment: `PlacementStatus == "Complete"` with both E and N `Resolved`. Road/Fence/UnitBoundary: `EngineeringSpatialSubject` resolved with exactly one primitive (the existing `ResolvePrimitive` invariant) | the existing status values and source handles |
| `MEDIUM` | Classification accepted under a deterministic heuristic (e.g. cross-tier layer match in `PlotGeometryBuilder.EvidenceTier`) | the tier and the ambiguity margin outcome |
| `LOW` | `PlacementStatus == "Ambiguous"` or association `Status == "Ambiguous"` | count of retained candidates |
| `NONE` | `PlacementStatus == "Incomplete"` or association `Status == "Missing"` | the existing `Reason` string |

Rationale: this adds a *vocabulary* for confidence without adding a *numeric* claim that could be mistaken for
a calibrated probability. It is a strict function of existing evidence states, so it is deterministic and
testable against existing fixtures.

## B.5 `PlantModelDiagnostics`

| Aspect | Definition |
|---|---|
| Purpose | Deterministic counts that make extraction quality visible in the read-only result, mirroring the existing `PlotGeometryStatistics` / `PlotPlanStatistics` style |
| Fields | counts of facility elements per kind; `High/Medium/Low/None` confidence counts; equipment with unresolved coordinates; road/fence/unit-boundary subjects with unresolved geometry; total resolved measurement evidence (from existing `PlotSpatialMeasurementStatistics`) |
| Source of data | Existing `PlotPlanStatistics`, `PlotGeometryStatistics`, `PlotSpatialMeasurementStatistics` — no recomputation |
| New code eventually required | Yes — counts only |

---

# C. Facility taxonomy

## C.1 Canonical kinds

`FacilityElementKind` — a `static class` of `const string`, following the exact style of the existing
`EngineeringDistanceEvidenceKeys`, `StandardDecisionStates`, `StandardAuthorityClasses`, and
`StandardEnforcementModes` (all `const string`, compared with `StringComparer.Ordinal`):

| Canonical kind | Constant value | Existing source of truth |
|---|---|---|
| Equipment | `Equipment` | `EngineeringDistanceEvidenceKeys.Equipment`; `EquipmentPlacement.EngineeringApplicabilityKey` |
| Road | `Road` | `EngineeringSpatialSubject.SubjectKind == "Road"` with basis `AccessRoadEdge` or `UnitPeripheralRoadEdge` |
| Plant Fence | `PlantFence` | `EngineeringSpatialSubject.SubjectKind == "Boundary"` with basis `SecurityFence` (from `BoundaryEvidence.BoundaryType == "Fence"`) |
| Unit Boundary | `UnitBoundary` | `EngineeringSpatialSubject.SubjectKind == "Boundary"` with basis `UnitBoundary` (from `BoundaryEvidence.BoundaryType == "UnitLimit"`) |

`SiteBoundary` exists in `EngineeringDistanceEvidenceKeys` but is **out of the bounded scope** and is not a
PlantModel v1 kind. `BatteryLimit` boundary text is parsed by `SemanticExtractor.ParseBoundary` but has no
`EngineeringDistanceEvidenceKeys` basis and is therefore not a PlantModel v1 kind either. Both exclusions are
stated here so the omission is deliberate and visible, not accidental.

## C.2 Classification rules (deterministic, evidence-bound)

| Kind | Evidence required | Rule (all existing) |
|---|---|---|
| Equipment | A `DrawingTag` occurrence that parses via `EquipmentTagGrammar.TryParse`, is not `BLOCKNAME` metadata, and resolves to an `EquipmentPlacement` | `SemanticExtractor.ClassifyTag` (returns Equipment / Structure / `Ambiguous`); `PlotPlanBuilder` placement assembly |
| Road | Model-space text matching the closed allow-list in `EngineeringSpatialSubjectBuilder.IsExplicitRoadEdge` ("ACCESS ROAD EDGE", "ROAD EDGE", "EDGE OF ROAD", "EDGE OF ACCESS ROAD"), plus a `Resolved` `GeometryAssociation` | `EngineeringSpatialSubjectBuilder.Build` |
| Plant Fence | `BoundaryEvidence.BoundaryType == "Fence"` plus a `Resolved` `GeometryAssociation` | `SemanticExtractor.ParseBoundary` (`FenceToken`) → `EngineeringSpatialSubjectBuilder.Build` |
| Unit Boundary | `BoundaryEvidence.BoundaryType == "UnitLimit"` plus a `Resolved` `GeometryAssociation` | `SemanticExtractor.ParseBoundary` (`UnitLimitToken`) → `EngineeringSpatialSubjectBuilder.Build` |

**No classification is silently guessed.** Specifically:
- A road label containing the word "ROAD" but not an explicit edge phrase is **not** classified as a Road
  facility element — `IsExplicitRoadEdge` is a closed allow-list and must not be loosened.
- A `BoundaryType == "BatteryLimit"` or `== "Boundary"` (generic) evidence is **not** a Plant Fence or Unit
  Boundary because it carries no engineering measurement basis key.
- `ClassifyTag == Ambiguous` equipment yields `ClassificationConfidence = LOW`, never a forced Equipment
  classification.
- Geometry with `bulge != 0` is dropped by `DrawingScanner.Geometry()` and therefore cannot underpin any
  facility element; the resulting missing association surfaces as `NONE` confidence.

## C.3 Unresolved / ambiguous handling

Every classification failure path maps to an existing, already-accepted reporting state rather than a guess:

| Failure | Result |
|---|---|
| No resolved E/N for a tag | FacilityElement emitted with `ClassificationConfidence = NONE`; existing rule `PP-COORD-001` (IncompletePlacementRule) already reports it |
| Ambiguous E/N | `LOW`; existing rule `PP-COORD-002` (AmbiguousPlacementRule) reports it |
| Label present but no resolved geometry | `NONE`; existing rule `PP-GEO-001` (MissingGeometryRule) reports it |
| Multiple geometry candidates within margin | `LOW`; existing rule `PP-GEO-002` (AmbiguousGeometryRule) reports it |
| No eligible subject for a required basis | constraint result `INSUFFICIENT_EVIDENCE` (see G) |

This means the taxonomy **adds no new silent classification** and every degradation is already observable in
the accepted Plot Plan QA output.

---

# D. StandardsPack contract

## D.1 Design principle — a governed projection, not a second authority system

Per the governing design rules and the audit (Section E), the new StandardsPack **must not** bypass the
P6→P9 trust/governance foundation. It is therefore defined as a **read-only projection** composed entirely of
existing authoritative types, produced by a service that consumes the existing authority chain:

```
PlotPlanStandardsAuthorityRuntimeContextCatalog.Create()        [existing, authoritative]
   → StandardSourceRegistry / StandardClauseRegistry            [existing, authoritative]
   → StandardRulePackRegistry → StandardRulePackResolver        [existing, authoritative]
   → StandardAuthorityResolver → StandardAuthorityExecutionGate [existing, authoritative]
   → StandardsPackProjectionBuilder.Build(...)                   [NEW: projection only]
        → IReadOnlyList<StandardsPackRule>                      [NEW: read-only view]
```

The projection **never** creates a source, clause, policy, approval, or trust decision. It only reads them.
Authority, executability, and currentness continue to be decided by the existing chain exactly as they are
today (authority/gate/review 4/4/4, auto-executable 0, flare `SOURCE_QUARANTINED`).

## D.2 `StandardsPackRule` — field-by-field mapping to authoritative types

| Required field (handoff §6) | Type on `StandardsPackRule` | Authoritative existing type — remains the source of truth |
|---|---|---|
| `RuleId` | `string` | `EffectiveStandardRuleDefinition.RuleId` (`StandardRulePackResolutionModels.cs:17`); identity validated by `EngineeringDistanceRuleRegistry.Create` |
| `Company / Pack` | `string Company`, `string PackId`, `string PackName` | `StandardRulePackDefinition.PackId` + `.Name` (`StandardRulePackModels.cs:20`). `Company` is **new** — the existing pack contract has no company attribute; it is metadata only, carries no authority, and defaults to `string.Empty` |
| `Document` | `string DocumentNumber`, `string DocumentTitle` | `StandardSourceDefinition.DocumentNumber` + `.Title` (`StandardSourceModels.cs:6`) |
| `Revision` | `string RevisionOrEdition` | `StandardSourceDefinition.RevisionOrEdition` |
| `Clause` | `string ClauseId`, `string ClauseReference` | `StandardClauseDefinition.ClauseId` + `.ClauseReference` (`StandardClauseModels.cs:6`) |
| `Page` | `int? PageNumber` | `StandardClauseDefinition.PageNumber` |
| `Requirement text` | `string RequirementText` | `StandardClauseDefinition.ClauseText` |
| `SubjectType` | `string SubjectType` | maps onto `EngineeringDistanceEvidenceKeys` applicability (`Equipment` / `ProcessingEquipment` / `SpecialHazardousUnit`); validated by `IsSupportedApplicabilityKey` |
| `ReferenceType` | `string ReferenceType` | maps onto `EngineeringDistanceEvidenceKeys` measurement basis (`AccessRoadEdge`, `UnitPeripheralRoadEdge`, `SecurityFence`, `UnitBoundary`, `SiteBoundary`); validated by `IsSupportedMeasurementBasisKey` |
| `MeasurementBasis` | `string MeasurementBasisKey` | `EngineeringDistanceRuleDefinition.RequiredMeasurementBasisKey` (`EngineeringDistanceRuleModels.cs:18`) |
| `criterion / MinimumDistance` | `decimal MinimumDistanceMeters` | `EngineeringDistanceRuleDefinition.MinimumDistanceMeters`; for governed candidates `EngineeringDistanceCandidateDefinition.ProposedMinimumDistanceMeters`, executable only through `EngineeringDistanceApprovalGate.TryCreateExecutableRule` |
| `Applicability` | `string ApplicabilityKey` | `EngineeringDistanceRuleDefinition.RequiredApplicabilityKey` |
| `Exceptions` | `IReadOnlyList<string> Exceptions` | **NEW** — no existing type carries exceptions. Defaults to empty. Populated only from authorized pack metadata; never inferred from clause text |
| `AuthorityLevel` | `string AuthorityClass` | `StandardAuthorityPolicyDefinition.AuthorityClass` (`StandardAuthorityModels.cs:36`) — one of the 6 existing `StandardAuthorityClasses` |
| `EngineeringReviewRequired` | `bool EngineeringReviewRequired` | **derived**, not asserted: `true` unless `StandardRuleExecutionGateDecision.AutoExecutionAllowed == true` **and** `DecisionState` is null/`PASS`. Today this yields `true` for all 4 runtime rules, consistent with auto-executable = 0 |
| `ExecutableStatus` | `string EffectiveEnforcementMode`, `bool AutoExecutionAllowed`, `string? DecisionState` | `StandardRuleExecutionGateDecision` (`StandardAuthorityExecutionModels.cs:7`) — authoritative; `AutoExecutionAllowed` is `mode == DETERMINISTIC` per `StandardAuthorityExecutionGate.Evaluate` |
| `Evidence` | `IReadOnlyList<StandardClauseAuthorityResolution> Clauses` | `StandardRuleAuthorityResolution.Clauses` → `StandardClauseAuthorityResolution` (`StandardAuthorityResolutionModels.cs:7`, 17 fields incl. source document/title/issuer/revision/filename/SHA256, clause reference/text/heading/page) |
| `SourceHash` | `string? SourceSha256` | `StandardSourceDefinition.SourceSha256` and `StandardClauseAuthorityResolution.SourceSha256` |
| `Currentness / Staleness` | `string? CurrentnessState`, `IReadOnlyList<string> CurrentnessReasons` | `EngineeringDistanceApprovalCurrentnessStates` (`CURRENT`/`STALE`) + `EngineeringDistanceApprovalCurrentnessReasons` (4 reasons), produced by `EngineeringDistanceApprovalCurrentnessValidator.Evaluate`; review-side staleness via `HumanEngineeringReviewDecisionStalenessValidator` |

Every required field is therefore either an existing authoritative type read verbatim, an existing value
derived by an existing rule, or one of exactly **three** new metadata fields (`Company`, `Exceptions`,
`EngineeringReviewRequired` as a projected boolean) that carry no authority of their own.

## D.3 `SOURCE_REQUIRED` semantics

An unavailable or unauthorized standard is **not** a rule. Concretely:

- A `StandardsPackRule` cannot exist without an `EffectiveStandardRuleDefinition` — `EngineeringDistanceRuleRegistry.Create` throws `"references unknown effective RuleId"` otherwise (`EngineeringDistanceRuleRegistry.cs:44`).
- A governed candidate that is `PENDING` / `REJECTED` / `REFERENCE_ONLY` produces **no** executable rule — `EngineeringDistanceApprovalGate.TryCreateExecutableRule` returns `false` (`EngineeringDistanceApprovalGate.cs:23-27`).
- A quarantined or non-deterministic rule carries `AutoExecutionAllowed = false` and `DecisionState = SOURCE_QUARANTINED` / `EXPERT_REVIEW_REQUIRED`, so it is surfaced as `REVIEW_REQUIRED` in the proposal layer and never evaluated as a real engineering criterion.
- `SOURCE_REQUIRED` is expressed through the existing vocabulary: `StandardDecisionStates.ExpertReviewRequired` / `SourceQuarantined`, plus `StandardEnforcementModes.NeverAutoEnforce`. No new `SOURCE_REQUIRED` string constant is introduced, avoiding a second parallel vocabulary.

**No rule value, distance, clause, or page may be populated from model memory.** The projection builder reads
only registered sources/clauses; an empty registry yields an empty pack, which yields `INSUFFICIENT_EVIDENCE`
downstream — never a fabricated criterion.

---

# E. Constraint contract

## E.1 Three-layer separation

The architecture separates three concerns that the handoff requires to stay distinct:

```
1. Governed source rule      — what an authorized document says (authority-bearing)
2. Constraint treatment      — how the design engine must treat it (never authority-bearing)
3. Evaluation outcome        — what the evidence proves (state + evidence)
```

**Authority and optimization priority are never conflated:** treatment is a property recorded on the
constraint, while executability remains exclusively a property of the authority gate. A rule labelled
`OPTIMIZATION_OBJECTIVE` cannot become executable by relabelling, and a `HARD_CONSTRAINT` cannot gain
authority by relabelling — authority comes only from `StandardAuthorityExecutionGate`.

## E.2 `ConstraintTreatment` — layer 2

A `static class` of `const string` in the existing house style, matching the handoff list exactly:

| Treatment | Meaning | Relationship to authority |
|---|---|---|
| `HARD_CONSTRAINT` | Must hold; a violation blocks any candidate from being presented as compliant | Requires `AutoExecutionAllowed == true` to be *evaluated as a hard constraint*; otherwise it degrades to `REVIEW_REQUIRED` |
| `SAFETY_CONSTRAINT` | Must hold and is safety-relevant; always carries `EngineeringReviewRequired = true` | Same requirement; authority-independent |
| `PROJECT_REQUIREMENT` | Approved project/client criterion | Maps to `StandardAuthorityClasses.GoverningProject` / `PROJECT_RULE_ONLY` (`ProjectDistanceGovernedRuleResolutionStates.ProjectRuleOnly`) |
| `ENGINEERING_PREFERENCE` | Preferred but not mandatory | Never blocks a candidate; recorded in assumptions |
| `OPTIMIZATION_OBJECTIVE` | Declared objective for ranking (e.g. minimum movement) | **Cannot override a violated mandatory constraint** (see J) |
| `REVIEW_REQUIRED` | Explicitly deferred to human engineering review | Never evaluated to PASS/FAIL |

Treatment is recorded at pack-metadata time and is part of the projection; it is never derived from clause
text by inference.

## E.3 Evaluation outcome states — layer 3, mapped onto existing semantics

The five required states map onto the existing accepted vocabulary rather than replacing it:

| Proposal-layer state | Existing authoritative state | Existing type / constant |
|---|---|---|
| `SATISFIED` | `PASS` | `StandardDecisionStates.Pass`; produced today by `EngineeringDistanceRuleEvaluator` when `ViolationCount == 0 && EvaluatedSubjectCount > 0` |
| `VIOLATED` | `FAIL` / `VIOLATIONS` | `StandardDecisionStates.Fail`; `EngineeringDistanceRuleEvaluation.Status == "VIOLATIONS"`; `QaRuleResult.Status == "FINDINGS"` via `EngineeringDistanceQaIntegrator.QaStatus` |
| `INSUFFICIENT_EVIDENCE` | `INSUFFICIENT_EVIDENCE` | `StandardDecisionStates.InsufficientEvidence`; produced when `governing.Count == 0` |
| `NOT_APPLICABLE` | `NOT_APPLICABLE` | `StandardDecisionStates.NotApplicable`; `PlotPlanRuleBase.NotApplicable()` |
| `REVIEW_REQUIRED` | `EXPERT_REVIEW_REQUIRED` / `SOURCE_QUARANTINED` / `STANDARD_CONFLICT` | `StandardDecisionStates.ExpertReviewRequired` / `.SourceQuarantined` / `.StandardConflict`; also `StandardEnforcementModes.NeverAutoEnforce` |

**Critical non-regression rule (from audit H.2):** `EngineeringDistanceQaIntegrator.ValidateStatus` enforces
strict invariants on `EngineeringDistanceRuleEvaluation.Status` (`PASS ⇒ subjects > 0 && violations == 0`,
`VIOLATIONS ⇒ both > 0`, `INSUFFICIENT_EVIDENCE ⇒ both == 0`) and `ValidateViolations` requires unique
PlacementId per rule and exact basis/applicability match.

**Therefore the proposal-layer states live on NEW proposal types only.** They must never be written into
`EngineeringDistanceRuleEvaluation.Status`, `QaRuleResult.Status`, or `QaReport.OverallStatus`, which continue
to carry exactly the accepted `PASS` / `FINDINGS` / `INSUFFICIENT_EVIDENCE` / `REVIEW_REQUIRED` / `FAIL` set.
The mapping in the table above is applied only when *projecting* a proposal result into a read-only view.

## E.4 `BoundedConstraint`

| Aspect | Definition |
|---|---|
| Purpose | One governed constraint as evaluated against PlantModel v1 — the evaluation unit for CHECK and the guard unit for SUGGEST |
| Fields | `string ConstraintId` (deterministic); `StandardsPackRule` (governed source, by reference); `string ConstraintTreatment`; `string MeasurementKind` (existing 2 values only); `string RequiredMeasurementBasisKey`; `string RequiredApplicabilityKey`; `decimal MinimumDistanceMeters`; `string? UnitContext` |
| Source of data | `EngineeringDistanceRuleDefinition` + `StandardsPackRule` projection |
| Confidence/evidence | Treatment requires `AutoExecutionAllowed` or explicit `REVIEW_REQUIRED`; otherwise the constraint is not evaluated |
| Reusable existing type | `EngineeringDistanceRuleDefinition`, `EngineeringDistanceRuleEvaluation`, `EngineeringDistanceViolation` |
| New code eventually required | Yes — composition only |

## E.5 Preservation of governance outcomes inside proposals

Where any of the following governance outcomes applies to a constraint, the proposal layer **retains** it
verbatim rather than converting it to a distance verdict:

- `SOURCE_QUARANTINED` (flare terms) → proposal state `REVIEW_REQUIRED` with the quarantine reason recorded; never a distance comparison.
- `LEGACY_CONFLICT` (IPS E-PR-190) → `REVIEW_REQUIRED`; never auto-enforced.
- `EXPERT_REVIEW_REQUIRED` → `REVIEW_REQUIRED`.
- `STANDARD_CONFLICT` → `REVIEW_REQUIRED` with both conflicting citations retained (per `STANDARDS_AUTHORITY_MATRIX.md:51`: "If two applicable requirements conflict, EngiSentry records both").
- `PROJECT_RULE_ONLY` (Road, Fence today) → evaluated as a project criterion and **labelled** as such, exactly as `GovernanceDisplay` already does in `VisualResultsPalette`.

---

# F. Spatial proposal candidate contract

## F.1 Terminology decision — the audit naming finding is preserved

`EngineeringDistanceCandidateDefinition` (`EngineeringDistanceCandidateModels.cs:7`) already means a
**candidate engineering criterion/rule** — a proposed minimum distance awaiting engineering approval
(`ApprovalStatus == "PENDING_ENGINEERING_APPROVAL"`, `ProposedMinimumDistanceMeters`). It is referenced by
`EngineeringDistanceCandidateCatalog`, `ProjectDistanceRuleBinder`, `ProjectEngineeringDistanceRuntime`,
`ProjectDistanceGovernanceRuntimeLoader`, `EngineeringDistanceApprovalGate`, and the P8/P9 approval chain.

**It MUST NOT be reused for proposed equipment positions.** The distinct name chosen for spatial proposal
candidates is:

> ## `PlacementProposalCandidate`

Rationale: `Placement` is the established domain term for an equipment location in this codebase
(`EquipmentPlacement`, `PlacementId`, `placement:<CanonicalId>:<handle>`), and `Proposal` makes the
read-only, non-executing nature explicit. The collision with the P9 candidate-criteria vocabulary is
structurally impossible.

## F.2 `PlacementProposalCandidate`

| Aspect | Definition |
|---|---|
| Purpose | One deterministic proposed point position for one equipment placement, with its full re-evaluated constraint result set |
| Identity | `string CandidateId` — deterministic, e.g. `proposal-candidate:<PlacementId>:<sequence>`; no random ID, no `DateTime.UtcNow` (per the determinism discipline in `StandardAuthorityReviewPackageBuilder`) |
| Fields | `string CandidateId`; `string PlacementId`; `string EquipmentEntityId`; `string NormalizedTag`; `double OriginalEasting / OriginalNorthing` (existing resolved point); `double ProposedEasting / ProposedNorthing`; `double DisplacementX / DisplacementY`; `double DisplacementDistance`; `string DisplacementDirection` (deterministic label, e.g. `E`, `NE`, `N`); `string GeneratingConstraintId`; `string GeneratingRuleId`; `IReadOnlyList<PlacementProposalConstraintOutcome> CheckedConstraints`; `IReadOnlyList<string> UncheckedConstraints`; `IReadOnlyList<string> Assumptions`; `IReadOnlyList<string> RejectedReasons`; `bool EngineeringReviewRequired` |
| Source of data | one `EngineeringDistanceViolation` (governing) + existing `EngineeringSpatialSubject` geometry + `EngineeringDistanceRuleDefinition` minima |
| Confidence/evidence | Every numeric field derives from existing measured evidence; no field is estimated beyond the declared displacement model; `EngineeringReviewRequired` is always `true` in v1 |
| Reusable existing type | `EngineeringDistanceViolation` (origin), `EquipmentSpatialMeasurement` (measurement shape), `PlotGeometryPrimitive` (geometry) |
| New code eventually required | Yes — contract + generator |
| Host coupling | **None.** Only portable string evidence identifiers (`LabelSourceHandle`, `GeometrySourceHandle`, `GeometrySourceLayer`, `GeometryKind`) and numeric values. No AutoCAD type |

## F.3 `PlacementProposalConstraintOutcome`

| Aspect | Definition |
|---|---|
| Purpose | The re-evaluated result of one bounded constraint at one candidate position |
| Fields | `string ConstraintId`; `string RuleId`; `string ConstraintTreatment`; `string MeasurementBasisKey`; `string SubjectKind`; `string? SubjectId`; `double? ActualDistanceMeters`; `decimal RequiredDistanceMeters`; `string EvaluationState` (E.3 vocabulary); `string? Reason`; `IReadOnlyList<string> EvidenceHandles` |
| Source of data | point-to-geometry distance vs. the constraint minimum |
| Confidence/evidence | `INSUFFICIENT_EVIDENCE` when no resolved subject exists for the basis — never `SATISFIED` |
| New code eventually required | Yes |

## F.4 Deterministic, non-inventing guarantees

The contract is designed so that a candidate can never overclaim:

- `CheckedConstraints` may contain `INSUFFICIENT_EVIDENCE` and `REVIEW_REQUIRED` entries — presence in the list means "evaluated", never "satisfied".
- `UncheckedConstraints` is a non-optional, non-empty list in v1. It statically declares the known-unavailable categories (see G.4), so "Not checked" is always visible.
- `RejectedReasons` retains candidates that were generated and then discarded, with the deterministic reason.
- `EngineeringReviewRequired` is a hard `true` for every v1 candidate — there is no field path that produces a candidate presented as final.

---

# G. Suggestion contract

## G.1 `PlacementProposal` — the explainable read-only suggestion result

| Aspect | Definition |
|---|---|
| Purpose | One complete, human-readable, read-only CHECK+SUGGEST explanation for one governing violation |
| Identity | deterministic `ProposalId` from PlacementId + generating constraint + candidate set (no time/random) |
| Contents (all required by the handoff) | |
| violated governed rule | `StandardsPackRule ViolatedRule` |
| observed value | `double ObservedDistanceMeters`, `string ObservedMeasurementBasis`, `string ObservedMeasurementId` (existing `EngineeringDistanceViolation.MeasurementId`) |
| required value | `decimal RequiredDistanceMeters` |
| measurement basis | `string MeasurementBasisKey` + applicability + unit context (`DrawingUnitResolution` fields) |
| source/clause evidence | `IReadOnlyList<StandardClauseCitation> ClauseEvidence` (existing type, carries clause + source + revision + page + SHA256) |
| original position | `double OriginalEasting / OriginalNorthing` |
| proposed position | `double ProposedEasting / ProposedNorthing` |
| movement distance/direction | `double MovementDistanceMeters`, `string MovementDirection` |
| Road re-evaluation | `PlacementProposalConstraintOutcome` (or null with reason) |
| Fence re-evaluation | `PlacementProposalConstraintOutcome` (or null with reason) |
| Unit Boundary re-evaluation | `PlacementProposalConstraintOutcome` (or null with reason) |
| satisfied constraints | `IReadOnlyList<PlacementProposalConstraintOutcome>` |
| violated constraints | `IReadOnlyList<PlacementProposalConstraintOutcome>` |
| insufficient-evidence constraints | `IReadOnlyList<PlacementProposalConstraintOutcome>` — **never collapsed into satisfied** |
| unchecked constraints | `IReadOnlyList<string>` — mandatory non-empty |
| assumptions | `IReadOnlyList<string>` — mandatory non-empty |
| rejected candidates | `IReadOnlyList<PlacementProposalCandidate>` with their `RejectedReasons` |
| engineering review requirement | `bool EngineeringReviewRequired` (always `true` in v1) + `string EngineeringReviewReason` |
| known limitations | `IReadOnlyList<string>` — mandatory non-empty, statically including the point-based limitation |
| DWG effect | **None.** No field in this contract can express a drawing mutation. The type contains no host entity and no write capability. |

## G.2 Mandatory limitation text

Every `PlacementProposal` must carry, verbatim in `KnownLimitations`, the point-based limitation:

> Equipment location is represented as a single resolved coordinate point derived from drawing coordinate
> evidence. Equipment footprint, orientation, and physical extent are not captured. Distances are
> point-to-geometry measurements, not edge-to-edge clearances. Candidate positions are candidate points.
> Pipe routing, maintenance access, underground utilities, crane and nozzle interactions, and process layout
> are not evaluated. Engineering review is required.

This is a contract-level invariant (non-empty list containing this item), not a UI nicety, so it cannot be
silently dropped by a future caller.

## G.3 Assumptions (mandatory, non-empty in v1)

At minimum: the point-based location model; the closed road-edge allow-list; the exclusion of curved geometry
(`bulge != 0`); the association distance and ambiguity margins (`30 × text height`, `2 × text height`); and
that project criteria are `PROJECT_RULE_ONLY` unless an approved governed basis exists.

## G.4 Unchecked constraints (mandatory, non-empty in v1)

Statically declared for the bounded scope: pipe routing; maintenance access envelopes; underground utilities;
crane/nozzle interactions; process layout; equipment footprint-based clearance; occupied-building siting;
flare/hazardous-area separation; any constraint whose required subject kind is not Road / Plant Fence /
Unit Boundary.

---

# H. Candidate generation boundary

## H.1 Strategy (architecture level only)

**Normal-displacement generation from a proven governed violation.** No optimizer, no search, no objective
function. Steps:

1. **Start from a proven violation.** Input is exactly one `EngineeringDistanceViolation` produced by the
   existing `EngineeringDistanceRuleEvaluator` for a constraint whose treatment is `HARD_CONSTRAINT` or
   `SAFETY_CONSTRAINT` and whose `AutoExecutionAllowed == true` (otherwise the violation is surfaced as
   `REVIEW_REQUIRED` and no candidate is generated).
2. **Use the existing point-based measurement semantics.** The equipment point is the existing resolved
   `(Easting, Northing)`. The violating geometry is the existing `PlotGeometryPrimitive` identified by
   `EngineeringDistanceViolation.GeometrySourceHandle`, with the recorded closest point
   (`ClosestPointX`, `ClosestPointY`).
3. **Compute the displacement direction** as the unit vector from the violating geometry's closest point
   toward the equipment point. This is the direction that *increases* the clearance, derived from the same
   evidence as the measurement. Degenerate case (equipment point coincides with the closest point): fall back
   to a deterministic ordered set of cardinal directions (`E`, `N`, `NE`, …), each evaluated and retained or
   rejected with a reason.
4. **Compute the displacement magnitude** as `RequiredDistanceMeters − ActualDistanceMeters`, converted to
   drawing units via the existing `DrawingUnitResolver.MetersToDrawingUnits`. A declared, governed margin
   parameter may be added later; v1 uses margin `0` (minimum movement).
5. **Emit a small deterministic candidate set**: the exact-minimum displacement candidate, plus a small fixed
   set of additional candidates along the same direction at declared increments (so ranking has something to
   compare and rejection reasons are exercised). The set size and increments are constants, not search.
6. **Terminate.** No iteration toward global optimality, no constraint relaxation, no trade-off search.

## H.2 Hard boundaries

- **No general optimizer** in Phase B/C/D (handoff: "Do not design a general optimizer yet").
- **No** pipe routing, maintenance-layout, process-layout, or full facility optimization; no nozzle/crane
  reasoning; no underground utility avoidance.
- **No invented geometry**: no footprint, orientation, or extent is created to make a candidate "fit".
- **No compliance claims without evidence**: a candidate that resolves the original violation but has
  `INSUFFICIENT_EVIDENCE` for another bounded constraint is retained with that state visible (see I.3).
- **Determinism**: identical inputs ⇒ identical candidate set, identical order, identical IDs. Ordering by
  (displacement, then `ProposedEasting`, then `ProposedNorthing`, then `GeneratingConstraintId`), all with
  `StringComparer.Ordinal` where strings are compared.
- **No DWG access**: generation consumes `PlantModelV1` only; it never opens a document or writes anything.

## H.3 Generation refusal conditions

A candidate is **not** generated, and the proposal carries `REVIEW_REQUIRED`, when: the violation's rule is
not auto-executable; the subject geometry is unresolved; the equipment placement is `Incomplete` or
`Ambiguous`; the displacement direction is degenerate and no cardinal candidate achieves compliance; or any
required evidence handle is missing. Each refusal records a deterministic reason.

---

# I. Candidate re-evaluation

## I.1 Rule

**Every** proposed point is re-evaluated against **all** currently supported bounded constraints — Road,
Plant Fence, and Unit Boundary — using the existing measurement semantics. A candidate is never presented as
compliant merely because it resolves the original violation.

## I.2 Reuse of the existing pipeline

The measurement math already exists, but is currently private inside
`PlotSpatialMeasurementBuilder.BuildEngineeringSpatial` (`PlotSpatialMeasurementBuilder.cs:176-210`): iterate
the primitive's segments (`Closed` aware), clamp the projection parameter `t` to `[0,1]`, compute the
point-to-segment distance, and keep the minimum with deterministic tie-break (lowest distance, then lowest
segment index).

**The smallest safe extraction:** a new public pure helper — proposed `PointToSegmentDistance` /
`PointToPolylineDistance` in `EngiSentry.PlotPlan` — containing that exact algorithm. `PlotSpatialMeasurementBuilder`
is then changed to *delegate* to it. Behaviour is provably identical (same inputs, same outputs, same
tie-breaks), and the re-evaluator gains a host-free, placement-free function:

```
double distance = PointToPolylineDistance(pointX, pointY, primitive);   // pure
```

The re-evaluator then applies the **existing selection rule** used by `EngineeringDistanceRuleEvaluator.EvaluateRule`:
group measurements by placement, take the minimum-distance governing subject per rule basis, and compare
against the rule minimum — but applied to the *candidate* point instead of the original placement point.

## I.3 Complete known result set

For each candidate and each bounded constraint, the outcome is one of the E.3 states, and **all** are retained
on `PlacementProposalCandidate.CheckedConstraints`:

| Situation | Outcome |
|---|---|
| ≥1 resolved subject for the basis, min distance ≥ minimum | `SATISFIED` |
| ≥1 resolved subject for the basis, min distance < minimum | `VIOLATED` |
| 0 resolved subjects for the basis | `INSUFFICIENT_EVIDENCE` — never `SATISFIED` |
| Rule not auto-executable / quarantined / legacy / conflict | `REVIEW_REQUIRED` |
| Treatment is `ENGINEERING_PREFERENCE` / `OPTIMIZATION_OBJECTIVE` | recorded in assumptions, not as a PASS/FAIL |
| Constraint not in the bounded set | listed in `UncheckedConstraints` |

## I.4 Unit consistency

All candidate geometry is in drawing units, exactly as the existing pipeline. Comparison against
`MinimumDistanceMeters` uses the existing `DrawingUnitResolver.MetersToDrawingUnits` /
`DrawingUnitsToMeters` conversions, and the unit context (`InsertionUnits`, `CanonicalUnit`,
`MetersPerDrawingUnit`) is carried on the proposal — the same fields already on
`EngineeringDistanceRuleEvaluation`.

---

# J. Candidate ranking

## J.1 Deterministic policy (applied in order)

1. **Mandatory/governed constraint validity first.** Rank by ascending count of `VIOLATED` outcomes among
   constraints whose treatment is `HARD_CONSTRAINT` or `SAFETY_CONSTRAINT`. Any candidate with a violated
   mandatory constraint **cannot** outrank a candidate with none, regardless of objective score.
2. **Unresolved / review-required states explicitly retained.** Candidates are not penalized into silence for
   `INSUFFICIENT_EVIDENCE` or `REVIEW_REQUIRED`; instead these counts are carried as visible secondary
   attributes. A candidate is presented with its unresolved count; the count is never collapsed to zero and
   never hidden.
3. **Then the declared objective** — minimum movement: ascending `DisplacementDistance`.
4. **Deterministic tie-breaking:** `DisplacementDistance`, then `ProposedEasting`, then `ProposedNorthing`,
   then `GeneratingConstraintId`, then `CandidateId` — all numeric/string comparisons with
   `StringComparer.Ordinal` for strings. No dictionary or set iteration order, no `DateTime.UtcNow`, no random
   IDs — the same determinism discipline already enforced by `StandardAuthorityReviewPackageBuilder`.

## J.2 Hard prohibitions

- An optimization score **must not** override a violated mandatory constraint (structural in J.1 step 1).
- `INSUFFICIENT_EVIDENCE` **must not** be hidden; it is a first-class outcome with its own count and its own
  visible rows.
- A candidate with any `REVIEW_REQUIRED` outcome is labelled as requiring engineering review even if it ranks
  first.
- Ranking never changes any measurement, evidence, or governance state — it only orders already-computed
  outcomes.

## J.3 Output

The ranker produces an ordered `IReadOnlyList<PlacementProposalCandidate>` plus a deterministic
`PlacementProposalRankingSummary` (counts per state, the winning candidate id, and the reason each rejected
candidate was dropped). The summary is what makes "rank candidates transparently" auditable from the artifact
alone.

---

# K. Read-only proposal projection

## K.1 Principle — the smallest possible UI extension

Per the handoff ("Do not design a large new UI"), the proposal surface is **an additive read-only projection
through the existing Results infrastructure**, reusing the patterns already proven in P6/P7/P10.2:

- `VisualResultsPalette.ShowPlotQa` already publishes metrics, findings, distance rows, authority review items,
  and the review package + SHA256 to a read-only grid.
- `PlotPlanDistancePresentation.Build` already turns an `EngineeringDistanceRunSummary` into exactly three
  presentation rows with `Name / Minimum / Observed / Status / Interpretation`, including the
  `<not resolved>` wording for unresolved measurements and the `PROJECT_RULE_ONLY` explanation.
- `PlotPlanReportCompactor.CreatePlotPlanQaReport` already compacts the report graph without changing the live
  model, and `Create()` deliberately empties `Primitives` — the compaction discipline to follow.

## K.2 What must be visible to the engineer

A `PlacementProposal` projection surfaces, read-only: the violated rule with company/pack/document/revision/
clause/page/requirement text; source SHA256; the measurement basis and unit context; observed vs. required
distance; original and proposed point; movement distance and direction; per-constraint re-evaluation rows for
Road, Plant Fence, and Unit Boundary with their states; satisfied / violated / insufficient-evidence /
unchecked lists; assumptions; rejected candidates with reasons; the engineering-review requirement; and the
known-limitations text from G.2.

## K.3 Serialization discipline (non-regression critical)

- The projection is emitted as a **separate sidecar artifact**, following the exact P7.2 pattern:
  `<drawing>.<snapshot timestamp>.placement-proposal.json`, written with `FileMode.CreateNew`, reusing the
  existing snapshot timestamp, with a `DataContractJsonSerializer`, and deterministic ordering.
- **`PlotPlanQaReport` must not gain any new `[DataMember]`.** Its `[IgnoreDataMember] ReviewProjection`
  guarantee ("ReviewProjection serialized in PlotQA JSON: No") and the accepted P8 review-package SHA256 chain
  depend on the current serialized shape. A byte-identical serialization gate (Section M) protects this.
- The UI path keeps the existing `AutoCADHostBoundary.IsCoreConsole()` short-circuit so headless tests never
  require a window, and keeps `Publish()` non-mutating.
- Optionally a new command (e.g. `ES_SUGGEST`) may be added later; it must follow the existing `ES_PLOTQA`
  shape: scan → extract → build → evaluate → DBMOD guard before and after → sidecar write → DBMOD guard →
  palette show. **No command authorized in Phase A.**

---

# L. Exact future file plan

## L.1 Boundary reasoning

The audit proposed four additive files for Phase A. Phase A is now documentation-only, so those four become
Phase B targets. Repository evidence requires the boundary to be slightly larger than four files, for two
specific reasons:

1. **One-concept-per-file convention.** `EngiSentry.Contracts` holds 39 small files, each carrying one concept
   (`PlotPlanModels.cs`, `EngineeringDistanceRuleModels.cs`, `EngineeringDistanceEvaluationModels.cs`,
   `EngineeringDistanceCandidateModels.cs`, `StandardAuthorityModels.cs`, …). Merging PlantModel + taxonomy +
   constraint + candidate + StandardsPack into two files would contradict the established convention and make
   the additive diff harder to review.
2. **Contract/service separation.** The codebase keeps `[DataContract]` contracts in `Contracts` and the
   services that operate on them in `Rules` / `PlotPlan` / `Standards` (e.g. `EngineeringDistanceRuleEvaluator`
   in `Rules`, its contracts in `Contracts`). Generators, re-evaluators, and rankers are services and cannot
   live in `Contracts` without breaking that separation.

The audit's four files therefore map as: `ConstraintTreatmentModels.cs` and `ProposalModels.cs` split into
five contract files (same concepts, one per file); `PointToPolylineDistance.cs` unchanged; and the test file
plus the service files that Phase A had correctly deferred.

## L.2 Proposed files — Phase B (contracts + pure geometry + PlantModel extraction)

| Project | Directory | Namespace | Filename | Types |
|---|---|---|---|---|
| `EngiSentry.Contracts` | `src/EngiSentry.Contracts/` | `EngiSentry.Contracts` | `FacilityElementModels.cs` | `FacilityElementKind`, `ClassificationConfidence` (const-string classes), `FacilityElement`, `PlantModelV1`, `PlantModelDiagnostics` |
| `EngiSentry.Contracts` | `src/EngiSentry.Contracts/` | `EngiSentry.Contracts` | `ConstraintTreatmentModels.cs` | `ConstraintTreatment` (const-string class), `BoundedConstraint` |
| `EngiSentry.Contracts` | `src/EngiSentry.Contracts/` | `EngiSentry.Contracts` | `ProposalEvaluationModels.cs` | `ProposalConstraintState` (const-string class), `PlacementProposalConstraintOutcome` |
| `EngiSentry.Contracts` | `src/EngiSentry.Contracts/` | `EngiSentry.Contracts` | `PlacementProposalCandidateModels.cs` | `PlacementProposalCandidate`, `PlacementProposalRankingSummary` |
| `EngiSentry.Contracts` | `src/EngiSentry.Contracts/` | `EngiSentry.Contracts` | `PlacementProposalModels.cs` | `PlacementProposal` (G.1), `ProposalAssumptions`, `ProposalUncheckedConstraints` (static default lists) |
| `EngiSentry.Contracts` | `src/EngiSentry.Contracts/` | `EngiSentry.Contracts` | `StandardsPackRuleModels.cs` | `StandardsPackRule` (D.2 projection) |
| `EngiSentry.PlotPlan` | `src/EngiSentry.PlotPlan/` | `EngiSentry.PlotPlan` | `PointToPolylineDistance.cs` | `PointToPolylineDistance` (pure helper; `PlotSpatialMeasurementBuilder` delegates to it) |
| `EngiSentry.PlotPlan` | `src/EngiSentry.PlotPlan/` | `EngiSentry.PlotPlan` | `FacilityElementBuilder.cs` | `FacilityElementBuilder` (reads `PlotPlanModel` only) |
| `EngiSentry.Tests` | `tests/` | `EngiSentry.Tests` | `ProposalContractTests.cs` | contract round-trip; `PointToPolylineDistance` parity vs `BuildEngineeringSpatial`; the five states; `INSUFFICIENT_EVIDENCE` preservation |

## L.3 Proposed files — Phase C (one governed CHECK pack)

| Project | Directory | Namespace | Filename | Types |
|---|---|---|---|---|
| `EngiSentry.Standards` | `src/EngiSentry.Standards/` | `EngiSentry.Standards` | `StandardsPackProjectionBuilder.cs` | builds `IReadOnlyList<StandardsPackRule>` from `PlotPlanStandardsAuthorityRuntimeContext` + `EngineeringDistanceRuleRegistry` |
| `EngiSentry.Standards` | `src/EngiSentry.Standards/` | `EngiSentry.Standards` | `StandardsPackPackCatalog.cs` | one authorized pack definition (source/clause/rule rows supplied by the user, never model memory) |
| `EngiSentry.Rules` | `src/EngiSentry.Rules/` | `EngiSentry.Rules` | `BoundedConstraintBuilder.cs` | `StandardsPackRule` + treatment → `BoundedConstraint` |
| `EngiSentry.Tests` | `tests/` | `EngiSentry.Tests` | `StandardsPackProjectionTests.cs` | projection field mapping; `SOURCE_REQUIRED`/quarantine degradation; 0 auto-executable invariant |

## L.4 Proposed files — Phase D (explainable SUGGEST)

| Project | Directory | Namespace | Filename | Types |
|---|---|---|---|---|
| `EngiSentry.Rules` | `src/EngiSentry.Rules/` | `EngiSentry.Rules` | `PlacementProposalCandidateGenerator.cs` | H strategy |
| `EngiSentry.Rules` | `src/EngiSentry.Rules/` | `EngiSentry.Rules` | `PlacementProposalReEvaluator.cs` | I; uses `PointToPolylineDistance` |
| `EngiSentry.Rules` | `src/EngiSentry.Rules/` | `EngiSentry.Rules` | `PlacementProposalRanker.cs` | J |
| `EngiSentry.Rules` | `src/EngiSentry.Rules/` | `EngiSentry.Rules` | `PlacementProposalGenerator.cs` | composes the three above into `PlacementProposal` |
| `EngiSentry.AutoCAD2021` | `src/EngiSentry.AutoCAD2021/Scanning/` | `EngiSentry.AutoCAD2021.Scanning` | `PlacementProposalSnapshotWriter.cs` | K.3 sidecar |
| `EngiSentry.AutoCAD2021` | `src/EngiSentry.AutoCAD2021/` | `EngiSentry.AutoCAD2021` | *(modify)* `VisualResultsPalette.cs` | additive read-only proposal rows only |
| `EngiSentry.AutoCAD2021` | `src/EngiSentry.AutoCAD2021/` | `EngiSentry.AutoCAD2021` | *(modify)* `Commands.cs` | optional `ES_SUGGEST` following the `ES_PLOTQA` shape |
| `EngiSentry.Tests` | `tests/` | `EngiSentry.Tests` | `PlacementProposalTests.cs` | generation determinism; re-evaluation completeness; ranking invariants; refusal conditions |

## L.5 Modifications required (additive only)

`src/EngiSentry.PlotPlan/PlotSpatialMeasurementBuilder.cs` (delegate to `PointToPolylineDistance`,
behaviour identical), `tests/EngiSentry.Tests.csproj` (each new test file added to the explicit
`<Compile Include>` list at line 29 — classic non-SDK csproj), and the two `AutoCAD2021` files above.

## L.6 Explicitly not modified

`DrawingScanner.cs`, `SemanticExtractor.cs`, `EntityIndexer.cs`, `PlotPlanBuilder.cs`,
`PlotGeometryBuilder.cs`, `PlotSpatialBuilder.cs`, `EngineeringSpatialSubjectBuilder.cs`,
`EngineeringDistanceRuleEvaluator.cs`, `EngineeringDistanceRuleRegistry.cs`,
`EngineeringDistanceQaIntegrator.cs`, `ProjectEngineeringDistanceRuntime.cs`,
`PlotPlanReportCompactor.cs`, `StandardAuthorityReviewPackageBuilder.cs`, the entire P8/P9 governance layer,
and every DWG. `PlotPlanQaReport`, `QaReport`, `QaEvidence`, `EngineeringDistanceRunSummary`, and
`EngineeringDistanceRuleEvaluation` gain **no** new serialized members.

---

# M. Non-regression architecture

## M.1 Gates and the tests that protect them

| Area to preserve | Existing protection | New tests eventually needed |
|---|---|---|
| Plot Plan QA behaviour | `ClearanceTests` P51–P54, P102C1; `ProfileTests` P55; `UnitTests` final-line invariant `All EngiSentry M2.2 through M5.0 tests passed.` | Proposal layer must not alter `QaReport`/`QaRuleResult`/`QaFinding` shape — asserted by a serialization-parity test |
| Road / Fence / Unit Boundary measurements | `MeasurementTests.RunP410QualificationTests`; `ClearanceTests` P51–P53 | `PointToPolylineDistance` byte-identical parity vs `BuildEngineeringSpatial` on identical inputs — the regression canary |
| Existing evidence | `ClearanceTests` P92A/B/C, P92C2A/B/C; `ProfileTests` P61–P64 | Evidence handle sets on proposals must be subsets of existing source handles |
| P6/P7 authority & review traceability | `ProfileTests` P71, P72; `StandardAuthorityReviewPackageBuilder` determinism | Projection must not create sources/clauses/policies — asserted by identity-count invariants (4/4/4, 0 auto-executable) |
| P8 human review lifecycle | `ProfileTests` P81, P82, P83, P83H, P85 | Review-package SHA256 stability after adding the proposal sidecar |
| P9 project approval governance | `ClearanceTests` P91, P92A–P92C2C | `PROJECT_RULE_ONLY` unchanged for Road and Fence; no approval/trust artifact created by the projection |
| Serialization / contracts | accepted PlotQA + review sidecar artifacts | **New: byte-identical serialization gate** over the accepted fixture graph — protects `[DataMember]` order and the P8 hash chain |
| Results UI behaviour | P10.2 pilot acceptance; read-only grid | `Publish()` remains non-mutating; `IsCoreConsole()` bypass intact |
| No DWG mutation | `DrawingScanner` DBMOD before/after throw; per-command DBMOD guards in `Commands.cs` | DWG SHA256 before/after on the protected fixture; DBMOD unchanged |
| Host-independent deterministic core | headless `EngiSentry.Tests` `Main()` (5 runners, no AutoCAD) | All proposal tests run headlessly; zero `Autodesk.*` references in design-engine projects (grep gate) |

## M.2 Architectural invariants to be enforced by review and test

1. Zero `using Autodesk.*` and zero host types in the eight design-engine projects.
2. `PlotPlanQaReport`, `QaReport`, `EngineeringDistanceRunSummary`, `EngineeringDistanceRuleEvaluation` gain no
   new serialized members.
3. `ReviewProjection` stays `[IgnoreDataMember]` and absent from PlotQA JSON.
4. Authority/gate/review stays 4/4/4; auto-executable stays 0; authority-generated findings and rule-results
   stay 0.
5. Road and Fence governance stays `PROJECT_RULE_ONLY` until an approved governed basis exists.
6. `EngineeringDistanceRuleEvaluation.Status` vocabulary is unchanged; proposal states live only on new types.
7. Every `PlacementProposal` carries non-empty `KnownLimitations`, `Assumptions`, `UncheckedConstraints`, and
   `EngineeringReviewRequired == true`.
8. No candidate is generated for a non-auto-executable or quarantined rule.
9. Determinism: no `DateTime.UtcNow`, no random IDs, no unordered-collection iteration in outputs.
10. Phase A wrote no code; Phase B/C/D changes are additive and touch no accepted contract field.

---

# N. Phase B entry gate

Phase B may begin only when **all** of the following hold. The baseline-preservation action is a **gate before
Phase B**, not a new project phase, and is kept strictly separate from Standards Assistant architecture work.

1. **Phase A architecture approval.** This document accepted by the user as-is or with recorded amendments.
2. **Accepted P8/P9 baseline safely preserved in Git.** The 34 untracked P8/P9 `.cs` files plus the P10.2
   modifications are committed (and ideally pushed) as a verified baseline snapshot, so that
   `eb6efbc`-plus-governance is recoverable and a `git clean`/bad merge cannot destroy the accepted layer.
   *Recorded by the audit (M.1); acknowledged by instruction; not fixed or re-audited here.*
3. **Line-ending churn excluded from the baseline-preservation commit.** The CRLF/LF mixed endings are not
   normalized (explicitly prohibited); the preservation commit must contain only real content changes,
   verified by `git diff --ignore-all-space`. *Recorded by the audit (M.2).*
4. **Existing build/tests passing.** `Release|x64` with 0 warnings / 0 errors, and the full
   `EngiSentry.Tests` harness green on the Windows host with final line
   `All EngiSentry M2.2 through M5.0 tests passed.` (Cannot be verified in the Linux VM — no .NET toolchain.)
5. **No unexplained semantic working-tree changes.** Every real content difference vs. HEAD is attributable to
   P8/P9/P10.2 (the 12 files, +1637/−20, identified by `git diff --ignore-all-space`).
6. **No authorized StandardsPack rule invented from model memory.** Phase C's pack rows (source, revision,
   clause, page, requirement text, distance) come only from authorized user-supplied standards.
7. **Original DWG untouched.** Protected fixtures retain their accepted SHA256 values, including
   `PLOTPLAN-A0-E-FINAL.dwg` = `3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4`.

---

# DECISIONS FROM THE AUDIT

Review of Section M of `ENGISENTRY_STANDARDS_ASSISTANT_AUDIT_2026-09-19.md` (M.1–M.8). Per instruction, no
question is asked where repository evidence or the governing handoff already determines the answer.

## Resolved directly by the handoff / governing instructions

| # | Audit decision | Resolution | Basis |
|---|---|---|---|
| **M.1** | Uncommitted P8/P9 baseline | **RESOLVED — recorded as a mandatory pre-Phase-B gate.** Not fixed, not re-audited, and not mixed into Standards Assistant architecture work. | User instruction: "acknowledged … record it as a mandatory pre-Phase-B baseline-preservation gate" → encoded as gate item 2 (Section N) |
| **M.2** | CRLF/LF churn | **RESOLVED — no normalization.** Line endings are left untouched; the baseline-preservation commit must exclude line-ending churn and contain only content changes. | User instruction prohibits normalizing line endings; audit A.2 proves the churn is mechanical → gate item 3 |
| **M.3** | P10.3 administrative closeout | **RESOLVED — out of scope for Phase A/B.** The handoff's instruction not to re-audit P10.3 stands; it is not among the Phase B entry conditions. | `ENGISENTRY_CURRENT_HANDOFF_2026-09-09.md` §P10.3 remains ACTIVE with engineering review PENDING; user instruction "Do not re-audit P10.3" |
| **M.4** | Candidate-position naming | **RESOLVED — `PlacementProposalCandidate`.** Distinct from `EngineeringDistanceCandidateDefinition` (candidate criterion/rule); collision structurally impossible. | User instruction preserves the audit naming finding; `Placement` is the existing domain term (`EquipmentPlacement`, `PlacementId`) |
| **M.5** | Point vs. footprint stance | **RESOLVED — point-based model, explicitly labelled.** No footprint/orientation/maintenance-envelope invention; every proposal carries the point-based limitation text and `EngineeringReviewRequired`.** | User instruction: "Phase A must not invent equipment footprint…" → contract invariants G.2 and M.2 item 7 |
| **M.6** | Five-state result vocabulary | **RESOLVED — proposal layer states mapped onto existing semantics, never replacing them.** `SATISFIED/VIOLATED/INSUFFICIENT_EVIDENCE/NOT_APPLICABLE/REVIEW_REQUIRED` live on new proposal types only; `EngineeringDistanceRuleEvaluation.Status` and the accepted QaReport vocabulary are untouched. | Handoff: "It must support the existing decision semantics rather than replace them"; audit H.2 (`ValidateStatus` invariants) |
| **M.8** | Toolchain for verification | **RESOLVED — builds and tests run on the Windows host.** Phase A is documentation-only, so no build is required or attempted in this phase. | Audit A.4: no .NET toolchain in the Linux VM; `AGENTS.md` toolchain constraints |

## USER DECISION REQUIRED

**M.7 — Which governed source is authorized for the first StandardsPack (Phase C)?**

This is the only genuinely unresolved item, and it cannot be answered from repository evidence without inventing
engineering content — which is expressly prohibited. The existing governed sources are:

| Source | Identity | Authority posture (existing) |
|---|---|---|
| `trc.pcd-pd-135.rev1` | PCD-PD-135 Rev.1, Tehran Raymand Consulting Engineers — SHA256 `A59E7C6C…D572E` | `GOVERNING_PROJECT` except flare terms, which are `QUARANTINED` |
| `ips.e-pr-190.rev1.2009` | IPS-E-PR-190(1), Iranian Ministry of Petroleum, Mar 2009 — SHA256 `FC0FF48E…E152` | `LEGACY_CONFLICT` — never auto-enforced |
| `ips.g-sf-150.rev1.2023` | IPS-G-SF-150(1):2023 / INSO 23591:1402 — SHA256 `B767EE18…99B4B` | `ACTIVE_PRIMARY` (Iran baseline) |

**Safest recommended option:** authorize a small, explicit subset of clauses from **one** current,
non-quarantined source — `ips.g-sf-150.rev1.2023` — covering only Equipment-to-Road / Equipment-to-Fence /
Equipment-to-Unit-Boundary spacing, with the user supplying verified clause numbers, page numbers, requirement
text, and distance values from the controlled copy. Project criteria remain `PROJECT_RULE_ONLY` and continue to
run unchanged; the governed pack is additive and produces no auto-executable rule.

**Consequence of this option:** Phase C delivers one governed CHECK pack with real clause/page/SHA256 evidence
and zero invented distances. **Consequence of not deciding:** Phase C cannot start, and Phase B proceeds with
project criteria only — which is a valid, lower-risk path since the bounded CHECK already works on
`PROJECT_RULE_ONLY` criteria today. **Consequence of a wrong decision:** an unauthorized or mis-transcribed
distance is presented as a governed engineering requirement — the exact failure mode the P6→P9 chain exists to
prevent, and why `EngineeringDistanceApprovalGate` requires an `ApprovalReference` for any executable rule.

Note that the recommended option **does not** require any change to the accepted flare posture: flare terms stay
`SOURCE_QUARANTINED` / `NEVER_AUTO_ENFORCE` with `90 meters` remaining source evidence only.

---

## Phase A completeness check

Sections A through N are all defined above, plus the audit-decision resolution. Phase A produced **one
document and nothing else**. No implementation file, test file, DWG, project file, or git state was created or
modified by this phase (verified below).

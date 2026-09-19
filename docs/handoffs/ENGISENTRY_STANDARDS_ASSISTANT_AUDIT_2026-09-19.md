# EngiSentry — Standards-Based Plot Plan Design Assistant
## Step 1: Read-Only Architecture / Code-Backed Audit

Date: 2026-09-19
Scope: READ-ONLY. No production code, tests, DWG, or git state were modified.
Baseline audited: `master` @ `eb6efbc58825592f95cd1eb2b7b3372106ca8364` + working tree.

---

# A. Repository / baseline verification

## A.1 Git state (read-only)

| Item | Value | Evidence |
|---|---|---|
| Current branch | `master` | `git branch` |
| HEAD | `eb6efbc58825592f95cd1eb2b7b3372106ca8364` | `git rev-parse HEAD` — **matches** the handoff's "Known baseline HEAD" |
| Remote | `remotes/origin/master` | `git branch -a` |
| Ahead of origin | **2 commits** (`7fd05d5` P6, `eb6efbc` P7) | `git log origin/master..HEAD` — **not pushed** |
| Worktree | **NOT CLEAN — 66 porcelain entries** | `git status --porcelain \| wc -l` |
| Modified tracked | 28 files | `git status` |
| Untracked | 38 entries (incl. `artifacts/`, `packaging/installer/`, 35 `.cs` files, 1 `.md`) | `git status` |

## A.2 The worktree is dirty — and the dirt is not what it looks like

`git diff --stat` reports huge changes (e.g. `Commands.cs` 643 changed lines, `DrawingScanner.cs` 662, `VisualResultsPalette.cs` 1804). **This is almost entirely CRLF/LF line-ending churn, not content.** Verified:

```
file src/EngiSentry.AutoCAD2021/Commands.cs
→ ASCII text, with very long lines (1247), with CRLF, LF line terminators
```

`git diff --ignore-all-space --stat` reduces to **12 files, +1637 / −20**:

| File | Real change | Phase |
|---|---|---|
| `docs/handoffs/ENGISENTRY_CURRENT_HANDOFF_2026-09-09.md` | +128 | P10.x record keeping |
| `packaging/EngiSentry.Bundle/PackageContents.xml` | 2 | packaging |
| `src/EngiSentry.AutoCAD2021/Commands.cs` | 11 | P9 wiring into `ES_PLOTQA` |
| `src/EngiSentry.AutoCAD2021/EngiSentry.AutoCAD2021.csproj` | 1 | adds `ProjectDistanceGovernanceRuntimeLoader.cs` |
| `src/EngiSentry.AutoCAD2021/ProjectDistanceCriteriaDialog.cs` | 4 | P10.2 criteria UX |
| `src/EngiSentry.AutoCAD2021/ProjectEngineeringDistanceRuntime.cs` | 34 | P9 governance overload |
| `src/EngiSentry.AutoCAD2021/Properties/AssemblyInfo.cs` | 6 | version |
| `src/EngiSentry.AutoCAD2021/VisualResultsPalette.cs` | +430 | P10.2 Results UX |
| `src/EngiSentry.Contracts/EngineeringDistanceRunSummaryModels.cs` | 1 | `Governance` field |
| `src/EngiSentry.Standards/StandardAuthorityReviewPackageBuilder.cs` | 10 | P8.6 timestamp normalization |
| `tests/ClearanceTests.cs` | +591 | P9 tests |
| `tests/ProfileTests.cs` | +439 | P6–P9 tests |

## A.3 ⚠ Critical baseline finding — P8/P9 code is UNCOMMITTED

`git ls-files src/EngiSentry.Standards/` shows the committed Standards surface ends at P7. **The entire P8 (Human Engineering Review) and P9 (Project Approval Governance) implementation exists only as untracked working-tree files:**

Untracked (P9 approval/trust): `EngineeringDistanceApprovalArtifactCatalog.cs`, `EngineeringDistanceApprovalAuthorityPolicyArtifactCanonicalizer.cs`, `EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.cs`, `EngineeringDistanceApprovalAuthorityPolicyArtifactValidator.cs`, `EngineeringDistanceApprovalAuthorityPolicyRegistry.cs`, `EngineeringDistanceApprovalAuthorityResolver.cs`, `EngineeringDistanceApprovalAuthorityTrustStore.cs`, `EngineeringDistanceApprovalAuthorityTrustedIssuerRegistry.cs`, `EngineeringDistanceApprovalCurrentnessValidator.cs`, `EngineeringDistanceApprovalHistoryResolver.cs`, `EngineeringDistanceApprovalRecordBuilder.cs`, `EngineeringDistanceApprovalRecordFileStore.cs`, `EngineeringDistanceApprovalRecordValidator.cs`, `ProjectDistanceGovernanceAggregateResolver.cs`, `ProjectDistanceGovernedRuleResolver.cs`, `ProjectEngineeringDistanceAuthorityPolicyCatalog.cs`.

Untracked (P8 human review): `HumanEngineeringReviewDecisionArtifactCatalog.cs`, `HumanEngineeringReviewDecisionBuilder.cs`, `HumanEngineeringReviewDecisionFileStore.cs`, `HumanEngineeringReviewDecisionHistoryResolver.cs`, `HumanEngineeringReviewDecisionStalenessValidator.cs`, `HumanEngineeringReviewDecisionValidator.cs`.

Untracked (contracts): `EngineeringDistanceApproval*Models.cs` (7), `HumanEngineeringReviewDecision*Models.cs` (3), `ProjectDistanceGovernance*Models.cs` (2), plus `ProjectDistanceGovernanceRuntimeLoader.cs`.

Consequences, all verified against the project files:

1. `src/EngiSentry.Standards/EngiSentry.Standards.csproj` is **SDK-style** (`Microsoft.NET.Sdk`) with **no explicit `<Compile Include>` list**, so it auto-globs every `.cs` in the folder. The untracked P8/P9 files therefore compile locally.
2. A clean checkout of `eb6efbc` **cannot build the P8/P9 layer** — those files would be absent.
3. `tests/EngiSentry.Tests.csproj` is a **classic (non-SDK) csproj with an explicit `<Compile Include>` list** (line 29). New test files must be manually registered there or they are silently excluded from the build.
4. P10.2's `VisualResultsPalette`/`PlotPlanDistancePresentation` changes and the `ES_PLOTQA` governance wiring in `Commands.cs` are modified-but-uncommitted.

**Interpretation:** the operational baseline described in the handoff (P6→P10.2 CLOSED/PASS) is real and runtime-validated, but it is **not fully captured in git**. Any new work must be layered on top of this uncommitted state, not on a clean `eb6efbc` checkout.

## A.4 Build/test toolchain in this session

`which dotnet msbuild csc` → **none available** in this Linux VM. The solution targets **.NET Framework 4.8** (`EngiSentry.AutoCAD2021`, `EngiSentry.Tests`) and **netstandard2.0** (Contracts, Core, Semantics, Rules, PlotPlan, Standards, CrossDocument, Revision). Per `AGENTS.md`: AutoCAD 2021 x64 only, no AI/cloud/database/Plant 3D/auto-fix, no unnecessary dependencies, and "Keep architecture ready for a future .NET 10 engine separated from the AutoCAD process."

**Consequence:** I could not compile or run the test harness (`tests/UnitTests.cs` → `Main()` orchestrates `CrossDocumentTests.Run()`, `RevisionTests.Run()`, `ProfileTests.Run()`, `MeasurementTests.Run()`, `ClearanceTests.Run()`) in this session. All build/test verification in later phases must run on the Windows host via the user's toolchain.

## A.5 P10.3 status — administrative closeout only

`docs/handoffs/ENGISENTRY_CURRENT_HANDOFF_2026-09-09.md` line 290: `## P10.3 — Supervised Engineer Pilot: ACTIVE`.
`docs/handoffs/P10.3-SUPERVISED-ENGINEER-PILOT-RECORD.md` line 36: `Operational pilot: 4 / 4 PASS`, and every one of the 4 cases carries `Engineering review: PENDING`.

**Per instruction, P10.3 is NOT re-audited.** The only closeout action is administrative: a qualified engineer completes the observation template (already present in `P10.3-SUPERVISED-ENGINEER-PILOT-RECORD.md` lines 86–146, one per drawing, ≥3 drawings), records the engineering-interpretation and usability verdicts, and the record is updated so `Engineering review` moves from `PENDING` to a recorded outcome and P10.3 flips to `CLOSED / PASS`. No code, DWG, or re-run is required.

## A.6 Baseline safety posture (unchanged, verified in code)

- `DrawingScanner.Scan` reads `DBMOD` before/after and throws on change (`DrawingScanner.cs:54-55`).
- Every command in `Commands.cs` re-checks `DBMOD` after processing and after artifact write (e.g. `Commands.cs:181-190`).
- `SnapshotFileWriter` uses `FileMode.CreateNew` for sidecar artifacts (per P7.2 record); the DWG is never written.
- Protected DWG `PLOTPLAN-A0-E-FINAL.dwg` SHA256 `3D5BCC2D…F19DC4` is documented unchanged across P6→P10.2.

---

# B. Existing reusable architecture

The accepted production pipeline is already a near-complete **CHECK** machine for the bounded use case. Sequence, all verified:

```
ES_PLOTQA (Commands.PlotPlanQa, Commands.cs:151)
 ├─ DrawingScanner.Scan(document, capturePlotGeometry:true, …)      [AutoCAD adapter]
 │    └─ PlotGeometryCapture { Primitives, ModelSpaceTextSources }
 ├─ SemanticExtractor.Extract(snapshot) → EngineeringDrawingModel
 │    └─ EquipmentTags, StructureTags, Coordinates, Elevations, RoadLabels, BoundaryLabels, EquipmentListEntries
 ├─ EntityIndexer.Build(semantic) → EngineeringEntityIndex
 ├─ PlotPlanBuilder.Build(index, model, snapshot, geometryCapture) → PlotPlanModel   ← proto-PlantModel
 │    ├─ PlotGeometryBuilder.Build        (label → geometry association)
 │    ├─ PlotSpatialBuilder.Build         (explicit "UNIT LIMIT E.x" / "ROAD C.L." coordinate → segment)
 │    ├─ EngineeringSpatialSubjectBuilder.Build  (Road/Fence/UnitBoundary → EngineeringSpatialSubject)
 │    └─ PlotSpatialMeasurementBuilder.Build     (point→segment measurements, incl. equipment-pair)
 ├─ QaEngine.Evaluate(index, BuiltInQaRules.All, plotPlan, BuiltInPlotPlanQaRules.All) → base QaReport
 ├─ ProjectDistanceGovernanceRuntimeLoader.LoadCanonical(criteria, approvalDirectory) → governance aggregate
 ├─ ProjectEngineeringDistanceRuntime.Evaluate(criteria, plotPlan, baseQa, governance) → QaReport + EngineeringDistanceRunSummary
 ├─ PlotPlanReportCompactor.CreatePlotPlanQaReport → PlotPlanQaReport (+ StandardsAuthority + ReviewProjection)
 ├─ SnapshotFileWriter.WritePlotPlanQaReport / WriteStandardsReviewPackage
 └─ VisualResultsPalette.ShowPlotQa (read-only Results surface)
```

Governance/authority chain (P6→P9), all in `EngiSentry.Standards`:

```
PlotPlanStandardsAuthorityRuntimeContextCatalog.Create()
 ├─ StandardSourceRegistry.Create(Sources())          3 real sources w/ SHA256
 ├─ StandardClauseRegistry.Create(sourceRegistry, Clauses())   9 clauses w/ page numbers
 ├─ PlotPlanStandardsAuthorityPolicyCatalog.Create()  4 authority policies
 ├─ StandardRulePackRegistry / StandardRulePackResolver  (packs + precedence → EffectiveRules)
 ├─ StandardAuthorityResolver.Resolve(...)            → StandardAuthorityResolution
 ├─ StandardAuthorityExecutionGate.Evaluate(...)      → StandardExecutionGateResolution (AutoExecutionAllowed)
 └─ StandardAuthorityReviewProjector.Create(...)      → StandardAuthorityReviewProjection
```

Project-distance governance chain (P9):

```
ProjectDistanceGovernanceRuntimeLoader.LoadCanonical(criteria, approvalDirectory)
 ├─ ProjectEngineeringDistanceReferenceCatalog.Create()       (clause + source registries)
 ├─ ProjectEngineeringDistanceAuthorityPolicyCatalog.Create()
 ├─ EngineeringDistanceApprovalArtifactCatalog.Load(directory, projectDirectory)
 ├─ EngineeringDistanceApprovalAuthorityTrustStore.LoadCanonical()   (%PROGRAMDATA%\EngiSentry\authority-trust)
 ├─ EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.LoadCanonical(projectDirectory, trustedIssuers)
 └─ ProjectDistanceGovernanceAggregateResolver.Resolve(...)  → ProjectDistanceGovernanceAggregateResult
      └─ per-rule ProjectDistanceGovernedRuleResolver.Resolve(...) → ProjectDistanceGovernedRuleResolution
           └─ EngineeringDistanceApprovalAuthorityResolver.Resolve(...) (currentness + history + policy)
```

---

# C. Exact reusable classes / files / contracts

## C.1 Contracts (`src/EngiSentry.Contracts`)

| Contract | File | Reuse for |
|---|---|---|
| `PlotPlanModel` | `PlotPlanModels.cs:7` | **PlantModel v1 root** — already holds Drawing, EquipmentPlacements, Geometry, Measurements, Statistics |
| `EquipmentPlacement` | `PlotPlanModels.cs:17` | Equipment instance: PlacementId, EquipmentEntityId, NormalizedTag, tag position/scale, E/N `CoordinateAssociation`, `PlacementStatus` (Incomplete/Complete/Ambiguous), `EngineeringApplicabilityKey` |
| `PlotGeometryModel` | `PlotPlanModels.cs:110` | Geometry index: Primitives, Associations, SpatialConstraints, **`EngineeringSpatialSubjects`** |
| `PlotGeometryPrimitive` / `PlotGeometryVertex` | `PlotPlanModels.cs:99/108` | Road/fence/boundary polyline geometry (handle, layer, kind, closed, vertices) |
| `EngineeringSpatialSubject` | `EngineeringSpatialSubjectModels.cs:7` | **The Road/Fence/UnitBoundary actor** — SubjectId, SubjectKind, `EngineeringMeasurementBasisKey`, geometry handle/layer/kind, AssociationIds, LabelSourceHandles |
| `EquipmentSpatialMeasurement` | `PlotPlanModels.cs:232` | **Observed measurement** — SegmentStart/End, ClosestPointX/Y, `Distance2D`, basis + applicability keys, label + geometry source handles |
| `EngineeringDistanceRuleDefinition` | `EngineeringDistanceRuleModels.cs:6` | Rule shape: RuleId, MeasurementKind, `MinimumDistanceMeters`, RequiredMeasurementBasisKey, RequiredApplicabilityKey, `CriteriaProvenance` |
| `EngineeringDistanceEvidenceKeys` | `EngineeringDistanceRuleModels.cs:27` | Deterministic taxonomy constants: `AccessRoadEdge`, `UnitPeripheralRoadEdge`, `SecurityFence`, `UnitBoundary`, `SiteBoundary`, `Equipment`, `ProcessingEquipment`, `SpecialHazardousUnit` + validators |
| `EngineeringDistanceRuleEvaluation` | `EngineeringDistanceEvaluationModels.cs:7` | **Constraint result** — Status, EvaluatedSubjectCount, ViolationCount, unit conversion fields, violations |
| `EngineeringDistanceViolation` | `EngineeringDistanceEvaluationModels.cs:26` | **Violation evidence** — PlacementId, NormalizedTag, ActualDistanceDrawingUnits/Meters, ClosestPointX/Y, LabelSourceHandle, GeometrySourceHandle |
| `StandardDecisionStates` | `StandardAuthorityModels.cs:23` | Result vocabulary already includes `PASS`, `FAIL`, `ADVISORY`, `EXPERT_REVIEW_REQUIRED`, `STANDARD_CONFLICT`, `NOT_APPLICABLE`, `INSUFFICIENT_EVIDENCE`, `SOURCE_QUARANTINED` |
| `StandardAuthorityClasses` / `StandardEnforcementModes` | `StandardAuthorityModels.cs:5/15` | Authority classification (6 classes, 4 modes) |
| `StandardSourceDefinition` | `StandardSourceModels.cs:6` | Source identity: DocumentNumber, Title, Issuer, RevisionOrEdition, SourceFileName, **SourceSha256** |
| `StandardClauseDefinition` | `StandardClauseModels.cs:6` | Clause identity: ClauseId, SourceId, ClauseReference, ClauseText, Heading, **PageNumber** |
| `StandardClauseAuthorityResolution` | `StandardAuthorityResolutionModels.cs:7` | Full source/clause provenance (17 fields incl. SourceSha256, PageNumber, ClauseText) |
| `StandardRuleExecutionGateDecision` | `StandardAuthorityExecutionModels.cs:7` | Executability: EffectiveEnforcementMode, **AutoExecutionAllowed**, DecisionState |
| `StandardRulePackDefinition` / `StandardRulePackResolution` / `EffectiveStandardRuleDefinition` | `StandardRulePackModels.cs` / `…ResolutionModels.cs` | Pack + precedence resolution |
| `StandardFindingProvenance` / `StandardClauseCitation` | `StandardFindingProvenanceModels.cs` | Finding-level clause/source traceability |
| `PlotPlanQaReport` | `PlotPlanModels.cs:74` | Report root; `[IgnoreDataMember] ReviewProjection` keeps review out of PlotQA JSON |
| `QaReport` / `QaFinding` / `QaEvidence` / `QaStatistics` | `QaModels.cs` | Findings + evidence projection (29-field `QaEvidence`, incl. `EngineeringDistanceEvidence`) |
| `ProjectDistanceCriteria` / `ProjectDistanceCriteriaDefaults` | `ProjectDistanceCriteriaModels.cs` | Schema 2 criteria: Road 3 m, Fence 30 m, UnitBoundary 15 m |
| `EngineeringDistanceRunSummary` | `EngineeringDistanceRunSummaryModels.cs` | Run summary incl. `Governance` aggregate |
| `ProjectDistanceGovernanceAggregateResult`/`Item`, `ProjectDistanceGovernedRuleResolution`, `ProjectDistanceGovernanceProvenance` | `ProjectDistanceGovernanceAggregateModels.cs` / `…GovernedRuleResolutionModels.cs` | P9 governance outcome per rule |
| `EngineeringDistanceApprovalRecord`, `EngineeringDistanceCandidateBinding`, `EngineeringDistanceApprovalEvidenceBinding`, `EngineeringDistanceApprovalDecision` | `EngineeringDistanceApprovalRecordModels.cs` / `…CandidateModels.cs` | Immutable approval evidence with SHA256 bindings |
| `EngineeringDistanceApprovalCurrentnessStates/Reasons` | `EngineeringDistanceApprovalCurrentnessModels.cs` | CURRENT/STALE + 4 staleness reasons |
| `HumanEngineeringReviewDecisionRecord` + `HumanEngineeringReviewDecisionStates` | `HumanEngineeringReviewDecisionModels.cs` | P8 immutable human-review record |
| `DrawingUnitResolution` | `EngineeringMeasurementModels.cs` | Unit resolution (InsertionUnits → CanonicalUnit, MetersPerDrawingUnit) |

## C.2 Services

| Service | File | Reuse for |
|---|---|---|
| `PlotPlanBuilder` | `src/EngiSentry.PlotPlan/PlotPlanBuilder.cs` | PlantModel assembly; also exposes `MaxAssociationDistanceFactor=30`, `AmbiguityMarginFactor=2` |
| `PlotGeometryBuilder` | `PlotGeometryBuilder.cs` | Label→geometry association with evidence tiers + ambiguity |
| `PlotSpatialBuilder` | `PlotSpatialBuilder.cs` | Explicit coordinate→segment matching (`PlotPlanSpatialSettings` tolerances 0.001) |
| `EngineeringSpatialSubjectBuilder` | `EngineeringSpatialSubjectBuilder.cs` | **Road/Fence/UnitBoundary subject construction**; `IsExplicitRoadEdge` deterministic text classifier; `ValidatePrimitive` |
| `PlotSpatialMeasurementBuilder` | `PlotSpatialMeasurementBuilder.cs` | **The measurement engine** — `BuildEngineeringSpatial` computes min point→segment distance over a primitive's segments |
| `DrawingUnitResolver` | `DrawingUnitResolver.cs` | meters↔drawing-units conversion |
| `EngineeringDistanceRuleRegistry` | `src/EngiSentry.Rules/EngineeringDistanceRuleRegistry.cs` | Rule registry with `Create` (standards-backed) / `CreateProjectCriteria` (project-only) paths |
| `EngineeringDistanceRuleEvaluator` | `EngineeringDistanceRuleEvaluator.cs` | **Constraint evaluator** — basis+applicability filtering, per-placement governing min, `Status = INSUFFICIENT_EVIDENCE \| PASS \| VIOLATIONS` |
| `EngineeringDistanceQaIntegrator` | `src/EngiSentry.Standards/EngineeringDistanceQaIntegrator.cs` | Evaluation → `QaReport` with strict state invariants + provenance enforcement |
| `ProjectDistanceRuleBinder` | `ProjectDistanceRuleBinder.cs` | The 3 bounded rule IDs + criteria→rule binding |
| `EngineeringDistanceCandidateCatalog` | `EngineeringDistanceCandidateCatalog.cs` | **Candidate *criteria*** catalog (see F.6 — naming collision) |
| `ProjectEngineeringDistanceRuntime` | `src/EngiSentry.AutoCAD2021/ProjectEngineeringDistanceRuntime.cs` | Runtime composition; `Overall()` already returns `FAIL \| REVIEW_REQUIRED \| INSUFFICIENT_EVIDENCE \| PASS` |
| `PlotPlanStandardsAuthorityRuntimeContextCatalog` | `PlotPlanStandardsAuthorityRuntimeContext.cs` | Deterministic P6 authority context (3 sources, 9 clauses, 4 rules) |
| `StandardAuthorityResolver` / `StandardAuthorityExecutionGate` / `StandardAuthorityReviewProjector` / `StandardAuthorityReviewPackageBuilder` | same-named files | Authority → gate → review projection → sidecar package |
| `StandardRulePackRegistry` / `StandardRulePackResolver` / `StandardSourceRegistry` / `StandardClauseRegistry` / `StandardFindingProvenanceEnricher` | same-named files | Pack/source/clause lifecycle + finding enrichment |
| `ProjectDistanceGovernanceRuntimeLoader` + the full P9 chain | `ProjectDistanceGovernanceRuntimeLoader.cs` + 15 files | Approval/history/currentness/trust resolution |
| `PlotPlanReportCompactor` | `Scanning/PlotPlanReportCompactor.cs` | Report projection; note `Create()` deliberately empties `Primitives` (compaction) while `CreatePlotPlanQaReport` keeps the full model |
| `VisualResultsPalette` + `PlotPlanDistancePresentation` | `VisualResultsPalette.cs` | **Read-only results UI** — 3 distance rows, authority review items, review package + SHA256 |
| `ProjectDistanceCriteriaStore` / `ProjectDistanceCriteriaDialog` | `EngiSentry.AutoCAD2021` | Criteria persistence + UX |
| `SnapshotFileWriter` | `Scanning/SnapshotFileWriter.cs` | Deterministic artifact naming/writing |

## C.3 Runtime acceptance harnesses

- `tests/UnitTests.cs:47 Main()` → `CrossDocumentTests.Run()`, `RevisionTests.Run()`, `ProfileTests.Run()`, `MeasurementTests.Run()`, `ClearanceTests.Run()`; final line printed on success: `All EngiSentry M2.2 through M5.0 tests passed.` (`UnitTests.cs:299`).
- Phase-specific sub-runners already exist: `ClearanceTests.RunP51…RunP92C2C`, `ProfileTests.RunP51…RunP85`, `MeasurementTests.RunP410QualificationTests`.
- Runtime (AutoCAD) acceptance is manual/scripted per handoff (ES_PLOTQA demand-load, DBMOD guard, DWG SHA256 before/after).

---

# D. Current DWG data capabilities and confidence

For the bounded use case `Equipment + Road + Plant Fence + Unit Boundary`, here is exactly what the existing pipeline can and cannot produce. Every row is traceable to code.

## D.1 Obtainable (with confidence basis)

| Needed | Obtained? | Source | Confidence mechanism |
|---|---|---|---|
| **Source entity (equipment)** | ✅ | `SemanticExtractor.ParseEquipment` → `EquipmentTag.SourceHandle/SourceObjectType/SourceLayer` (`SemanticExtractor.cs:64-70`) | Grammar-parsed (`EquipmentTagGrammar.TryParse`); `BLOCKNAME` text explicitly excluded |
| **Source entity (road/fence/boundary)** | ✅ | `TextEvidence.SourceHandle` / `BoundaryEvidence.SourceHandle` | Text-token classifiers (`RoadToken`, `FenceToken`, `UnitLimitToken`, `BatteryLimitToken`, `BoundaryToken`) at `SemanticExtractor.cs:18-22` |
| **Geometry (road/fence/boundary)** | ✅ | `PlotGeometryPrimitive` (handle, layer, GeometryKind, Closed, vertices) from `DrawingScanner.Geometry()` | Only `Line`/`Polyline`/`Polyline2d`/`Polyline3d`; **bulge ≠ 0 rejected → arcs/curves dropped** (`DrawingScanner.cs:302-304`); nested-block geometry transformed into model space |
| **Location (equipment)** | ⚠️ Partial — **a single 2D point** | `EquipmentPlacement.Easting/Northing.SelectedValue` — a decimal parsed from *coordinate text* near the tag, associated by proximity (`PlotPlanBuilder.FindCandidates`/`Associate`) | Status `Resolved`/`Missing`/`Ambiguous`; ambiguity margin = `2 × text height`; association window = `30 × text height`. **Not** the tag insertion point, and **not** any equipment geometry |
| **Footprint / representative geometry (equipment)** | ❌ **NOT AVAILABLE** | — | No equipment block geometry, extent, orientation, or bounding box is captured for measurement purposes. `BlockReferenceSnapshot` records position/rotation/scale but is **never used** by `PlotPlanBuilder` or the measurement builder |
| **Classification** | ✅ deterministic strings | `EngineeringDistanceEvidenceKeys` (basis + applicability); `EquipmentPlacement.EngineeringApplicabilityKey` = `Equipment` | `ClassifyTag` can return `Ambiguous` (`SemanticExtractor.cs:99-105`) |
| **Confidence** | ⚠️ Implicit only | `PlacementStatus`, `CoordinateAssociation.Status`, `GeometryAssociation.Status`, `SpatialCoordinateConstraint.Status`, `ParseStatus` (Parsed/Partial) | **No numeric confidence field exists anywhere.** Confidence is a tri-state (Resolved/Missing/Ambiguous) plus candidate lists |
| **Association evidence** | ✅ rich | `GeometryAssociation` (Status, Reason, SelectedGeometryHandle, Candidates), `CoordinateAssociation` (Candidates w/ distances), `EngineeringSpatialSubject.AssociationIds`/`LabelSourceHandles` | Full candidate short-list retained; deterministic tie-breaks (distance, then handle) |
| **Measurement basis** | ✅ explicit | `EngineeringSpatialSubject.EngineeringMeasurementBasisKey`; validated by `EngineeringDistanceEvidenceKeys.IsSupportedMeasurementBasisKey` | Mismatch is a hard error in the evaluator/integrator |
| **Observed distance** | ✅ | `EquipmentSpatialMeasurement.Distance2D` = min point→segment distance over the subject primitive's segments (`PlotSpatialMeasurementBuilder.BuildEngineeringSpatial`, lines 176-210); unit-converted at evaluation | Segment selection deterministic (min distance, then lowest index); non-finite values throw |
| **Boundary/road/fence evidence** | ✅ | Label handle + geometry handle + segment endpoints + closest point | `EngineeringDistanceViolation` carries all of these |
| **Units** | ✅ | `DrawingUnitResolver.Resolve(plotPlan.Drawing.InsertionUnits)`; `database.Insunits` read in `DrawingScanner.Scan` | Meters/drawing-units conversion recorded on every evaluation |
| **Revision/currentness of the drawing** | ✅ | `ES_REVBASELINE`/`ES_REVISION` (`RevisionEngine`) | Out of scope for Phase A/B but present |

## D.2 The single most important geometric limitation

**Equipment is a point, not a body.** `PlotSpatialMeasurementBuilder.IsEligiblePlacement` (lines 84-93) requires `PlacementStatus == "Complete"` with resolved E and N `SelectedValue`. The measurement is then `min over segments of point→segment distance`. Therefore:

- The "observed 2.2 m from Road Edge" in the SUGGEST example is **the distance from a coordinate-text-derived point to the nearest road segment** — not from the equipment's physical edge.
- Candidate positions are candidate **points**. Re-evaluation against Road/Fence/UnitBoundary is therefore a pure, self-contained geometry computation that needs **no AutoCAD access at all** — exactly the property the target architecture requires ("design engine independent of AutoCAD entities").
- But any claim like "move at least 2.8 m East" is only a **point-kinematics** statement. Without footprint/orientation it cannot certify real clearance for the physical equipment. This must be stated in every suggestion, and is the primary reason `EngineeringReviewRequired` must stay non-optional.

## D.3 Not reliably knowable from the DWG today

Plot boundary/property line, site north (no north-arrow entity is captured), equipment type/duty/hazard classification, equipment footprint/orientation/maintenance envelope, pipe racks, buildings, substations, tank farms, gates, drainage, fire/flare/escape requirements, underground utilities, wind/topography/elevation, and any pipe/nozzle/crane interaction. `docs/plotplan-v1.1/STANDARDS_AUTHORITY_MATRIX.md` independently confirms the policy side of this: flare siting is `EXPERT_REVIEW_REQUIRED`, maintenance clearance is "Usually EXPERT_REVIEW_REQUIRED", occupied-building siting is risk-based.

---

# E. Governance / Standards Authority reuse (P6/P7/P8/P9)

The new StandardsPack **must not** build a parallel engine. Below is the exact existing contract that remains authoritative for each required property.

| Required property (handoff §6) | Authoritative existing contract | Location |
|---|---|---|
| **Source** identity + hash | `StandardSourceDefinition` (SourceId, DocumentNumber, Title, Issuer, RevisionOrEdition, SourceFileName, **SourceSha256**) | `StandardSourceModels.cs:6` |
| **Revision** | `StandardSourceDefinition.RevisionOrEdition`; runtime sources: `trc.pcd-pd-135.rev1`, `ips.e-pr-190.rev1.2009`, `ips.g-sf-150.rev1.2023` | `PlotPlanStandardsAuthorityRuntimeContext.cs:71-79` |
| **Clause / page** | `StandardClauseDefinition` (ClauseId, SourceId, ClauseReference, ClauseText, Heading, **PageNumber**) | `StandardClauseModels.cs:6` |
| **Source hash** enforcement | `StandardClauseAuthorityResolution.SourceSha256` (P7.1 enrichment); `EngineeringDistanceApprovalEvidenceBinding.SourceSha256`; `HumanEngineeringReviewEvidenceBinding.SourceSha256` | `StandardAuthorityResolutionModels.cs:21`, `EngineeringDistanceApprovalRecordModels.cs:22`, `HumanEngineeringReviewDecisionModels.cs:15` |
| **Applicability** | `EngineeringDistanceEvidenceKeys` (basis + applicability constants + validators); enforced in `EngineeringDistanceRuleRegistry.ValidateRule`, `EngineeringDistanceQaIntegrator.ValidateViolations` | `EngineeringDistanceRuleModels.cs:27-48` |
| **Approval** (project criteria) | `EngineeringDistanceApprovalRecord` + `EngineeringDistanceApprovalGate.TryCreateExecutableRule` (only `APPROVED` + `ApprovalReference` + matching effective rule + matching ClauseIds yields an executable rule) | `EngineeringDistanceApprovalGate.cs:15-47` |
| **Authority** | `StandardAuthorityPolicyDefinition` (PolicyId, SourceId, ClauseId, AuthorityClass, EnforcementMode, Rationale) + `StandardAuthorityResolver` + `StandardAuthorityExecutionGate` (`AutoExecutionAllowed` = mode == `DETERMINISTIC`) | `StandardAuthorityModels.cs:36`, `StandardAuthorityExecutionGate.cs:15` |
| **Currentness / staleness** | `EngineeringDistanceApprovalCurrentnessValidator.Evaluate` → `CURRENT`/`STALE` with reasons `PROJECT_SCOPE_CHANGED`, `CANDIDATE_MISSING`, `CANDIDATE_DEFINITION_CHANGED`, `EVIDENCE_BINDINGS_CHANGED`; `HumanEngineeringReviewDecisionStalenessValidator` (`SCAN_TIMESTAMP_CHANGED`, `REVIEW_PACKAGE_SHA_CHANGED`) | `EngineeringDistanceApprovalCurrentnessModels.cs`, `HumanEngineeringReviewDecisionStalenessValidator.cs:15` |
| **Executability** | `StandardRuleExecutionGateDecision.AutoExecutionAllowed`; accepted runtime state: **0 auto-executable**, flare = `SOURCE_QUARANTINED` / `NEVER_AUTO_ENFORCE` | `StandardAuthorityExecutionModels.cs:11`, handoff §P6/P7 |
| **Engineering review** | `HumanEngineeringReviewDecisionRecord` (immutable, SHA256-bound, append-only, `SupersedesDecisionRecordId` chain) + `StandardAuthorityReviewProjection`. **Handoff is explicit: "REVIEW_ACCEPTED IS NOT APPROVED"** — zero automatic engineering effect | `HumanEngineeringReviewDecisionModels.cs:23`, `StandardAuthorityReviewModels.cs` |
| **Pack precedence** | `StandardRulePackPrecedenceDefinition` + `StandardRulePackResolver.Resolve` → `EffectiveStandardRuleDefinition` (WinningPackId, OverriddenPackIds) | `StandardRulePackResolutionModels.cs` |
| **Conflict handling** | `STANDARD_CONFLICT` / `EXPERT_REVIEW_REQUIRED` rather than an invented answer; quarantine for flagged sources (PCD-PD-135 flare terms, IPS E-PR-190 legacy) | `STANDARDS_AUTHORITY_MATRIX.md:44-45,51` |

**Design rule for the new StandardsPack:** it should be a *new pack definition* consumed by the existing `StandardRulePackRegistry` → `StandardRulePackResolver` → `StandardAuthorityResolver` → `StandardAuthorityExecutionGate` chain, and its rules must pass the same `EngineeringDistanceRuleRegistry.Create(standardsResolution, rules)` gate that already rejects rules not present in `EffectiveRules`. The `EngineeringDistanceCandidateCatalog.Create(clauseRegistry, candidates)` → `ToStandardRulePacks()` path is the existing, proven route for turning governed candidates into packs with clause bindings — it should be reused, not duplicated.

---

# F. Gap matrix

Legend: **E/R** EXISTS / REUSABLE · **E/N** EXISTS / NEEDS EXTENSION · **P** PARTIALLY EXISTS · **M** MISSING

| # | Area | Class | Evidence & notes |
|---|---|---|---|
| F.1 | **PlantModel v1** | **E/N** | `PlotPlanModel` (`PlotPlanModels.cs:7`) already holds equipment, geometry, subjects, measurements, statistics. **Needs extension** only for: explicit plot boundary, units list, and a place to hang constraint results. It must NOT be renamed or re-rooted — `PlotPlanBuilder`, `PlotPlanReportCompactor`, `QaEngine`, `RevisionEngine`, and every test depend on this exact type. |
| F.2 | **Facility/equipment taxonomy** | **P** | Deterministic string taxonomy exists via `EngineeringDistanceEvidenceKeys` (5 measurement bases, 3 applicability classes) and `SemanticExtractor.ClassifyTag` (Equipment/Structure/`Ambiguous`). `StructureTag.TypeCode`/`AreaOrUnitCode` and `EquipmentTag.EquipmentPrefix` give raw material. **Missing:** a facility taxonomy covering buildings, pipe racks, substations, tanks, gates; and any numeric confidence score (today confidence is only Resolved/Missing/Ambiguous). |
| F.3 | **AutoCAD → PlantModel builder** | **E/N** | `PlotPlanBuilder.Build` + `DrawingScanner` + `SemanticExtractor` + `EntityIndexer` are exactly this adapter. The dependency boundary is already clean: `PlotPlanModel`/`EngineeringDrawingModel`/`PlotGeometryCapture` are pure `[DataContract]` contracts in `EngiSentry.Contracts` (netstandard2.0), and `PlotPlan`/`Rules`/`Standards` never reference Autodesk assemblies. **Needs extension** only to emit a richer equipment representation (and, later, non-AutoCAD sources). `AutoCADHostBoundary.IsCoreConsole()` (`Commands.cs:381`) already isolates host-specific behaviour. |
| F.4 | **StandardsPack contract** | **E/N** | `StandardRulePackDefinition` (PackId, Name, Rules→RuleId/Title/ClauseIds) + precedence + resolution exist and are runtime-proven. **Missing fields vs handoff §6:** `Company`, `Requirement text` (partially in `ClauseText`), `SubjectType`/`ReferenceType` (partially in basis/applicability keys), `MeasurementBasis` (exists as key), `Applicability and Exceptions`, `AuthorityLevel` (exists as AuthorityClass), `EngineeringReviewRequired`, `Evidence` (exists via clause/source bindings), `Currentness/Staleness` (exists for approvals, not for pack rows). |
| F.5 | **Generic constraint model** | **P** | `EngineeringDistanceRuleDefinition` is a *distance* constraint only: 2 `MeasurementKind`s, `MinimumDistanceMeters`, basis + applicability keys. The handoff's constraint *treatment* vocabulary (`HARD_CONSTRAINT`, `SAFETY_CONSTRAINT`, `PROJECT_REQUIREMENT`, `ENGINEERING_PREFERENCE`, `OPTIMIZATION_OBJECTIVE`, `REVIEW_REQUIRED`) has **no corresponding enum/type anywhere** — grep finds none. Result vocabulary, by contrast, is complete (`StandardDecisionStates`). |
| F.6 | **Candidate position model** | **M** | **⚠ Naming trap.** `EngineeringDistanceCandidateDefinition` (`EngineeringDistanceCandidateModels.cs:7`) is a candidate **rule/criterion** (a proposed minimum distance awaiting engineering approval), NOT a candidate equipment position. There is **no** type anywhere holding candidate position, direction of movement, delta, re-evaluated constraint results, or rank. Introducing a `Candidate*` type without disambiguating it from this existing one is a serious collision risk — 15+ P9 files and ~30 tests reference the existing meaning. |
| F.7 | **Constraint evaluator** | **E/R** | `EngineeringDistanceRuleEvaluator.Evaluate` is the working evaluator with strict state invariants (`INSUFFICIENT_EVIDENCE` when `governing.Count == 0`). Reusable as-is for CHECK. Extension needed only to emit the 5-state result set and to accept candidate positions. |
| F.8 | **Suggestion generator** | **M** | Nothing generates, scores, or explains a proposed equipment movement. The needed primitives exist separately: distance math (`PlotSpatialMeasurementBuilder`), observed-value explanation (`EngineeringDistanceViolation`), requirement explanation (`StandardClauseCitation`), and required/actual deltas — but no generator wires them. |
| F.9 | **Candidate re-evaluator** | **M** | No component re-runs Road/Fence/UnitBoundary evaluation for a hypothetical position. The core math is reusable but currently **private** inside `PlotSpatialMeasurementBuilder.BuildEngineeringSpatial` (lines 176-210) and bound to whole `EquipmentPlacement`s. Needs a pure, placement-free helper: `min point→polyline distance`. |
| F.10 | **Candidate ranker** | **M** | No ranking component. Existing precedent to copy: deterministic ordering by (distance, then handle) throughout `PlotPlanBuilder`/`PlotSpatialBuilder`; and the "mandatory first, then declared objective" pattern mirrors the existing precedence model in `StandardRulePackResolver`. |
| F.11 | **Proposal/explanation contract** | **P** | Strong precedent: `StandardAuthorityReviewItem` (RuleId, mode, DecisionState, Message, Clauses), `PlotPlanDistancePresentationRow` (Name, Minimum, Observed, DisplayStatus, Interpretation), and the P10.2 wording rules in `GovernanceDisplay`. Missing: candidate/explanation contract, "checked vs not checked" lists, assumptions, rejected candidates. |
| F.12 | **Read-only proposal projection/UI** | **E/N** | `VisualResultsPalette` + `PlotPlanDistancePresentation` + `InteractiveVisualResultsPalette` are a working read-only surface (read-only grid proven in P6/P8). Extension needed to render proposals; must keep `Publish()` non-mutating and retain the `AutoCADHostBoundary.IsCoreConsole()` bypass for headless tests. |

---

# G. Missing-data / insufficient-evidence matrix

Cases that must remain unresolved rather than guessed. Each maps to an existing state that already exists and must be preserved.

| Situation | Must report | Existing mechanism to reuse |
|---|---|---|
| No resolved equipment placement (E or N unresolved/ambiguous) | `INSUFFICIENT_EVIDENCE` | `PlotSpatialMeasurementBuilder.IsEligiblePlacement` excludes the placement → `governing.Count == 0` → evaluator sets `INSUFFICIENT_EVIDENCE` (`EngineeringDistanceRuleEvaluator.cs:107`) |
| No resolved Road/Fence/UnitBoundary subject for the required basis | `INSUFFICIENT_EVIDENCE` | same path; `PlotGeometryStatistics.MissingAssociationCount` and `MissingSpatialConstraintCount` already surface the gap, and `PP-GEO-001`/`PP-SPAT-001` QA rules flag it |
| Road label present but not an explicit "ROAD EDGE" phrase | not treated as AccessRoadEdge | `EngineeringSpatialSubjectBuilder.IsExplicitRoadEdge` — closed allow-list; must not be loosened |
| Geometry has curves (bulge ≠ 0) | entity silently excluded from geometry evidence | `DrawingScanner.Geometry()` returns null → association `Missing` |
| Ambiguous coordinate or geometry association | `Ambiguous` (no value selected) | `PlotPlanBuilder.Associate`, `PlotGeometryBuilder`, `PlotSpatialBuilder` retain candidate lists; `PP-COORD-002`, `PP-GEO-002`, `PP-SPAT-002` report them |
| Rule source not authorized / pack not resolved | `SOURCE_REQUIRED`-equivalent | `EngineeringDistanceRuleRegistry.Create` throws "references unknown effective RuleId"; `EngineeringDistanceApprovalGate` returns `false` for `PENDING`/`REJECTED`/`REFERENCE_ONLY` |
| Source quarantined (e.g. flare terms) | `SOURCE_QUARANTINED`, never auto-enforced | `StandardAuthorityExecutionGate.DecisionState`; `AutoExecutionAllowed = false` |
| Conflicting / conditional requirements | `EXPERT_REVIEW_REQUIRED` / `STANDARD_CONFLICT` | `StandardDecisionStates`; `STANDARDS_AUTHORITY_MATRIX.md:51` |
| Rule not applicable to this equipment class | `NOT_APPLICABLE` | `StandardDecisionStates.NotApplicable`; `PlotPlanRuleBase.NotApplicable()` |
| Equipment footprint/orientation unknown (always, today) | candidate suggestions must state point-kinematics limitation | **NEW** — must be added to the proposal contract; no existing field |
| Pipe routing, maintenance access, underground utilities, crane, nozzle, process layout | "Not checked" | **NEW** — explicit unchecked-constraint list in the proposal contract |
| Missing north / plot boundary / site constraints | `INSUFFICIENT_EVIDENCE` for any future constraint needing them | no captured entity exists today |

---

# H. Regression risks

Introducing PlantModel / StandardsPack / Suggestion concepts can break accepted behaviour in these specific, verified ways:

**H.1 Plot Plan QA result contract.** `Commands.PlotPlanQa` uses `evaluations.Single(...)` on exactly the three rule IDs `project.plotplan.equipment.road.minimum-clearance`, `…equipment.plant.minimum-clearance`, `…equipment.unit-boundary.minimum-clearance` (`Commands.cs:175-177`). `ProjectEngineeringDistanceRuntime` requires `baseQa.EngineeringDistanceSummary == null` and enforces unique RuleId/FindingId across the merged report. **Any new rule/pack that adds a RuleId colliding with these, or that changes `EngineeringDistanceRunSummary` shape, breaks ES_PLOTQA at runtime.**

**H.2 Strict state invariants.** `EngineeringDistanceQaIntegrator.ValidateStatus` asserts `PASS ⇒ EvaluatedSubjectCount > 0 && ViolationCount == 0`, `VIOLATIONS ⇒ both > 0`, `INSUFFICIENT_EVIDENCE ⇒ both == 0`; `ValidateViolations` requires unique PlacementId per rule and exact basis/applicability match. A new 5-state result vocabulary that flows into `QaRuleResult.Status` **will** trip these. New states must be introduced at the *proposal* layer, not by extending `EngineeringDistanceRuleEvaluation.Status`.

**H.3 JSON contract of the accepted artifacts.** `PlotPlanQaReport.ReviewProjection` is `[IgnoreDataMember]` (`PlotPlanModels.cs:79`) — this is an accepted safety guarantee ("ReviewProjection serialized in PlotQA JSON: No"). Adding any new `[DataMember]` to `PlotPlanQaReport`, `QaReport`, `EngineeringDistanceRunSummary`, or the distance models changes the emitted PlotQA JSON and invalidates accepted artifacts and P8's SHA256-bound review packages.

**H.4 Review-package hash stability.** `HumanEngineeringReviewDecisionFileStore` binds the review package SHA256; `StandardAuthorityReviewPackageBuilder` normalizes `ScanTimestampUtc` to whole milliseconds precisely so hashes are reproducible (P8.6). Any change to the package graph, field order, or serializer settings retroactively invalidates the accepted A/B/C decision chain.

**H.5 Determinism.** Ordering is (distance, then handle, then segment index) throughout, and `StandardAuthorityReviewPackageBuilder` uses deterministic RuleId/ClauseId ordering. Candidate generation/ranking must follow the same discipline (no `DateTime.UtcNow`, no random IDs, no dictionary iteration order) or artifacts become non-reproducible.

**H.6 Geometry association behaviour.** `PlotGeometryBuilder`/`PlotSpatialBuilder` rely on `MaxAssociationDistanceFactor = 30 × label text height` and `AmbiguityMarginFactor = 2 × text height`. Reusing these for candidate re-evaluation is fine; *changing* them changes every accepted measurement.

**H.7 Governance invariants that must hold.** Accepted runtime state: authority/gate/review = 4/4/4, **auto-executable = 0**, 0 authority-generated findings/rule-results, `PROJECT_RULE_ONLY` for Road and Fence. A new StandardsPack that produces an auto-executable rule or an authority-generated finding would violate the accepted P6/P7/P9 guarantees and the pilot record.

**H.8 Read-only / DWG safety.** `DBMOD` guards are in `DrawingScanner` and in every command. Suggestion code must never open the drawing for write; it should be pure and run off `PlotPlanModel`. `AutoCADHostBoundary.IsCoreConsole()` must remain the headless-test escape hatch.

**H.9 Test harness coupling.** `ClearanceTests`/`ProfileTests` assert exact evaluation statuses, counts, and provenance shapes. New contracts that alter `EngineeringDistanceRuleEvaluation`, `EngineeringDistanceViolation`, or `QaEvidence` will fail P51–P92C2C and P51–P85 runners.

**H.10 Uncommitted-baseline risk (amplifies all of the above).** Because P8/P9 + P10.2 are untracked and the 2 local commits are unpushed, a `git clean`/checkout — or a merge resolved the wrong way — silently destroys the accepted governance layer. **This is the single largest near-term risk to the baseline and should be resolved before new feature work.**

---

# I. Proposed target architecture mapped to existing code

The handoff's target chain, placed on top of what already exists. **Bold = new; everything else exists.**

```
AutoCAD adapter / existing extraction
   DrawingScanner.Scan(capturePlotGeometry:true)        [E/R, no change]
   SemanticExtractor.Extract                            [E/R, no change]
   EntityIndexer.Build                                  [E/R, no change]
        ↓ (pure contracts — already host-free, netstandard2.0)
→ PlantModel builder
   PlotPlanBuilder.Build → PlotPlanModel               [E/R as-is]
   PlantModel v1 = PlotPlanModel + extension surface   [E/N — additive only]
   EquipmentPoint/representative-geometry projection   [NEW, thin]
   Facility taxonomy (Road/Fence/UnitBoundary/Equipment first)  [E/N over EngineeringDistanceEvidenceKeys]
→ governed Standards Pack resolver
   StandardSourceRegistry / StandardClauseRegistry      [E/R — authoritative]
   StandardsPack v1 definition (governed, source-backed) [E/N: new pack rows, reuse StandardRulePackDefinition]
   StandardRulePackRegistry → StandardRulePackResolver [E/R — authoritative]
   StandardAuthorityResolver → StandardAuthorityExecutionGate [E/R — authoritative]
   Constraint treatment (HARD/SAFETY/PROJECT/PREFERENCE/OBJECTIVE/REVIEW) [NEW enum, separate from source rule]
→ Constraint evaluator
   EngineeringDistanceRuleEvaluator                     [E/R for CHECK]
   ConstraintResult (SATISFIED/VIOLATED/INSUFFICIENT_EVIDENCE/NOT_APPLICABLE/REVIEW_REQUIRED) [NEW — maps onto StandardDecisionStates, does NOT replace it in QaReport]
→ Suggestion generator
   ObservedExplanation  (from EngineeringDistanceViolation + EquipmentSpatialMeasurement) [NEW]
   GovernedRequirementExplanation (from StandardClauseCitation) [NEW]
   **CandidatePosition** { PlacementId, candidate E/N, basis, delta vector }   [NEW — named to avoid the EngineeringDistanceCandidate* collision]
   CandidateGenerator (min-residual direction along the violation's normal)   [NEW — pure geometry]
→ Candidate re-evaluator / ranker
   PointToPolylineDistance (extracted from PlotSpatialMeasurementBuilder) [NEW public pure helper, E/N source]
   CandidateReEvaluator: re-run Road/Fence/UnitBoundary per candidate        [NEW]
   CandidateRanker: mandatory constraints first, then declared objectives    [NEW]
→ Read-only report / Results UI
   ProposalProjection { checked, unchecked, assumptions, rejected, review-required } [NEW contract]
   VisualResultsPalette / PlotPlanDistancePresentation  [E/N — additive rows only]
   PlotPlanReportCompactor                              [E/R, no change]
```

**Key architectural constraint already satisfied:** the design engine *never* touches Autodesk types. `EngiSentry.PlotPlan`, `EngiSentry.Rules`, and `EngiSentry.Standards` have zero `using Autodesk.*` — only `EngiSentry.AutoCAD2021` references the host. This is exactly the separation the handoff asks for, and it means the SUGGEST engine can be unit-tested headlessly in `EngiSentry.Tests` (which already runs `Main()` without AutoCAD, and which `Publish()` already short-circuits via `AutoCADHostBoundary.IsCoreConsole()`).

---

# J. Smallest safe Phase A/B implementation plan

## Phase A — contracts and gates only, no production behaviour change

1. **New file** `src/EngiSentry.Contracts/ConstraintTreatmentModels.cs` — the 6 treatment values (`HARD_CONSTRAINT` … `REVIEW_REQUIRED`) as a `static class` of `const string`, mirroring the existing `StandardDecisionStates` style. No enum (the codebase uses string constants + `StringComparer.Ordinal` everywhere).
2. **New file** `src/EngiSentry.Contracts/ProposalModels.cs` — `CandidatePosition`, `CandidateConstraintResult`, `ProposalProjection`, `UncheckedConstraint`, plus `ProposalExplanation` (observed / required / basis / delta). All `[DataContract]`, all with `Order =` data members, all defaulting to non-null.
3. **New file** `src/EngiSentry.Contracts/PlantModelExtensions.cs` (or extend `PlotPlanModels.cs`) — the minimal PlantModel v1 extension surface. Prefer **additive nested collections** on `PlotPlanModel` rather than editing existing fields.
4. **New file** `src/EngiSentry.PlotPlan/PointToPolylineDistance.cs` — a public pure helper containing the exact algorithm currently private in `PlotSpatialMeasurementBuilder` (segment iteration, clamped `t`, min distance, deterministic tie-break). `PlotSpatialMeasurementBuilder` then delegates to it — same outputs, no behaviour change.
5. **New test file** `tests/ProposalContractTests.cs` + register it in `tests/EngiSentry.Tests.csproj` line 29 (classic csproj — **easy to miss**). Cover: contract round-trip serialization; point→polyline identity against known `BuildEngineeringSpatial` outputs; the 5 result states; and that a candidate with no resolved subjects yields `INSUFFICIENT_EVIDENCE`.
6. **Non-regression gate:** run `EngiSentry.Tests` (all 5 runners) on Windows and confirm the final line is still `All EngiSentry M2.2 through M5.0 tests passed.` plus the new runner. Confirm `Release|x64` build = 0 warnings / 0 errors.

Phase A delivers **no user-visible change**, no new RuleId, no JSON contract change, and no change to accepted artefacts.

## Phase B — read-only PlantModel extraction for the bounded use case

1. **New** `PlantModelProjection` in `EngiSentry.PlotPlan`: `PlotPlanModel → PlantModel v1`, carrying Equipment (point + applicability + placement status), Road/Fence/UnitBoundary subjects (`EngineeringSpatialSubject`), and per-subject geometry. **Pure, host-free, deterministic.** It reads only existing fields, so `ES_PLOTQA` output is unchanged.
2. **New** `ConstraintEvaluator` wrapper that runs the existing `EngineeringDistanceRuleEvaluator` and additionally projects results into `ProposalProjection`'s "checked" list, leaving "unchecked" explicitly populated (pipe routing, maintenance access, underground utilities, footprint-based clearance) as static, declared limitations.
3. **New** read-only diagnostic surface (a `PlantModelDiagnostics` counts block: placements complete/incomplete/ambiguous, subjects resolved per basis, missing geometry) — mirroring `PlotGeometryStatistics`, which already exists and can be reused directly.
4. **Wire a headless-only command path or extend `ES_PLOTQA` output with a strictly additive sidecar** (e.g. `<drawing>.<ts>.plantmodel.json`) written with `FileMode.CreateNew`, reusing the snapshot timestamp — exactly the P7.2 pattern. Do **not** extend `PlotPlanQaReport`'s serialized members.
5. **Tests:** `PlantModelProjectionTests` asserting byte-identical measurements vs the existing `PlotSpatialMeasurementBuilder` outputs on the same fixture inputs; `INSUFFICIENT_EVIDENCE` cases preserved 1:1.
6. **Gates:** full harness green; `Release|x64` 0/0; DWG SHA256 unchanged before/after on the protected fixture; DBMOD unchanged.

Phases C (one governed CHECK pack) and D (explainable SUGGEST) follow only after A and B are approved and committed.

---

# K. Exact files that would need to be added or modified later

**New (Phase A)**
- `src/EngiSentry.Contracts/ConstraintTreatmentModels.cs`
- `src/EngiSentry.Contracts/ProposalModels.cs`
- `src/EngiSentry.PlotPlan/PointToPolylineDistance.cs`
- `tests/ProposalContractTests.cs`

**New (Phase B)**
- `src/EngiSentry.PlotPlan/PlantModelProjection.cs`
- `src/EngiSentry.PlotPlan/PlantModelDiagnostics.cs` (or extend `PlotGeometryStatistics` usage)
- `src/EngiSentry.Rules/ConstraintEvaluator.cs` (thin wrapper, delegates to `EngineeringDistanceRuleEvaluator`)
- `src/EngiSentry.AutoCAD2021/Scanning/PlantModelSnapshotWriter.cs` (sidecar writer)
- `tests/PlantModelProjectionTests.cs`

**Modified (Phase A/B) — each change is additive**
- `src/EngiSentry.PlotPlan/PlotSpatialMeasurementBuilder.cs` — delegate to `PointToPolylineDistance`; **behaviour must stay identical**.
- `src/EngiSentry.Contracts/PlotPlanModels.cs` — additive PlantModel v1 surface only; **never** alter existing `[DataMember]` order.
- `src/EngiSentry.AutoCAD2021/Commands.cs` — optional sidecar emission inside `ES_PLOTQA`, after the existing DBMOD guard, mirroring the review-package pattern.
- `src/EngiSentry.AutoCAD2021/VisualResultsPalette.cs` — additive read-only proposal rows; keep `Publish()` non-mutating; keep `IsCoreConsole()` bypass.
- `tests/EngiSentry.Tests.csproj` — **must** add each new test file to the explicit `<Compile Include>` list.
- `docs/handoffs/…` — audit/test records.

**Explicitly NOT modified in Phase A/B**
`DrawingScanner.cs`, `SemanticExtractor.cs`, `EntityIndexer.cs`, `PlotPlanBuilder.cs`, `PlotGeometryBuilder.cs`, `PlotSpatialBuilder.cs`, `EngineeringSpatialSubjectBuilder.cs`, `EngineeringDistanceRuleEvaluator.cs`, `EngineeringDistanceRuleRegistry.cs`, `EngineeringDistanceQaIntegrator.cs`, `ProjectEngineeringDistanceRuntime.cs`, `PlotPlanReportCompactor.cs`, `StandardAuthorityReviewPackageBuilder.cs`, and the entire P8/P9 governance layer. Any DWG.

---

# L. Proposed test and non-regression gates

**L.1 Automated harness (Windows, `EngiSentry.Tests`, must stay green)**
- Final line invariant: `All EngiSentry M2.2 through M5.0 tests passed.` — extended for new runners only, never weakened.
- Existing runners untouched: `UnitTests`, `ProfileTests` (P51–P85 incl. P8.3H append-only history), `MeasurementTests` (P410 qualification), `ClearanceTests` (P51–P92C2C incl. P9.2C2A signed-policy trust), `CrossDocumentTests`, `RevisionTests`.
- New gates: contract round-trip serialization; `PointToPolylineDistance` parity with `BuildEngineeringSpatial` on identical inputs (this is the regression canary for measurement behaviour); PlantModel projection measurement identity; `INSUFFICIENT_EVIDENCE` preservation matrix (Section G) as an explicit parameterized test.

**L.2 Build gate**
- `Release|x64`, 0 warnings, 0 errors (the standard accepted in P7/P10.2).
- No new dependency; `AGENTS.md` forbids unnecessary dependencies.

**L.3 Runtime / AutoCAD gates (per accepted practice)**
- `ES_PLOTQA` demand-load and normal command return on AutoCAD 2021 x64.
- DBMOD unchanged after processing and after artifact write.
- Protected DWG SHA256 before == after (`3D5BCC2D…F19DC4` and the other three pilot fixtures).
- Authority/gate/review remains 4/4/4; **auto-executable remains 0**; authority-generated findings/rule-results remain 0; Road and Fence governance remains `PROJECT_RULE_ONLY`.
- `ReviewProjection` must remain absent from PlotQA JSON; any new sidecar must be a separate file.

**L.4 Contract-stability gate (new, recommended)**
- A test that serializes the accepted PlotQA/review-package fixture graph and asserts byte-identical output after each phase. This directly protects H.3/H.4 and makes any accidental `[DataMember]` addition fail loudly instead of silently invalidating accepted artifacts and P8 decision hashes.

**L.5 Determinism gate**
- No `DateTime.UtcNow` (use the existing snapshot timestamp), no random IDs, no `Dictionary`/`HashSet` iteration in outputs — all ordering via explicit `OrderBy(..., StringComparer.Ordinal)`.

---

# M. Blockers / decisions requiring user approval

**M.1 ⚠ The uncommitted accepted baseline (highest priority).**
P8 + P9 + P10.2 are untracked; P6 + P7 are committed locally but unpushed. The handoff calls these CLOSED/PASS, yet `eb6efbc` alone cannot build them. **Decision required:** commit and push (or at minimum commit) the working tree as a single verified baseline snapshot before any new work — or explicitly instruct that new work continues on top of the dirty tree. I have taken no action either way; per the rules I will not commit, push, or change git state without approval.

**M.2 CRLF/LF churn.** The working tree mixes CRLF and LF, inflating every diff and making real review changes hard to see (Section A.2). **Decision required:** whether to normalize line endings (a one-time, whole-tree reformat) as part of M.1, or leave as-is.

**M.3 P10.3 administrative closeout.** Requires a qualified engineer to complete the existing observation template for ≥3 of the 4 pilot drawings and record the engineering-review outcome. No code or DWG action. **Decision required:** who performs and signs it, and whether it must close before Phase A begins.

**M.4 Naming of the candidate-position concept.** `EngineeringDistanceCandidate*` is already taken by the P9 candidate-*criteria* model and is referenced by ~15 governance files and many tests (Section F.6). **Decision required:** approve a distinct name (e.g. `CandidatePosition` / `PlacementCandidate`) so the two never collide.

**M.5 Point-vs-footprint stance for SUGGEST.** Equipment is measurable only as a point today (Section D.2). **Decision required:** confirm that Phase D suggestions are explicitly labelled as point-kinematics with `EngineeringReviewRequired`, and that "compute candidate positions" means candidate points — or authorize adding equipment footprint/orientation extraction (a larger `DrawingScanner` + `PlotPlanBuilder` change) first.

**M.6 The 5-state result vocabulary.** `StandardDecisionStates` already carries PASS/FAIL/ADVISORY/EXPERT_REVIEW_REQUIRED/STANDARD_CONFLICT/NOT_APPLICABLE/INSUFFICIENT_EVIDENCE/SOURCE_QUARANTINED, while the handoff asks for SATISFIED/VIOLATED/… . **Decision required:** confirm the proposal layer uses its own states mapped *onto* the existing vocabulary (my recommendation) rather than extending `EngineeringDistanceRuleEvaluation.Status`, which would break the accepted `ValidateStatus` invariants (H.2).

**M.7 First governed StandardsPack source.** Phase C needs one authorized pack. The existing governed sources are `trc.pcd-pd-135.rev1`, `ips.e-pr-190.rev1.2009` (LEGACY_CONFLICT), `ips.g-sf-150.rev1.2023`, with flare terms quarantined. **Decision required:** which source/clause set is authorized for the first CHECK pack, and who supplies the verified clause/page/requirement text — none may be generated from model memory.

**M.8 Toolchain for verification.** This Linux session has no .NET build tooling (Section A.4). **Decision required:** confirm that all build/test/runtime gates in L run on the Windows host via the user's environment, and that I should not attempt to compile here.

---

## Status

Audit complete. **No code, tests, DWG, or git state were modified.** The only artifact produced is this report.

**Stopping here and waiting for architecture approval**, as instructed. No implementation is authorized in this step.

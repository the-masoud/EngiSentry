# EngiSentry Pilot 0503 — Accepted ES_PLOTQA Verdicts (2026-09-25)

## Baseline

- Commit: `b14eb08` — Count distance coverage by canonical equipment (local, no push).
- Command: `ES_PLOTQA` on a fresh pilot copy.
- Fixture: `artifacts/runtime-validation/fixture/1003-00-EB-PI-PLP-0503-A1-canonical-coverage-fresh.dwg`.
- QA artifacts: `artifacts/qa/plotplan/1003-00-EB-PI-PLP-0503-A1-canonical-coverage-fresh.dwg.20260925T104820773Z.{plotplan.qa,standards.review}.json`.
- Build/tests: Release x64 build and full test suite PASS (reported with the run).

## Accepted verdicts

| Rule | Status | Applicable / Evaluated / Unresolved |
|------|--------|--------------------------------------|
| Road | INSUFFICIENT_EVIDENCE | 19 / 5 / 14 |
| Fence | VIOLATIONS | 19 / 5 / 14 |
| UnitBoundary | INSUFFICIENT_EVIDENCE | 19 / 0 / 19 |

Duplicate DrawingTag QA behavior is unchanged. Coverage counts distinct
canonical `EquipmentEntityId` values; equivalent Complete occurrences with
identical coordinate pairs are deduplicated, conflicting pairs remain
unresolved.

## Why UnitBoundary 19/0/19 is correct (not a defect)

Evidence inventory from the accepted QA JSON:

- 82 engineering spatial subjects: 81 Road/AccessRoadEdge (ModelLayer) + 1
  Boundary/SecurityFence (ModelLayer, polyline `5836EA` on layer `FENCE`).
  Zero `UnitBoundary`-basis subjects.
- 410 spatial measurements: 405 Road + 5 SecurityFence. Zero UnitBoundary
  measurements, so no canonical entity can evaluate and none may pass.
- Drawing content: 194 layers with no unit-boundary model geometry; none of
  the 271 texts matches `UNIT LIMIT` or `BATTERY LIMIT`
  (`SemanticExtractor` unit-limit/battery-limit tokens). The only boundary
  subject path available to this drawing — the `FENCE` layer polyline — is
  correctly consumed by the Fence rule (5 evaluated, 2 violations).
- Fail-closed behavior is pinned by `tests/ClearanceTests.cs:493-495`:
  UnitBoundary without a valid subject stays INSUFFICIENT_EVIDENCE with
  EvaluatedPlacementCount 0 and Unresolved == applicable. A false PASS here
  would be the defect; the reported verdict is the specified behavior.

## DWG integrity

- `ES_PLOTQA` reported DBMOD unchanged; source and working-copy DWG hashes
  both remained `7E51EC0BDCE424DEFA46AFC5B63EA63ED4CBEB19A7785A79E0D198DC972BD807`
  (locally re-verified on the fixture copy).

## Known limitations (deferred, not defects of this run)

- `LayerBasis()` recognizes no unit-boundary layer: explicit unit-boundary
  geometry without a `UnitLimit` label cannot qualify (road/fence can).
- `BatteryLimit` labels are classified but never converted to subjects
  (subject builder handles `"Fence"` and `"UnitLimit"` only).
- Either change is a scoped product decision for a drawing that actually
  carries unit-boundary evidence; this pilot drawing carries none.

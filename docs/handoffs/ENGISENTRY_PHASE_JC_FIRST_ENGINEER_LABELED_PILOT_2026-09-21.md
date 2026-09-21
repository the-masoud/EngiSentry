# EngiSentry Phase J-C — First Engineer-Labeled Pilot Benchmark

Date: 2026-09-21

## Status

**PASS / CLOSED**

J-C was executed as a two-sample workflow pilot only. It is not statistical calibration and it does not select or activate any production confidence threshold.

## Repository

- Repository: `C:\Users\M.Zeynali\Documents\EngiSentry`
- Branch: `master`
- Base HEAD: `8d1086ec9abd5180b9061053b61627761f8f6000`
- Production source modified: **NO**
- Tests modified: **NO**
- Build performed: **NO**
- Regression tests performed: **NO**
- Push performed: **NO**

Protected untracked paths were preserved and were not staged:

- `.vscode/`
- `artifacts/`
- `docs/handoffs/P10.3-SUPERVISED-ENGINEER-PILOT-RECORD.md`
- `packaging/installer/`

## Calibration Package

CalibrationPackageId:

`EC0860837579DAA03C847959254E908C542433D0C486D24AB256A60E371F93AD`

Directory:

`C:\Users\M.Zeynali\AppData\Local\EngiSentry\artifacts\qa\jev-calibration\EC0860837579DAA03C847959254E908C542433D0C486D24AB256A60E371F93AD`

Calibration cases SHA-256:

`0992C6413FDA9B5653880AF65BEE21087E4DF03D027DDF2BD43037625AD9F537`

Engineer labels SHA-256:

`E48CFAB0B2B97F8650D4E97D532CAFD9BFA93FEBE7FACCD10B8A4888D3BF7D67`

Benchmark report SHA-256:

`A133EE9334561B2CC20B93391DAFFBA3B6DB94FEB07E529785287D06FBA24671`

## Human Engineer Labels

The user explicitly confirmed these J-C Gold Labels:

| Case | EvidenceId | EngineerLabel |
|---|---|---|
| 1 | `BDFBBA605B00D8A81243A9A0A19E6ABF1492E1981A4DA8973AFEA9CA96AB3BFB` | `UNKNOWN` |
| 2 | `C1E9D2D27CBB9F02243EC6F34F28AD115F9B5E03709200ECA848D3FD3A23D3D0` | `UNKNOWN` |

These labels were intentionally conservative because the retained J-B calibration projection did not preserve enough entity-level drawing evidence to prove a specific road-edge role.

## Runtime

Runtime drawing:

`C:\Users\M.Zeynali\Documents\EngiSentry\artifacts\runtime-validation\phase-e\ES-SUGGEST-A0-phase-e.dwg`

Command:

`ES_JEVCALIBRATE`

Environment:

- `ENGISENTRY_JEV_ENABLED=1`
- `TYPESAFE_API_KEY`: present through environment only; value not recorded

Runtime UI reached:

`BENCHMARK COMPLETE | ADVISORY ONLY | NO PRODUCTION THRESHOLD | READ-ONLY`

Displayed runtime counts:

- Labeled samples: 2
- Valid predictions: 2
- Exact matches: 0
- Incorrect: 2
- Coverage: 1
- Classified accuracy: 0

## Benchmark Result

Model:

`jev-1.13.0`

Case 1:

- EngineerLabel: `UNKNOWN`
- Jev prediction: `ACCESS_ROAD_EDGE`
- Confidence: `0.82`
- AdvisoryStatus: `CLASSIFIED`
- Correct: `false`

Case 2:

- EngineerLabel: `UNKNOWN`
- Jev prediction: `ACCESS_ROAD_EDGE`
- Confidence: `0.43`
- AdvisoryStatus: `CLASSIFIED`
- Correct: `false`

Aggregate:

- LabeledSampleCount: 2
- ValidPredictionCount: 2
- UnavailablePredictionCount: 0
- InvalidPredictionCount: 0
- ExactMatchCount: 0
- IncorrectCount: 2
- UnknownPredictionCount: 0
- Coverage: 1.0
- ExactMatchRate: 0.0
- ClassifiedAccuracy: 0.0
- ProductionThresholdSelected: **false**
- ThresholdStatus: `NO_PRODUCTION_THRESHOLD_SELECTED`

This is a workflow-pilot result only. Two samples are insufficient for model-quality or production-threshold conclusions.

## Immutable Reuse Gate

The scripted runtime invoked `ES_JEVCALIBRATE` a second time after the first report was created.

The accepted implementation checks the current calibration-case and engineer-label hashes before constructing the Jev client. When a matching report exists it returns:

`EXISTING_REPORT_MATCH`

before `clientFactory()` is invoked.

The benchmark report remained unchanged after the second invocation:

- report SHA-256: `A133EE9334561B2CC20B93391DAFFBA3B6DB94FEB07E529785287D06FBA24671`
- report modified UTC: `2026-09-21T19:07:44.6515907Z`

Therefore the second matching invocation used the immutable existing-report path and did not require a new Jev transport call.

## DWG Integrity

After AutoCAD/Civil 3D was fully closed:

Disposable runtime DWG:

- SHA-256: `3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4`
- mtime: `2026-09-20T23:12:29.0437840+03:30`
- unchanged: **YES**

Protected source DWG:

- SHA-256: `3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4`
- mtime: `2025-01-27T16:51:43.0000000+03:30`
- unchanged: **YES**

AutoCAD processes after closeout:

`NONE`

## Governance / Safety

J-C did not:

- modify deterministic semantic state
- modify PlotPlanModel
- modify standards authority
- modify governance
- modify rule execution
- modify AutoExecutionAllowed
- create a PlacementProposal
- select a production threshold
- mutate either DWG
- promote Jev output to engineering authority

## J-C Decision

**PASS / CLOSED**

The first engineer-labeled Jev pilot benchmark completed end-to-end with two human-confirmed labels, one live benchmark path, an immutable matching-report reuse path, preserved read-only DWG integrity, and no production-threshold activation.

## Next Logical Phase

**J-D — Multi-Drawing Calibration Dataset**

J-D should expand beyond this two-sample workflow pilot and should also improve future calibration evidence retention so engineer adjudication can preserve entity-level drawing context such as association identity, handles, positions, candidate geometry, and distances without promoting that evidence into deterministic authority.

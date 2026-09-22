# EngiSentry Phase J-D2 — Multi-Drawing Calibration Dataset Collection

Base HEAD: `0500769`

Status: **PASS_WITH_INSUFFICIENT_DATA**

## Collection
- Unique readable source DWGs after SHA-256 deduplication and J-C exclusion: 3
- Selected source DWGs: 2
- Valid J-D-1 packages: 1
- Total calibration cases: 2
- Engineer label files created: 0
- Benchmark reports created: 0
- Live TypeSafe calls: 0
- ES_JEVCALIBRATE executions: 2
- ES_JEVASSIST executions: 0
- Build runs: 0
- Test runs: 0
- Production source changes: 0

## Selected drawings
1. `FEP-DD-02-PI-PLN-0001-A1.dwg`
   - SHA-256: `BC317D2492FA22728EF4BAF4086FDEFBF3ADFCF384989E89894ED1E1A5DEA24F`
   - Result: `PACKAGE_READY`
   - CalibrationPackageId: `EC48006941257764ACD98558FA76A80A68BDE695FB835FCBE9E4664E460062BB`
   - Schema: `J-D-1`
   - Calibration cases: 2
2. `1003-00-ED-PI-PLP-0500-B1-overall.dwg`
   - SHA-256: `FF7E54A8AB605B4A537466E7666CF5EE9B8E49C8D751916783557DD10EB72A67`
   - Result: `NO_AMBIGUITIES`
   - Calibration cases: 0

## Integrity closeout
- AutoCAD/Civil 3D closed before final integrity verification: PASS
- Selected source DWGs unchanged by SHA-256, length, and LastWriteTimeUtc: PASS
- Opened disposable DWGs unchanged by SHA-256 and length: PASS
- Existing J-C package unchanged: PASS
- Protected paths preserved: PASS

Existing J-C evidence retained exact known hashes, including:
- `benchmark-report.json`: `A133EE9334561B2CC20B93391DAFFBA3B6DB94FEB07E529785287D06FBA24671`
- `calibration-cases.json`: `0992C6413FDA9B5653880AF65BEE21087E4DF03D027DDF2BD43037625AD9F537`
- `engineer-labels.json`: `E48CFAB0B2B97F8650D4E97D532CAFD9BFA93FEBE7FACCD10B8A4888D3BF7D67`

## Supervised recovery note
The initial automated J-D2 attempt stopped safely at `RUNTIME_INTERACTION_REQUIRED` before any `ES_JEVCALIBRATE` execution. A separate supervised recovery AutoCAD session was then used to complete the two fixed selected drawings without replacement or retry. Total AutoCAD/Civil 3D launches across the initial blocked attempt and supervised recovery were **2**, exceeding the original one-process automation budget. This is recorded as an execution-protocol deviation; it did not alter source/disposable/J-C integrity, introduce TypeSafe traffic, or change the fixed drawing selection.

No additional drawings were searched for or substituted to raise the package count. Therefore the dataset remains intentionally **PASS_WITH_INSUFFICIENT_DATA** rather than being expanded by retry/replacement.

## Next
J-D3 — Multi-Drawing Engineer Labeling

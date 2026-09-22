# EngiSentry — Phase J-D2S Supplemental Multi-Drawing Calibration Collection

Date: 2026-09-22
Status: **PASS_WITH_RUNTIME_LIMITATIONS**

## Purpose
Supplement J-D2 using newly discovered local copies of independent issued plot plans.
No TypeSafe/Jev inference calls, engineer labeling, benchmark execution, rule activation, or DWG mutation were permitted.

## Baseline
- Base HEAD: `c4476c7`
- J-D2S production-source changes: **none**
- Build/test runs during J-D2S: **0**
- Live TypeSafe calls: **0**
- `ENGISENTRY_JEV_ENABLED=0` used for runtime collection.
## Selected source drawings
Six distinct copied local references were selected, one per project:
1. AGHAJARI — `98102-AJR-000-PD-PLP-0001-B05_1.dwg`
2. AHVAZ — `98102-AVZ-000-PD-PLP-0001-B05_1.dwg`
3. BIBI-HAKIMEH — `98102-BBH-000-PD-PLP-0001-B04_1.dwg`
4. BINAK — `98102-BNK-000-PD-PLP-0001-B02_1.dwg`
5. PAZANAN — `98102-PZN-000-PD-PLP-0001-B04_1.dwg`
6. SHAHIDAN — `98102-SHN-000-PD-PLP-0001-B03-1OF3.dwg`

Disposable copies were created under:
`artifacts/runtime-validation/phase-jd2s/<slot>/`

Post-run SHA-256 checks show every disposable copy still matches its selected local source copy.
## Terminal runtime results

| Slot | Project | Result | Package | Cases |
|---|---|---|---|---:|
| 01 | AGHAJARI | PACKAGE_READY | `F28E09377D26510A3B603F4CB42282A58F363D7D5A470C9C959C91CB25569B0D` | 2 |
| 02 | AHVAZ | PACKAGE_READY | `F4413CCFEBD52D5CE100D187F2324296A6773535563EEF2850C7A04DE8538181` | 1 |
| 03 | BIBI-HAKIMEH | NO_AMBIGUITIES | none | 0 |
| 04 | BINAK | RUNTIME_INTERACTION_NOT_CLOSED | none | — |
| 05 | PAZANAN | PACKAGE_READY | `06BCE33790489F5827166D706CEE91EB87C7A636721E6E69FF11A301EB4E8D98` | 6 |
| 06 | SHAHIDAN | RUNTIME_NOT_CLOSED | none | — |

Valid supplemental J-D-1 packages: **3**
Total supplemental calibration cases: **9**
## Package validation
All three PACKAGE_READY outputs:
- use schema `J-D-1`
- contain `calibration-cases.json`
- contain `engineer-labels.template.json`
- contain `LABELING_INSTRUCTIONS.md`
- contain no `engineer-labels.json`
- contain no `benchmark-report.json`

The three package DrawingName values are distinct and correspond to AGHAJARI, AHVAZ, and PAZANAN.

## Runtime limitations / deviations
- BIBI-HAKIMEH was observed as `NO_AMBIGUITIES`; its AutoCAD log contains a duplicate command execution during recovery/verification. Both terminal results were `NO_AMBIGUITIES`; no package or DWG mutation resulted.
- BINAK presented the AutoCAD modal `Open Drawing - Previous Version AEC Objects Detected`. The modal was dismissed, but no trustworthy terminal `ES_JEVCALIBRATE` status was obtained.
- SHAHIDAN was launched during the bounded automation recovery but no trustworthy terminal calibration status was obtained.
- No replacement drawings were substituted.
- Once the success criterion of three distinct valid PACKAGE_READY drawings was met, further runtime retries were stopped to avoid a loop.
## Integrity
Selected local source copy ↔ disposable SHA-256 parity:
- AGHAJARI: PASS
- AHVAZ: PASS
- BIBI-HAKIMEH: PASS
- BINAK: PASS
- PAZANAN: PASS
- SHAHIDAN: PASS

Final AutoCAD process count: **0**

Existing J-C package `EC0860837579DAA03C847959254E908C542433D0C486D24AB256A60E371F93AD` remains byte-identical to its accepted hashes:
- benchmark report: `A133EE9334561B2CC20B93391DAFFBA3B6DB94FEB07E529785287D06FBA24671`
- calibration cases: `0992C6413FDA9B5653880AF65BEE21087E4DF03D027DDF2BD43037625AD9F537`
- engineer labels: `E48CFAB0B2B97F8650D4E97D532CAFD9BFA93FEBE7FACCD10B8A4888D3BF7D67`
## Acceptance
J-D2S success criterion:
- at least 3 distinct supplemental drawings produce valid J-D-1 PACKAGE_READY outputs.

Result: **PASS**
- PACKAGE_READY drawings: 3
- distinct projects: 3
- valid J-D-1 packages: 3
- supplemental cases: 9

Overall phase status is recorded as **PASS_WITH_RUNTIME_LIMITATIONS** only to preserve the incomplete terminal results for slots 04 and 06.

## Next
**J-D3 — Multi-Drawing Engineer Labeling**

Use the three new supplemental packages together with the previously accepted calibration material.
Do not select a production threshold and do not auto-resolve semantics in J-D3.

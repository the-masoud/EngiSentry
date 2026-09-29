# EngiSentry — Release Candidate Manifest

Status vocabulary: `PASS` | `PENDING` | `FINDING` | `NOT_APPLICABLE`.

## Build identity

| Field | Value |
| --- | --- |
| Repository HEAD | `03e25d5b2ef3d1887148798ccaec232b69a870cc` |
| Parent / baseline commit | `a6df32aa07f1e691251d8d36bdff60c12b7bbfc7` (P11.2B base); P11.2B commit `03e25d5` |
| Target AutoCAD version | 2021 (R24.0) |
| Framework | .NET Framework 4.8 |
| Platform | x64 |
| Assembly / file version | 1.3.0.0 (informational 1.3.0-rc1); bundle AppVersion 1.3.0 |

## Build and artifacts

| Field | Value |
| --- | --- |
| Release build status | **PASS** (exit 0; 3 pre-existing CS8602 test warnings; 0 errors) |
| Main DLL | `src\EngiSentry.AutoCAD2021\bin\x64\Release\EngiSentry.AutoCAD2021.dll` |
| Main DLL SHA256 | `e733b74afbeb9286512a411e7b47f54a03ac04dac95f73c59489f281bcdd1e3b` |
| Persian satellite | `src\EngiSentry.AutoCAD2021\bin\x64\Release\fa\EngiSentry.AutoCAD2021.resources.dll` |
| Persian satellite SHA256 | `2af79a1be9e91c5d11302b7aac4318e4652953c02b94a26578e6431d3c6e14f5` |
| Installed bundle comparison | **FINDING** — installed bundle does not match the P11.2B Release build (main DLL `5307d4e3…`, Persian satellite `06f9b252…`) → deployment required |
| Installed `PackageContents.xml` SHA256 | `a207ea72243507fa00822aba68e479a3c1e10c2b82c85a787131d034ca05a5b1` |

## Phase status

| Phase | Status |
| --- | --- |
| P11.1 (live AutoCAD 2021 acceptance) | **PASS** (evidence revalidated; automated CRIT 7/8 superseded by the manual acceptance) |
| P11.2 (engineer review UI) | **PASS** |
| P11.2B (bilingual implementation) | **PASS** (English + Persian parity; live switch; preference; RTL/LTR) |
| P11.2B manual visual acceptance | **PENDING** (bundle must be deployed first) |
| Real project-engineer pilot | **PENDING** |
| Installer / release closeout | **PENDING** |

## Unresolved findings

| # | Finding | Severity |
| --- | --- | --- |
| 1 | Installed bundle is stale vs the P11.2B Release build; deployment required before visual acceptance | INFO (expected deployment pending) |
| 2 | Prominent equipment-tag label not explicitly pinned LTR (value renders correctly via bidi; alignment follows layout) | LOW |
| 3 | Two stale-proposal exception details appended to a localized error remain English | LOW |
| 4 | `docs/handoffs/ENGISENTRY_CURRENT_HANDOFF.md` is an untracked, uncommitted handoff note | LOW (informational) |

No CRITICAL, HIGH or MEDIUM unattended software/integrity finding was identified.

## Acceptance gates (human/manual)

- P11.2B bilingual visual acceptance: **PENDING**
- Real qualified project-engineer acceptance: **PENDING**
- Installer/release closeout: **PENDING**

These are expected outstanding human/manual steps and do not by themselves indicate a defect.

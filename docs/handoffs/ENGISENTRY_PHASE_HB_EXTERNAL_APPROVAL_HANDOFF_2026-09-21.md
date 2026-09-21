# EngiSentry Phase H-B — External Approval Handoff

Date: 2026-09-21

Status: **PASS_CANDIDATE** — software, package integrity, and interactive AutoCAD 2021 runtime acceptance passed.

## Package format

H-B creates a deterministic directory package under `%LOCALAPPDATA%\\EngiSentry\\artifacts\\qa\\approval-handoff\\<PackageId>\\`. It is not a ZIP and contains no source PDFs or DWG copy.

The payload inventory is exactly:

- `road.engineering-distance.approval-request.json`
- `road.engineering-distance.authority-policy-request.json`
- `fence.engineering-distance.approval-request.json`
- `fence.engineering-distance.authority-policy-request.json`
- `approval-readiness.json`
- `APPROVER_INSTRUCTIONS.md`

`approval-handoff.manifest.json` is written last and is excluded from its own payload inventory. Each payload has a relative path, byte length, and SHA-256. The PackageId is SHA-256 over the canonical DataContract JSON manifest with `PackageId` blank, with files and all ordered collections sorted using `StringComparer.Ordinal`.

## Contents and safety

The manifest records project and drawing identity, the existing scan timestamp, Road/Fence candidate and rule identities, H-A request IDs, candidate-definition hashes, clause/source/source-hash evidence, readiness/governance values, and explicit false safety flags for approval, signed policy, trusted issuer, authority, and activation.

The package contains only request artifacts. Production approval suffixes, production authority-policy suffixes, trusted issuer stores, private keys, and credential extensions are rejected by the verifier. The instructions state that the package is a request only, that request files must not be edited, that real approval and signing occur externally through the existing P9 workflow, that `REVIEW_ACCEPTED` is not `APPROVED`, and that IPS E-PR-190 remains legacy evidence.

## Integrity verification

`EngineeringDistanceApprovalHandoffVerifier` checks manifest schema and deterministic PackageId, exact inventory, missing/unexpected files, forbidden authority material, every payload SHA-256, both H-A requests and their exact evidence, both unsigned policy requests, safety flags, and readiness consistency. It reports package-integrity statuses without reimplementing P9 governance.

Focused tests cover deterministic output and reordered candidate input, exact two-request/two-policy inventory, readiness and instructions, payload hashes, PackageId, tampered Road/Fence/policy/readiness rejection, missing/unexpected files, production approval/policy/trust/private-key rejection, false authority flags, non-executable Road/Fence readiness, UnitBoundary exclusion, and the existing regression harness.

## Expected readiness

With no production approval, policy, or trust material, Road and Fence remain `PROJECT_RULE_ONLY` with `AutoExecutionAllowed=false`. The package builder consumes `EngineeringDistanceApprovalPreparation` and `EngineeringDistanceApprovalReadinessService`; it does not create a second governance engine.

## Build and tests

- Release x64 Rebuild with `/warnaserror`: PASS, 0 warnings, 0 errors.
- `tests\\bin\\x64\\Release\\EngiSentry.Tests.exe`: PASS, exit code 0.
- Final test line: `All EngiSentry M2.2 through M5.0 tests passed.`

## Runtime result

Interactive AutoCAD 2021 runtime acceptance for `ES_APPROVALPACKAGE`: **PASS**.

The current H-B build was deployed into the installed EngiSentry bundle and the existing disposable Phase E drawing was opened. The standard Missing SHX Files prompt was dismissed with "Ignore the missing SHX files and continue". `ES_APPROVALPACKAGE` then completed and the existing Visual Results Palette was observed with:

- `ES_APPROVALPACKAGE`
- `APPROVAL HANDOFF PACKAGE`
- `PREPARATION ONLY | NO APPROVAL GRANTED | READ-ONLY`
- PackageId `4C143731E9D4FAC0EA73B7B5E05E3FF02CD37C83DE5894A5F4CA2CB89B14E443`
- AutoExecutionAllowed `0`
- manifest verification `VALID_HANDOFF_PACKAGE`
- payload hash verification `PASS`
- production authority material `NO`

The runtime package was created at:

`%LOCALAPPDATA%\\EngiSentry\\artifacts\\qa\\approval-handoff\\4C143731E9D4FAC0EA73B7B5E05E3FF02CD37C83DE5894A5F4CA2CB89B14E443`

Its payload is exactly the six expected files plus `approval-handoff.manifest.json`. The manifest inventory SHA-256 values matched the actual payload files. Road and Fence both remained `PROJECT_RULE_ONLY` with `AutoExecutionAllowed=false`.

DBMOD safety: **PASS** — the Results palette is published only after the command's DBMOD equality guard, and the command reached the verified package result.

## Production authority safety

Before and after the runtime run:

- `C:\\ProgramData\\EngiSentry\\authority-trust` remained absent.
- `C:\\ProgramData\\EngiSentry\\authority-policies` remained absent.
- production `*.engineering-distance.approval.json` count remained 0.
- production signed `*.engineering-distance.authority-policy.json` count remained 0.

No production approval artifact, signed policy, trust store, private key, or rule activation was created.

Disposable runtime DWG:
`artifacts\\runtime-validation\\phase-e\\ES-SUGGEST-A0-phase-e.dwg`
SHA-256 `3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4`.
Its mtime remained `2026-09-20T23:12:29.0437840+03:30`, confirming no save/write.

Protected source DWG:
`_project-data\\plotplan-v1.1\\Plot Plan (2x Revisions)\\FEP-DD-02-PI-PLN-0001-A0.dwg`
SHA-256 `3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4`.
It remained unchanged.

NO PRODUCTION APPROVAL WAS CREATED.

NO PRODUCTION AUTHORITY POLICY WAS CREATED.

NO TRUST STORE WAS MODIFIED.

NO PRIVATE KEY WAS GENERATED.

NO RULE WAS ACTIVATED.

## Future returned-artifact workflow

An authorized engineer reviews the package and exact evidence, creates a real P9 approval, and an authorized administrator creates and externally signs the corresponding authority policy. Returned artifacts must be validated offline against the project, candidate, currentness, trusted issuer store, and policy binding before any separately authorized installation. This H-B package never installs, trusts, signs, approves, or activates anything.

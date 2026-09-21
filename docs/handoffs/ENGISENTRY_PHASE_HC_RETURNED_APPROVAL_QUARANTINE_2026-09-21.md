# EngiSentry Phase H-C — Returned Approval Quarantine

Date: 2026-09-21

Status: **PASS_CANDIDATE** — corrected implementation, Release x64 build/test verification, and interactive AutoCAD 2021 safe-no-op runtime acceptance passed.

## Intake and quarantine roots

Returned files are read from the non-production root:

`%LOCALAPPDATA%\\EngiSentry\\artifacts\\qa\\approval-intake\\incoming\\<H-B PackageId>\\`

Every returned file is copied byte-for-byte, never moved or deleted, into:

`%LOCALAPPDATA%\\EngiSentry\\artifacts\\qa\\approval-intake\\quarantine\\<QuarantineId>\\`

The quarantine manifest `approval-intake.quarantine-manifest.json` is written last with `FileMode.CreateNew`. No production authority directory is written.

## H-B binding

Before returned authority files are inspected, the intake service locates the matching H-B package directory, runs `EngineeringDistanceApprovalHandoffVerifier`, requires `VALID_HANDOFF_PACKAGE`, and binds returned artifacts to the exact H-B project, candidate, rule, candidate-definition hash, clause/source evidence, and request identity. Evaluation is two-pass: approvals are validated/indexed first; policies are then validated and paired to the actual returned approved record by CandidateId, ApprovalRecordId, and ApprovalReference. Unknown or tampered H-B packages are rejected and quarantined.

## Quarantine format and identity

The manifest records schema, deterministic QuarantineId, source package identity and directory, expected project directory, normalized intake timestamp, original filename, relative path, byte length, SHA-256, classified artifact type, resolved candidate/rule/approval/policy identity, issuer/key identity, validation status, diagnostics, H-B verification status, and overall intake state.

QuarantineId is SHA-256 over the source PackageId plus the canonical ordered returned-file inventory. Inventory ordering uses `StringComparer.Ordinal`; the manifest excludes itself from the inventory.

The intake states are limited to `NO_RETURNED_ARTIFACTS`, `QUARANTINED`, `REJECTED`, `PENDING_TRUST`, and `READY_FOR_AUTHORIZED_INSTALLATION`. The last state is only a quarantine/readiness result; it never installs or activates anything.

## Supported and forbidden artifacts

Only exact production artifact suffixes are classified for validation: `*.engineering-distance.approval.json` and `*.engineering-distance.authority-policy.json`. Trusted issuer stores, private-key material, executables, DLL/EXE/scripts, archives, unknown authority files, and unexpected nested directories are not accepted as returned authority material. Returned files are never executed.

Approval records use the existing `EngineeringDistanceApprovalRecordFileStore`, `EngineeringDistanceApprovalRecordValidator`, and `EngineeringDistanceApprovalCurrentnessValidator`, then are checked against the exact H-B Road/Fence binding. Signed policies use the existing policy artifact validator and read the canonical trust store read-only. An absent or non-trusting store produces `PENDING_TRUST`; it is never created or copied.

Road and Fence are evaluated independently and are exposed as separate manifest/result projections. A candidate can become `READY_FOR_AUTHORIZED_INSTALLATION` only when its current APPROVED record and correctly bound trusted signed policy are both present. One candidate can be ready while the other remains `QUARANTINED` or `PENDING_TRUST`; overall readiness does not erase that distinction. No installation follows any state.

## Safe no-op behavior

With no incoming directory or an empty H-B PackageId directory, `ES_APPROVALINTAKE` returns `NO_RETURNED_ARTIFACTS` / `SAFE NO-OP`, reports `PRODUCTION UNCHANGED`, `AutoExecutionAllowed = 0`, and `READ-ONLY`. The command reuses the existing Visual Results Palette and checks DBMOD. It has no approval, install, activate, or trust controls.

## Verification and tests

`EngineeringDistanceApprovalQuarantineVerifier` independently reruns the shared semantic evaluator: QuarantineId, H-B binding, exact inventory, every byte hash, P9 approval currentness, policy signature/content/trust, returned-record pairing, candidate projections, and manifest state are all revalidated. Nested directories are rejected before processing. Focused H-C tests exercise absent/empty intake; Road-only, Fence-only, both-ready, untrusted, wrong-approval, unknown, nested, verifier-tamper, and production-path cases; all 16 forbidden/unknown filename classes (`trusted-issuers.json`, `.pfx`, `.p12`, `.pem`, `.key`, `.exe`, `.dll`, `.ps1`, `.bat`, `.cmd`, `.vbs`, `.js`, `.zip`, `.7z`, `.rar`, and unknown); byte/hash and deterministic quarantine behavior; and ProgramData non-use. Existing regression tests remain green.

- Release x64 MSBuild rebuild with `/warnaserror`: PASS, 0 warnings, 0 errors.
- `tests\\bin\\x64\\Release\\EngiSentry.Tests.exe`: PASS, exit code 0.
- Final line: `All EngiSentry M2.2 through M5.0 tests passed.`

## Runtime and production safety

Interactive AutoCAD 2021 runtime acceptance for `ES_APPROVALINTAKE`: **PASS** on the existing disposable Phase E drawing.

The current H-C Release build was deployed to the installed EngiSentry bundle, AutoCAD 2021 was launched on the disposable runtime DWG, the standard Missing SHX Files prompt was dismissed using "Ignore the missing SHX files and continue", and the existing Visual Results Palette was observed with:

- `ES_APPROVALINTAKE`
- `RETURNED APPROVAL INTAKE`
- `SAFE NO-OP | NO RETURNED ARTIFACTS | PRODUCTION UNCHANGED | READ-ONLY`
- `AUTOEXECUTIONALLOWED = 0`

The command reached the palette only after its existing DBMOD equality guard, so DBMOD remained unchanged.

Before and after runtime:
- `C:\\ProgramData\\EngiSentry\\authority-trust` remained absent.
- `C:\\ProgramData\\EngiSentry\\authority-policies` remained absent.
- production `*.engineering-distance.approval.json` count remained 0.
- the real intake root remained absent because no returned artifacts exist.

Disposable runtime DWG: `artifacts\\runtime-validation\\phase-e\\ES-SUGGEST-A0-phase-e.dwg` — SHA-256 `3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4`; mtime remained `2026-09-20T23:12:29.0437840+03:30`, proving no save/write.

Protected source DWG: `_project-data\\plotplan-v1.1\\Plot Plan (2x Revisions)\\FEP-DD-02-PI-PLN-0001-A0.dwg` — SHA-256 `3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4`; unchanged.

NO APPROVAL WAS INSTALLED.

NO AUTHORITY POLICY WAS INSTALLED.

NO TRUST STORE WAS CREATED OR MODIFIED.

NO PRIVATE KEY WAS GENERATED.

NO RULE WAS ACTIVATED.

NO DWG WAS MODIFIED.

# EngiSentry Phase H-A — Approval Onboarding Tooling

Date: 2026-09-21

## Status

**PASS_CANDIDATE** — implementation, offline validation, build/test verification, and interactive AutoCAD 2021 runtime acceptance all passed. The result is a preparation-only onboarding path for the existing P9 approval/governance chain. It does not approve a candidate, create authority, sign an artifact, install trust, or modify a DWG.

## Prepared candidates

The preparation contract is deterministic and bounded to the two existing built-in candidates:

- Road: `candidate.processing-equipment.access-road-edge.3m`, rule `std.plotplan.processing-equipment.access-road-edge.minimum-clearance`, proposed minimum 3 m, measurement kind `EquipmentToRoad2D`, basis `AccessRoadEdge`, applicability `ProcessingEquipment`.
- Fence: `candidate.equipment.security-fence.30m`, rule `std.plotplan.equipment.security-fence.minimum-clearance`, proposed minimum 30 m, measurement kind `EquipmentToBoundary2D`, basis `SecurityFence`, applicability `Equipment`.

Each request carries its schema version, deterministic request ID, normalized project directory, candidate/pack/rule binding, candidate-definition SHA-256, clause IDs, clause/source/source-hash evidence bindings, scan timestamp, and any available drawing/project identity. The request flags are explicitly `IsApproval=false`, `GrantsAuthority=false`, and `AutoExecutionAllowed=false`.

The companion authority-policy request is a separate unsigned request artifact. It is marked unsigned, untrusted, inactive, and non-authoritative. It contains no signature and is never loaded by the production approval or policy catalogs.

## Future authorized real-approval sequence

1. A qualified engineer reviews the Road or Fence preparation request and its clause/source evidence.
2. The engineer confirms the project directory, candidate definition hash, measurement basis, applicability, and current governed criterion.
3. The engineer records a real approval through the existing P9 approval-record contract, including authenticated identity and evidence bindings.
4. An authorized authority administrator creates the corresponding policy artifact from the real approval record.
5. The policy artifact is externally signed with the approved RSA-PKCS1-SHA256 key.
6. The signer/key is provisioned into the controlled trust store by an authorized administrator; this tooling does not perform that operation.
7. The signed approval and policy artifacts are placed in the existing production loader locations: project approval artifacts under `artifacts\\qa\\plotplan` for the current project workflow, policy artifacts under `C:\\ProgramData\\EngiSentry\\authority-policies`, and trusted issuers under `C:\\ProgramData\\EngiSentry\\authority-trust\\trusted-issuers.json`.
8. The offline validator is run against the approval path, policy path, and trust-store path; only the existing P9 validators may establish readiness for authorized installation.

## Command and UI

`ES_APPROVALPREP` performs criteria resolution, read-only drawing scanning, existing reference/candidate resolution, readiness inspection, and writes only the two request suffixes:

- `.engineering-distance.approval-request.json`
- `.engineering-distance.authority-policy-request.json`

Requests are written with `DataContractJsonSerializer` and `FileMode.CreateNew`. The command checks that `DBMOD` is unchanged and reuses the existing `VisualResultsPalette` boundary. The palette is labeled `PREPARATION ONLY / NO APPROVAL GRANTED / READ-ONLY` and reports readiness without enabling execution.

Core Console remains read-only and does not open UI.

## Offline validation

`EngineeringDistanceOfflineValidator` accepts explicit approval, policy, trust-store, project-directory, and candidate inputs. It calls the existing approval record file store/currentness validator, trust-store loader, and policy-artifact validator. It fails closed with statuses for invalid format, untrusted signer, stale approval, wrong project, wrong candidate, wrong binding, invalid signature, valid-but-not-approved format, or `READY_FOR_AUTHORIZED_INSTALLATION`. It does not copy, install, sign, trust, activate, or modify any artifact.

## Verification

- Rebuild: `MSBuild EngiSentry.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64 /warnaserror` — PASS, 0 warnings, 0 errors.
- Tests: `tests\\bin\\x64\\Release\\EngiSentry.Tests.exe` — PASS, exit code 0; final line `All EngiSentry M2.2 through M5.0 tests passed.`
- Current Phase H-A build was deployed into the installed EngiSentry AutoCAD 2021 bundle before runtime verification.
- `ES_APPROVALPREP` interactive AutoCAD 2021 runtime: PASS on the existing disposable runtime drawing.
- Live Visual Results Palette exposed `ES_APPROVALPREP`, `Approval Onboarding Preparation`, `PREPARATION ONLY | NO APPROVAL GRANTED | READ-ONLY`, candidate/request metrics, and the explicit non-authoritative request warning.
- Exactly four request artifacts were created under `%LOCALAPPDATA%\\EngiSentry\\artifacts\\qa\\approval-prep`: one approval request and one unsigned authority-policy request for Road, and the same pair for Fence.
- Road and Fence approval requests contain the existing candidate values, candidate-definition SHA256, exact clause/source SHA256 evidence, `IsApproval=false`, `GrantsAuthority=false`, and `AutoExecutionAllowed=false`.
- Authority-policy request artifacts are `IsSigned=false`, `IsTrusted=false`, `IsActive=false`, and `GrantsAuthority=false`; they use only the request suffix and are not production authority-policy artifacts.
- Production trust directory remained absent: `C:\\ProgramData\\EngiSentry\\authority-trust`.
- Production authority-policy directory remained absent: `C:\\ProgramData\\EngiSentry\\authority-policies`.
- Production approval artifact count under the PlotPlan artifact root remained 0.
- Production signed authority-policy artifact count remained 0.
- DBMOD safety: PASS — the command reached the Results palette only after its existing DBMOD equality guard.
- DWG integrity after AutoCAD closed: disposable runtime drawing and protected source drawing both remained SHA256 `3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4`; the disposable drawing mtime did not advance, confirming no save/write.
- `git diff --check`: PASS apart from normal LF/CRLF conversion notices.

## Explicit safety conclusion

NO PRODUCTION APPROVAL WAS CREATED. NO SIGNED PRODUCTION POLICY WAS CREATED. NO TRUST STORE WAS CHANGED. NO DWG WAS MODIFIED. PHASE H-A RUNTIME ACCEPTANCE PASSED.

# EngiSentry — Phase G: Governed Standards Activation Readiness

Date: 2026-09-21
Status: **PASS_CANDIDATE** (software PASS; production AutoCAD safety runtime PASS)

## P9 activation chain

The existing production path is:

`EngineeringDistanceCandidateCatalog` → `ProjectDistanceRuleBinder` → `EngineeringDistanceApprovalArtifactCatalog.Load`
→ `EngineeringDistanceApprovalRecordFileStore.Read` → `EngineeringDistanceApprovalRecordValidator.Validate`
→ `EngineeringDistanceApprovalHistoryResolver.Resolve` → `EngineeringDistanceApprovalCurrentnessValidator.Evaluate`
→ `EngineeringDistanceApprovalAuthorityTrustStore.LoadCanonical`
→ `EngineeringDistanceApprovalAuthorityPolicyArtifactCatalog.LoadCanonical`
→ `EngineeringDistanceApprovalAuthorityResolver.Resolve`
→ `ProjectDistanceGovernedRuleResolver.Resolve`
→ `ProjectDistanceGovernanceAggregateResolver.Resolve`
→ `ProjectDistanceGovernanceRuntimeLoader.LoadCanonical`
→ `StandardsPackProjectionBuilder` → `BoundedConstraintBuilder` → Phase C CHECK → Phase D/ E runtime.

An activation requires a normalized project directory; an approval record with schema `1.0`, content-bound
`ApprovalRecordId`, candidate binding hash, matching `CandidateId`, `RuleId`, pack, measurement kind, value,
basis, applicability and clause IDs; exact source/clause evidence bindings with SHA256 values; an active approval
history head with `APPROVED` status; current candidate/evidence bindings; a verified, signed authority-policy
artifact; and a matching trusted issuer/key, project, candidate, approval record, approval reference, deterministic
enforcement mode, and valid authority policy. The governing resolution must be
`GOVERNED_PROJECT_DETERMINISTIC` / `GOVERNED_COMPATIBLE` before execution is allowed.

## Production readiness

- Road candidate: `candidate.processing-equipment.access-road-edge.3m`; rule
  `std.plotplan.processing-equipment.access-road-edge.minimum-clearance`; configured/candidate criterion 3 m;
  current governed criterion: unavailable; approval artifact: NO; trusted issuer: NO; policy artifact: NO;
  currentness: unavailable; final state: `PROJECT_RULE_ONLY`; `AutoExecutionAllowed`: false.
- Fence candidate: `candidate.equipment.security-fence.30m`; rule
  `std.plotplan.equipment.security-fence.minimum-clearance`; configured/candidate criterion 30 m;
  current governed criterion: unavailable; approval artifact: NO; trusted issuer: NO; policy artifact: NO;
  currentness: unavailable; final state: `PROJECT_RULE_ONLY`; `AutoExecutionAllowed`: false.
- No canonical production artifact root was present at `C:\ProgramData\EngiSentry` during read-only inspection.
- PCD-PD-135 Rev.1 remains `GOVERNING_PROJECT` / `CONDITIONAL_REVIEW`.
- IPS E-PR-190 remains `LEGACY_CONFLICT` / `NEVER_AUTO_ENFORCE`; it cannot become the authority source.
- UnitBoundary remains outside the candidate catalog and is unaffected; its project rule remains review-required.

## Controlled tests

Added `PhaseGActivationReadinessTests` using controlled in-memory/temp-path fixtures only. It verifies bounded
Road/Fence definitions and evidence, no approval fail-closed behavior, no UnitBoundary candidate, and the PCD/IPS
authority split. The existing focused P9 and runtime tests also passed, covering trusted/current approval activation,
invalid/untrusted/stale/project-scope/binding rejection, deterministic ordering, approved-value preservation,
Road/Fence governed resolution, and end-to-end proposal behavior. No production approval artifact was created.

## Verification

- Build: PASS — Release x64 rebuild with `/warnaserror`; 0 warnings, 0 errors.
- Tests: PASS — `EngiSentry.Tests.exe`, exit code 0; final line: `All EngiSentry M2.2 through M5.0 tests passed.`
- ES_SUGGEST production safety runtime: PASS in interactive AutoCAD 2021 on the existing disposable Phase E drawing.
- Live palette result: governed rules 3; bounded constraints 3; violated constraints 0; review-required constraints 3;
  insufficient-evidence constraints 0; executable violations 0; placement proposals 0; status READ-ONLY.
- A new placement-proposal sidecar was emitted:
  `ES-SUGGEST-A0-phase-e.dwg.20260921T072035402Z.placement-proposal.json`; exact content `[]`; JSON parse PASS.
- DBMOD safety: PASS — the live Placement Proposals palette is published only after both existing ES_SUGGEST DBMOD
  guards pass, and the command reached COMPLETE / READ-ONLY.
- DWG integrity: PASS after AutoCAD closed. Disposable and protected source SHA256 both remained
  `3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4`; disposable mtime was unchanged.
- Production state remains fail-closed: 0 executable violations and 0 proposals because no real production approval
  artifact exists.
- Production approval fabricated: NO.
- P6/P7/P8/P9 semantics changed: NO.
- Phase C/D/E/F behavior changed: NO.
- `git diff --check`: PASS.

Phase G runtime acceptance is complete.

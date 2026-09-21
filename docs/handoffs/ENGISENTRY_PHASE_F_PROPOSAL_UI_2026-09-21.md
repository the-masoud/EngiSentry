# EngiSentry — Phase F: Read-only Placement Proposal UI

Date: 2026-09-21
Status: **PASS_CANDIDATE** (software PASS; interactive AutoCAD 2021 acceptance PASS)

## Scope

Added the smallest additive projection of the accepted `PlacementProposal` results into the existing
`VisualResultsPalette`. No new window, palette, command, proposal model, evaluator, governance path, or
serialized contract was added. The palette remains display-only and publishes only after `ES_SUGGEST` processing,
sidecar creation, and the existing DBMOD guards.

## Files changed

- `src/EngiSentry.AutoCAD2021/VisualResultsPalette.cs`
- `src/EngiSentry.AutoCAD2021/Commands.cs`
- `src/EngiSentry.AutoCAD2021/Properties/AssemblyInfo.cs` (test visibility only)
- `tests/EngiSentry.Tests.csproj`
- `tests/UnitTests.cs`
- `tests/PhaseFPresentationTests.cs`
- `docs/handoffs/ENGISENTRY_PHASE_F_PROPOSAL_UI_2026-09-21.md`

## Projection

Summary metrics map governed rules, bounded constraints, violated, review-required, insufficient-evidence,
executable violations, proposal count, sidecar path, and `READ-ONLY` status. Proposal details map rule and pack
metadata, document/revision/clause/page/source hash, observed/required distance, measurement basis and units,
positions, movement, Road/Plant Fence/Unit Boundary re-evaluation, all visible constraint states, assumptions,
unchecked constraints, rejected candidates/reasons, engineering-review requirement, and known limitations.
Unavailable top-level placement identity is shown as unresolved because the accepted `PlacementProposal` contract
does not carry `PlacementId`, `EquipmentEntityId`, or `NormalizedTag` fields.

Zero proposals render as a valid completed governed result with assumptions, unchecked constraints, known
limitations, and read-only status visible. Core Console remains a no-UI path through `AutoCADHostBoundary.IsCoreConsole()`.

## Verification

- Build: PASS — Release x64 rebuild with `/warnaserror`; 0 warnings, 0 errors.
- Tests: PASS — `EngiSentry.Tests.exe`, exit code 0; final line: `All EngiSentry M2.2 through M5.0 tests passed.`
- Deployed bundle: PASS — the Phase F build was copied into
  `%APPDATA%/Autodesk/ApplicationPlugins/EngiSentry.bundle/Contents/Win64`; the deployed
  `EngiSentry.AutoCAD2021.dll` SHA256 matched the current Release build:
  `6FF1A5D9783739075D1BB1CE4222423E8F5BCC03E67AE1E2D8379032C0BBE1AB`.
- Interactive AutoCAD 2021: PASS — the disposable
  `artifacts/runtime-validation/phase-e/ES-SUGGEST-A0-phase-e.dwg` was opened. The standard Missing SHX Files
  prompt was dismissed with "Ignore the missing SHX files and continue"; `ES_SUGGEST` then completed.
- Palette projection: PASS — live UI automation observed `ES_SUGGEST`, `Placement Proposals`,
  `COMPLETE | Read-only placement proposal projection completed`, the zero-proposal note, and visible metric
  labels for governed rules, bounded constraints, review-required constraints, executable violations,
  placement proposals, and READ-ONLY.
- Current governed runtime result: valid zero-proposal outcome. The accepted Phase E runtime result remains
  3 governed rules / 3 bounded constraints / 3 review-required constraints / 0 executable violations /
  0 placement proposals.
- DBMOD: PASS — `ShowPlacementProposals` is reached only after both existing DBMOD guards pass; the live Phase F
  palette was observed after sidecar creation, so the post-artifact DBMOD check completed successfully.
- Sidecar: PASS — a new runtime artifact was created:
  `ES-SUGGEST-A0-phase-e.dwg.20260921T064617384Z.placement-proposal.json`.
  It parses as valid JSON, exact content `[]`, SHA256
  `4F53CDA18C2BAA0C0354BB5F9A3ECBE5ED12AB4D8E11BA873C2F11161202B945`.
- DWG integrity: PASS — after AutoCAD closed, the disposable DWG SHA256 and the protected source DWG SHA256 were
  both `3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4`; the disposable mtime remained
  `2026-09-20T23:12:29.0437840+03:30`, confirming no save/write.
- Serialized contracts changed: NO.
- P6/P7/P8/P9 changed: NO.
- Phase C/D/E behavior changed: NO.
- `git diff --check`: PASS (only normal Git LF/CRLF notices were emitted).

Phase F runtime acceptance is complete.

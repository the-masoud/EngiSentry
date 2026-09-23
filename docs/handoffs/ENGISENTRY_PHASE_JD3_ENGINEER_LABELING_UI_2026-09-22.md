# EngiSentry Phase J-D3 — Engineer Labeling UI Handoff

- Base HEAD: `f2689b0`
- Command: `ES_JEVLABEL`
- UI host: existing `VisualResultsPalette` / `InteractiveVisualResultsPalette` modeless WinForms surface
- Package discovery: deterministic local discovery under `%LOCALAPPDATA%\EngiSentry\artifacts\qa\jev-calibration\`
- Package states: `NOT_STARTED`, `IN_PROGRESS`, `COMPLETE`, `LOCKED_BENCHMARKED`, `INVALID_LABEL_FILE`
- Engineer inputs: label and comment only
- Allowed labels: `ACCESS_ROAD_EDGE`, `SECURITY_FENCE`, `UNIT_BOUNDARY`, `UNRELATED`, `UNKNOWN` (from `JevSemanticLabels.All`; verified at runtime as the five Engineer Label combo items)
- Save behavior: explicit, validated, same-directory temporary file followed by atomic replacement
- Historical benchmark packages: read-only when `benchmark-report.json` exists
- Production files added: `src/EngiSentry.Intelligence/JevEngineerLabelingService.cs`, `src/EngiSentry.AutoCAD2021/JevEngineerLabelingControl.cs`
- Production files modified: `src/EngiSentry.Intelligence/EngiSentry.Intelligence.csproj`, `src/EngiSentry.AutoCAD2021/EngiSentry.AutoCAD2021.csproj`, `src/EngiSentry.AutoCAD2021/VisualResultsPalette.cs`, `src/EngiSentry.AutoCAD2021/Commands.cs`
- Test file added: `tests/PhaseJevEngineerLabelingTests.cs`
- Test integration modified: `tests/EngiSentry.Tests.csproj`, `tests/UnitTests.cs`
- Release x64 `/warnaserror` rebuild: PASS, 0 warnings, 0 errors
- Focused J-D3 assertions: PASS, 25
- Full Release test executable: PASS
- Network / TypeSafe calls: 0
- Benchmark executions: 0
- DWG files modified: NO
- Runtime AutoCAD GUI acceptance: PASS — deterministic, modeless `ES_JEVLABEL` UI driven through its real control surfaces (see "Deterministic runtime GUI acceptance" below)
- Commit: NONE
- Push: NO
- Ready for bounded GUI acceptance: YES

## GUI runtime acceptance correction

- Root cause corrected: `JevEngineerLabelingControl` assigned `SplitterDistance = 455` while the new splitter still had its small default width, causing `ArgumentOutOfRangeException`; the labeling display path swallowed that exception into debug output.
- Correction files: `src/EngiSentry.AutoCAD2021/JevEngineerLabelingControl.cs`, `src/EngiSentry.AutoCAD2021/VisualResultsPalette.cs`
- Corrected title: `EngiSentry - Jev Engineer Labeling`
- Display failures now propagate to the existing `ES_JEVLABEL` command-line error handler.
- Existing visible window is restored, brought to front, and activated.
- Correction Release x64 `/warnaserror` rebuild: PASS, 0 warnings, 0 errors, 1 run
- Correction Release tests: PASS, 1 run
- Final test line: `All EngiSentry M2.2 through M5.0 tests passed.`
- DLL deployment: PASS; SHA-256 equality verified for all 11 deployed DLLs
- GUI acceptance launch: 1
- Disposable drawing opened: `artifacts/runtime-validation/phase-jd2s/01/98102-AJR-000-PD-PLP-0001-B05_1.dwg`
- AutoCAD process environment: `ENGISENTRY_JEV_ENABLED=0`
- AutoCAD responsive after launch: YES
- Runtime result (superseded — first attempt): BLOCKED — `RUNTIME_INTERACTION_REQUIRED`
- Runtime blocker (first attempt): Windows automation failed with `GetCursorPos failed: Access is denied. (0x80070005)`; the single permitted recovery capture returned a black image and no accessibility tree, so no AutoCAD command was entered and visual acceptance was not guessed.
- Resolution: acceptance was re-run deterministically without any cursor/input-injection dependency (see "Deterministic runtime GUI acceptance" below); all checks PASS.
- Ready to commit J-D3: YES — build, tests, and deterministic runtime GUI acceptance all PASS; no commit performed.

## Deterministic runtime GUI acceptance

- Build verification: Release x64 rebuild with `-warnaserror`, PASS, 0 warnings, 0 errors (MSBuild 17.14), 1 run.
- Test verification: `tests/bin/x64/Release/EngiSentry.Tests.exe`, PASS, exit code 0; focused J-D3 assertions: 25 PASS; final line: `All EngiSentry M2.2 through M5.0 tests passed.`
- Deployment: current Release build copied into `%APPDATA%\Autodesk\ApplicationPlugins\EngiSentry.bundle\Contents\Win64`; SHA-256 equality verified for all 11 deployed DLLs against the Release output.
- AutoCAD launch count: 2 (1 pre-flight session used only to capture pre-state and confirm `NETLOAD`; no `ES_JEVLABEL` was entered in it and it was discarded; 1 acceptance session).
- `ES_JEVLABEL` executions: exactly 1 (invoked from the startup script after pre-state capture; the modeless command returns immediately).
- Disposable drawing: `artifacts\runtime-validation\phase-jd2s\01\98102-AJR-000-PD-PLP-0001-B05_1.dwg` (disposable runtime fixture; not a protected source drawing).
- AutoCAD process environment: `ENGISENTRY_JEV_ENABLED=0`.
- Pre-command state: `DBMOD=0`, `CMDNAMES=` (empty), `DWGNAME=98102-AJR-000-PD-PLP-0001-B05_1.dwg`, `DWGPREFIX=...\phase-jd2s\01\`.
- A. WINDOW: PASS — modeless window created and enabled (no swallowed exception); exact title `EngiSentry - Jev Engineer Labeling`.
- B. PACKAGE DISCOVERY: PASS — 5 packages discovered from `%LOCALAPPDATA%\EngiSentry\artifacts\qa\jev-calibration\`; deterministic ordering (by `DrawingName`, then `CalibrationPackageId`) observed as AJR/F28E09, AVZ/F4413, PZN/06BCE, ES-SUGGEST-A0-phase-e/EC0860, FEP-DD-02/EC4800; per-package status visible in the selector (4x `NOT_STARTED`, 1x `LOCKED_BENCHMARKED`); the first editable package was auto-selected.
- C. EVIDENCE DISPLAY: PASS — for the selected case the UI shows `EvidenceId: 7561DE6B...`, `InputProjectionSha256: 9D4E1412...`, `CurrentDeterministicState: Ambiguous`, deterministic-context fields (`ExistingCandidateKinds`, `MeasurementBasisHints`, `SpatialContext: CandidateCount:2 | Ambiguity:MultipleGeometryCandidatesWithinAmbiguityMargin`), full SOURCE EVIDENCE (`SourceRecordType`, `SourceRecordId`, `SubjectKind: Boundary`, `LabelRawText: NEW FENCE`, label handle/layer/position, `AssociationScale`, `Reason`), and GEOMETRY (`GeometryKind: Polyline`, `LayerName: EXISTING-MAIN FENCE`). Evidence refresh was verified on package switch (locked case `BDFBBA60...`) and on `Next` navigation (locked case `C1E9D2D2...`).
- D. LABEL EDITING: PASS — on the editable package the Engineer Label combo was set to the valid value `SECURITY_FENCE` and a short comment entered; verified that `engineer-labels.json` was NOT written before the explicit Save (file remained absent; no `.tmp` created).
- E. EXPLICIT SAVE: PASS — one click of the UI Save button created `engineer-labels.json` via the designed temporary-file-then-atomic-move path (no `.tmp` remained after success). Content verified: `SchemaVersion: J-D-1`, `CalibrationPackageId` preserved, both `EvidenceId`s and `InputProjectionSha256`s preserved exactly, `EngineerLabel: SECURITY_FENCE` + comment stored on the labeled case. `calibration-cases.json`, `engineer-labels.template.json`, and `benchmark-report.json` all byte-identical (SHA-256 unchanged). UI updated to `IN_PROGRESS`, `Labeled 1 / 2`, message `Engineer labels saved.`
- F. LOCKED PACKAGE: PASS — selecting the benchmarked package (`EC0860...` / `ES-SUGGEST-A0-phase-e.dwg`) shows state `LOCKED_BENCHMARKED`, `Labeled 2 / 2`; Engineer Label combo disabled, Comment disabled, Save disabled; `benchmark-report.json`, the locked `engineer-labels.json`, and the locked `calibration-cases.json` all unchanged. No benchmark was created for this test.
- DBMOD: before `0`, after `0`, unchanged; additionally AutoCAD was shut down after acceptance and exited cleanly with NO save-changes prompt, confirming the drawing was never modified.
- DWG integrity: SHA-256 before `DBD6D6BFDE04770EF250488DD926DC162EA5E246B3A7971B2745B65F85AC8CAA` == after; size `2890464` unchanged; last-write UTC `2021-02-15T23:59:43.798Z` unchanged; drawing was not saved.
- Network / TypeSafe calls during labeling: 0 (`ENGISENTRY_JEV_ENABLED=0`; the labeling service constructs no Jev client and exposes no benchmark execution; no network sidecar artifact was created in the fixture directory).
- Benchmark executions during labeling: 0.
- Production authority safety: no approval created, no authority policy created, no trust store modified, no private key created, no rule activated.
- Files written by the acceptance session: only the intended QA artifact `…\jev-calibration\F28E09…\engineer-labels.json` plus disposable scratch under the untracked `artifacts\runtime-validation\jd3-gui-acceptance-r4\` (scripts/logs/capture). No tracked source file was modified by the acceptance run.
- Automation method (deterministic, no cursor): Windows UI Automation tree reads for window/control identity and status text; real control-surface interaction via Win32 messages against the actual modeless control HWNDs (`CB_SETCURSEL` + `CBN_SELCHANGE` notification for the package/label combos, `WM_SETTEXT` for the comment, `BM_CLICK` for Save/Next). Cursor-based input injection remained unavailable in this environment (`GetCursorPos` access denied; `SendKeys` not delivered), so no mouse/capture path was used. Authoritative control text was read with `WM_GETTEXT`.
- Known environment limitation (not a product defect): in this AutoCAD-hosted modeless surface all WinForms controls are surfaced through the legacy UI Automation proxy (exposed as `ControlType.Pane` with no patterns), so `DataGridView` cell contents are not externally readable; candidate/geometry evidence was therefore verified through the evidence panel fields (including `ExistingCandidateKinds` and `SpatialContext: CandidateCount`) and by grid presence plus deterministic refresh behavior. Also, the cross-process `GetWindowText` API returned stale text for the evidence box; authoritative reads used `WM_GETTEXT`, which confirmed correct content and refresh.
- Commit: NONE. Push: NO.

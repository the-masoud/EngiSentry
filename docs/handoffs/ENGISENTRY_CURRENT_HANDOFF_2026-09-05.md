# EngiSentry — Current Development Handoff

Date: 2026-09-05
Repo: `C:\Users\M.Zeynali\Documents\EngiSentry`
Product: **EngiSentry — Engineering QA & Drawing Intelligence**
Target runtime: **AutoCAD 2021 x64**

## 1. Working model

This is a solo project between Masoud + ChatGPT + Codex.
Preferred responsibility split:
- **ChatGPT:** architecture, engineering reasoning, root-cause analysis, Codex prompts, independent review, runtime validation.
- **Codex:** C# implementation, focused tests, build, code changes.
- Do not let ChatGPT casually implement large changes directly when Codex can do them.
- Never accept a Codex report blindly; independently review diff/runtime evidence.

Core product principle:
**AI may reason/recommend; deterministic evidence/rules must prove engineering correctness. Never guess engineering facts.**

The product must work generically across drawings/projects/clients. Do not hardcode filenames, tags, layers, handles, project names, or one specific drawing's values.
## 2. Architecture / environment

Solution/projects:
- `EngiSentry.Core`
- `EngiSentry.Contracts`
- `EngiSentry.AutoCAD2021`
- `EngiSentry.Semantics`
- `EngiSentry.Rules`
- `EngiSentry.PlotPlan`
- `EngiSentry.Standards`
- `EngiSentry.CrossDocument`
- `EngiSentry.Revision`

Runtime architecture:
- AutoCAD 2021 plugin: C# / .NET Framework 4.8 / x64.
- Shared libraries: netstandard2.0.
- Autodesk API isolated to `EngiSentry.AutoCAD2021`.
- Scanner/adapter -> normalized `DrawingSnapshot` -> semantic model -> index -> rules/plot/revision.
- Future modern services may use .NET 10, but never inside AutoCAD 2021 process.

Canonical MSBuild:
`C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe`

Installed bundle:
`C:\Users\M.Zeynali\AppData\Roaming\Autodesk\ApplicationPlugins\EngiSentry.bundle\Contents\Win64`
## 3. Released baseline vs current working tree

Released baseline is **v1.0.1**:
- release/tag commit: `bd32c128522c6fa58a53962f0039540c0bd5226f`
- GitHub: `https://github.com/the-masoud/EngiSentry`
- v1.0.1 ZIP SHA256: `7BB1BC215B907FC4E49CD27CE00DA1EACF43511E34A56446B47A297D7C25615B`

Current source is **v1.1.0 release-preparation work, verified after v1.0.1**.
Do NOT claim the tree is clean.
Phase E final full regression is PASS / CLOSED.
Approved next version is **v1.1.0**; current work is release preparation, commit, and package review only.

Current work includes changes in scanner, semantics, QA applicability, PlotPlan, Revision, Results UI, zoom, CrossQA safety, tests, plus Plot Plan v1.1 standards docs.
`artifacts/` is also untracked/generated and must not be blindly committed.

Important: preserve all existing working-tree changes. Never reset/revert them unless Masoud explicitly approves.

## 4. Results UI / usability already implemented

Results are shown in a normal **modeless WinForms Form**, movable/minimizable/resizable.
JSON remains the audit backend.
Commands display useful metrics/tables instead of forcing users to read JSON.
`Zoom To Evidence` currently delegates navigation to AutoCAD native `ZOOM Object`, because custom WCS->view math failed on some drawings.
Nested evidence handles are represented as composite paths and zoom logic has been extended toward the owning block instance.
## 5. Real A5 generalization fixture

Primary real test drawing:
`C:\Users\M.Zeynali\Desktop\Masoud\New folder (2)\Inhand UG Composite Layout - Rev A5-14041002.dwg`

Original SHA256:
`5F31F64DE4A6E183324E8B55088912D8FE238C8C9B25605170947FB9994E5256`

Safety rule: never Save/QSAVE/overwrite the source during automated testing. Use disposable copies under repo artifacts.
Repeated tests confirmed the DWG remained byte-identical.

This drawing exposed the major generalization problem:
- top-level ModelSpace had very few entities,
- most real content lived inside nested block definitions/references,
- old semantic extraction returned essentially zero useful engineering evidence.

Scanner was hardened to recursively expand instantiated nested blocks with transforms while remaining read-only.
It now captures instantiated visible text/attributes and supported geometry in world/model coordinates.
Traversal has depth/entity safety limits and skips xrefs.

Coordinate grammar was generalized for formats such as:
- `E=427,00`
- `N=225,00`
- `E=560.00`
- `N=298,35`
without misclassifying equipment like `E-0201` as coordinates.
## 6. Semantic / QA generalization fixes already accepted

Semantic extraction now:
- includes instantiated nested-block text/attributes,
- filters off/frozen-layer evidence,
- distinguishes structure context before equipment parsing,
- supports broader equipment/structure/coordinate forms,
- does not treat arbitrary block-definition names as visible engineering identities.

Critical BLOCKNAME fix is independently accepted:
- `BLOCKNAME` / block object metadata cannot become EquipmentTag or StructureTag evidence.
- On A5 this removed 9 false equipment occurrences.
- Three old `TOMH-2550X180` Complete placements disappeared because they were block-name metadata, not real visible equipment evidence.

Generic QA applicability was also hardened:
- rules that require an Equipment List do not create hundreds of drawing-only false warnings when no list exists,
- those rules report `NOT_APPLICABLE` instead.

Cross-document safety:
`ES_CROSSQA` must NOT silently use the old global test CSV.
If no register is linked to the drawing/project, it fails safely and asks for a project/drawing-specific register rather than generating false findings.

Revision safety:
- zero usable equipment evidence must not be presented as “0 engineering changes”; use insufficient-evidence semantics.
- baseline is not folder-hardcoded.
- different INSUNITS alone no longer blocks revision evidence comparison.
- revision lineage confidence exists to warn when two drawings probably are not revisions of the same engineering drawing.
## 7. Latest Codex patch: safe E/N pair coherence

Codex changed only:
- `src/EngiSentry.PlotPlan/PlotPlanBuilder.cs`
- `tests/UnitTests.cs`

Accepted algorithm:
- Resolved + Resolved: skip pair coherence; preserve both selections exactly.
- Any Missing axis: skip pair coherence; do not manufacture evidence.
- Resolved + Ambiguous: resolved axis is fixed; only retained candidates of ambiguous axis may be disambiguated.
- Ambiguous + Ambiguous: pair only each axis's retained ambiguity candidates.
- Coherence uses shared instantiated root and/or semantic layer/context.
- Resolve only a uniquely best coherent candidate/pair; ties remain Ambiguous.
- `MaxAssociationDistanceFactor = 30.0` unchanged.
- `AmbiguityMarginFactor = 2.0` unchanged.

Why this safety rule matters:
An earlier attempt reduced Ambiguous counts but silently replaced already-resolved engineering coordinates, e.g. `PLT-20-001 E=560,00 -> E=632,00`. That version was rejected.
The corrected patch forbids replacing previously deterministic evidence.

Codex reported Release|x64 build PASS, 0 warnings/0 errors, and tests PASS.
ChatGPT then independently reviewed the diff and runtime-tested the real A5 drawing.
## 8. Latest independently validated A5 runtime result

After syncing the current Build DLLs into the AutoCAD bundle, disposable-copy runtime gave:

- Equipment placements: **300**
- Complete: **212**
- Incomplete: **64**
- Ambiguous: **24**
- QA findings: **92**
- Info: **4**
- Warnings: **88**
- Errors: **0**
- `PP-COORD-001`: 64 findings
- `PP-COORD-002`: 24 findings
- `PP-COORD-003`: 0 / PASS

Compared with pre-generalization A5 state:
- 309 placements / 198 complete / 70 incomplete / 41 ambiguous.

Safety comparison against old Complete placements:
- old Complete placements: 198
- common Complete placements whose status changed: **0**
- common Complete placements whose selected E/N evidence changed: **0**
- 3 removed old Complete records were false `TOMH-2550X180` block-name metadata.

Transitions among real retained placements:
- 195 Complete -> Complete
- 64 Incomplete -> Incomplete
- 24 Ambiguous -> Ambiguous
- 17 Ambiguous -> Complete
- 0 Complete -> Ambiguous
## 9. Current installed Build identity

The installed AutoCAD bundle was explicitly synchronized after the final Codex patch.
Important verified hashes:
- `EngiSentry.AutoCAD2021.dll` = `88E23B0E4124EE666F054F0BA570F25175B17C2EA0D42B6CF396A83FC1D2F151`
- `EngiSentry.PlotPlan.dll` = `D63C645FB42CF72AA3CD4E05EB9CE022C2A06E2F89C38A775FE252FC73CBC7D5`
- `EngiSentry.Semantics.dll` = `98B03700EB6C1FBE96C043DAFF3D8E63F858B24F343CA0B3C80BDCFCE0C1238E`

At sync time, source/build and installed copies matched.
AutoCAD was closed during the sync.

Important deployment lesson:
Codex can successfully build but the installed AutoCAD bundle may still contain an older dependency DLL. Before GUI runtime validation, compare/copy the complete Release dependency set, not only the main plugin DLL.

## 10. Historical issue: AcCoreConsole abnormal exit — RESOLVED / SUPERSEDED

A separate issue remains:
`accoreconsole.exe` often completes the EngiSentry command and writes the correct artifact, but then exits with Windows status `0xC00000FD` / decimal `-1073741571`.

Observed facts:
- command output/artifact is produced before the exit,
- tested DWG remains byte-identical,
- DBMOD/read-only behavior is intact,
- this issue is NOT proven to occur in normal AutoCAD GUI,
- it must not be confused with the safe E/N pair patch.

This historical issue is closed under acceptance A. Do not restart it unless a future source change directly affects host isolation. Do not modify semantic/coordinate logic to revisit this closed validation.
## 11. Remaining validation work — priority order

### A. AcCoreConsole crash — PASS / CLOSED
Host isolation acceptance passed. The historical diagnostic evidence above is retained for context; no further A work is active.

### B. Revision on real A5 family — PASS / CLOSED
Real A0->A1 revision validation passed. Do not repeat it unless a future source change directly affects revision behavior.

### C. Nested Zoom in real AutoCAD GUI — PASS / CLOSED
Results and Zoom runtime validation passed. Do not repeat it unless a future source change directly affects Results or navigation behavior.

### D1. Model/Paper provenance for Road/Boundary evidence — PASS / CLOSED
Text provenance and the Paper-space false-positive correction passed code review and independent runtime validation. Do not reopen D1 unless a future source change directly affects `SpaceKind` or provenance behavior.

### D2. Generic Road / Boundary geometry evidence — PASS / CLOSED
Generic deterministic, evidence-backed Road/Boundary association passed on official A0 and A1 drawings. The result is fail-closed: two Road labels remain Ambiguous and the FENCE label resolves to the compatible FENCE polyline; no Road/Boundary subject is created from geometry or layer names alone.

### E. Final full regression — PASS / CLOSED
Phase E passed the full command regression on disposable A5/A0/A1 drawings. A5/A0/A1 integrity passed; the A0 D2 regression passed; and A1 revision comparison passed against the active Phase E A0 baseline. No product defect was proven.
## 12. Plot Plan v1.1 real project dataset / standards

Project data folder:
`C:\Users\M.Zeynali\Documents\EngiSentry\_project-data\plotplan-v1.1`

Contains real Plot Plan A0/A1 DWGs, Equipment List XLSX, PCD-PD-135, IPS E-PR-190, IPS G-SF-150.
A0/A1 originals must remain untouched; automated work uses disposable copies.

Standards authority docs already created under:
`docs\plotplan-v1.1\STANDARDS_AUTHORITY_MATRIX.md`
`docs\plotplan-v1.1\standards-authority-matrix.json`

Critical engineering policy from document sender:
- some PCD-PD-135 flare-location terms contain major mistakes,
- IPS E-PR-190 is outdated and conflicts materially with IPS G-SF-150,
- spacing/distancing always requires expert judgment.

Therefore:
- PCD-PD-135 flare clauses are quarantined; never auto-enforce.
- IPS E-PR-190 is legacy/conflicting; never automatic governing authority.
- conflicts produce `STANDARD_CONFLICT` + expert review, not invented PASS/FAIL.
- universal hard-coded flare distances are forbidden.

Desired engineering outcome vocabulary includes:
`PASS`, `FAIL`, `ADVISORY`, `EXPERT_REVIEW_REQUIRED`, `STANDARD_CONFLICT`, `NOT_APPLICABLE`, `INSUFFICIENT_EVIDENCE`, `SOURCE_QUARANTINED`.
## 13. Commands / regression surface

User-facing command set currently includes:
- `ENGISENTRY`
- `ES_HELLO`
- `ES_SCAN`
- `ES_SEMANTIC`
- `ES_INDEX`
- `ES_QA`
- `ES_PLOTQA`
- `ES_PROFILEQA`
- `ES_CROSSQA`
- `ES_REVBASELINE`
- `ES_REVISION`

Normal EngiSentry commands are intended to be read-only.
Do not use Save/QSAVE/SAVEAS/QUIT as part of normal command automation.
Always hash the disposable DWG before/after runtime regression.

Canonical Release|x64 build:
`cd "C:\Users\M.Zeynali\Documents\EngiSentry"; & 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe' .\EngiSentry.sln /t:Rebuild /p:Configuration=Release /p:Platform=x64 /nologo`

When asking Codex to implement:
- inspect only required files,
- no broad repo scan/refactor,
- no repeated already-passed builds/tests,
- preserve working tree,
- one focused Release|x64 build and test pass,
- report exact changed files/behavior and STOP.

ChatGPT should then independently verify the diff and at least one real-DWG runtime path before declaring PASS.
## 14. Exact continuation point for the next chat

Do NOT redo the accepted A5 semantic/pair work or Phase E regression.

Current active phase:
**v1.1.0 release preparation / commit / package review.**

A/B/C, D1, D2, and E are PASS / CLOSED. Preserve the verified working tree; prepare the approved v1.1.0 metadata, commit scope, and package for independent review. Do not start a new milestone or change engineering logic.

## 15. One-line message to start the next chat

Paste this:

`Continue EngiSentry from the current handoff. A/B/C, D1, D2, and E are PASS/CLOSED and must not be rerun. Current work is v1.1.0 release preparation, commit, and package review. Preserve the verified working tree; do not start another milestone or change engineering logic.`

---
End of handoff.

## 16. A/B/C acceptance closure

Final acceptance status:
- A — CoreConsole / host isolation: **PASS**.
- B — Real A0->A1 revision validation: **PASS**.
- C — Results / Zoom runtime validation: **PASS / CLOSED**.

C runtime evidence:
- Initial QA row was current/selected when Results opened, and `Zoom To Evidence` was enabled immediately.
- Native Model-space Zoom To Evidence successfully navigated `PP-COORD-001 / CPS-00-02` from source handle `57D471` in `1003-00-EB-PI-PLP-0503-A1.dwg`.
- From `Layout1 / PAPER`, the same model-space finding switched to Model / MODEL and then navigated successfully.
- Nested evidence `43B540:4264BD:421C4E` for `PP-COORD-001 / DV-20-13` deterministically navigated via owner handle `43B540`, a real `BlockReference` (`A$C4c67e49c`, layer `ROAD-U-CEN`).
- EngiSentry Results remained modeless and usable; AutoCAD remained responsive.
- No original DWG was intentionally saved or overwritten; disposable copies were used where required.

Build/deployment evidence already validated:
- Release|x64: **PASS**, 0 warnings, 0 errors.
- Installed bundle DLL set matched the Release build; main plugin SHA256: `ADAF334EAE549DF222F9EEEE9720F979D88C383925E3C07B427647C16858243B`.

Deferred, non-blocking UX note:
- After some Zoom operations, the Results grid selection may fall back to the first row. This does not invalidate navigation or reopen C; defer any UX change to a future source change.

Do not reopen or repeat A/B/C acceptance unless a future source change directly affects them.

## 17. D1 acceptance closure

**D1 — Model/Paper provenance for Road/Boundary evidence: PASS / CLOSED.**

Accepted implementation:
- `TextSnapshot.SpaceKind` is a nullable trailing `DataMember(Order=12)`.
- Top-level Model DBText/MText is stamped `Model`.
- Top-level Paper/Layout DBText/MText is stamped `Paper`.
- Instantiated Model-space BLOCKNAME/ATTRIB/TEXT/MTEXT is stamped `Model`.
- `RoadLabels` and `BoundaryLabels` require `SpaceKind == "Model"` using ordinal case-insensitive comparison.
- Paper, null, and unknown provenance is excluded.
- Other semantic parsing remains unchanged.
- `PlotGeometryBuilder` was not changed.
- No D2 or layer-only Road/Boundary recognition was added.

Validation evidence:
- Focused tests: PASS.
- Release|x64 build: PASS; 0 warnings; 0 errors.
- `git diff --check`: PASS except existing line-ending notices.
- Independent ChatGPT diff review: PASS.

Runtime drawing:
`C:\Users\M.Zeynali\Desktop\Masoud\New folder\New folder\1003-00-EB-PI-PLP-0503-A1.dwg`

Before D1: GeometryAssociationCount = 2; Paper `FENCE` handle `39BD89` produced a false Boundary association; Paper `ACCESS ROAD` handle `387B14` produced a false Road association; total findings = 10; warnings = 10.

After D1: SupportedPrimitiveCount = 82; GeometryAssociationCount = 0; RoadAssociationCount = 0; BoundaryAssociationCount = 0; PP-GEO-001 = `NOT_APPLICABLE`; PP-GEO-002 = `NOT_APPLICABLE`; confirmed false-positive findings = 0; total findings = 8; warnings = 8; errors = 0.

DWG integrity: size before/after = 23,194,458 bytes; `LastWriteTimeUtc` unchanged; SHA256 before/after identical: `7E51EC0BDCE424DEFA46AFC5B63EA63ED4CBEB19A7785A79E0D198DC972BD807`.

D2 and E are now PASS / CLOSED. The approved next version is v1.1.0; the current next action is release preparation, commit, and package review. Do not reopen D1/D2/E unless a future source change directly affects their accepted behavior.

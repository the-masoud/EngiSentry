# EngiSentry Phase K — Governed Project Engineer Review Handoff

- Base HEAD: `2040a0170fc040e2e0d35c206732945ffe1fe435`
- Command: `ES_ENGINEERREVIEW`
- UI host: existing `VisualResultsPalette` / `InteractiveVisualResultsPalette` modeless WinForms surface
- Evidence source: accepted `docs/handoffs/P10.3-SUPERVISED-ENGINEER-PILOT-RECORD.md` (read-only; never modified)
- Purpose: present the pending P10.3 pilot results to a real project engineer through a dedicated
  human-review UI with bounded questions, record the engineer's observation evidence, and never
  create approval, authority policy, trusted-issuer status, private-key authority, or rule activation.

## What this workflow is not

A project engineer review records **human observation evidence only**. It is deliberately separate
from the P8 `HumanEngineeringReviewDecisionRecord` / P9 approval machinery. No response can be
converted into an `EngineeringDistanceApprovalRecord`, and no P9 gate overload accepts a review
response (compile-time and reflection-checked negative tests). Review ≠ approval: the
`REVIEW_ACCEPTED != APPROVED` invariant is asserted in the focused tests.

## Production files added

- `src/EngiSentry.Contracts/ProjectEngineerReviewModels.cs` — portable contracts (schema `PER-1`,
  statuses, classifications, answer vocabularies, request/response/evidence/question types). References
  no Autodesk assembly.
- `src/EngiSentry.Standards/ProjectEngineerReviewQuestions.cs` — the exact accepted P10.3 observation
  template: 14 questions (12 required bounded, 2 optional free text) across Criteria UX / Results UX /
  Engineering interpretation / Usability. No question added, broadened, or narrowed.
- `src/EngiSentry.Standards/ProjectEngineerReviewService.cs` — builds, validates, and atomically
  persists requests/responses; deterministic SHA-256 request identity; binding validation that rejects
  any response that does not bind to its request; completeness gate used by the UI.
- `src/EngiSentry.AutoCAD2021/ProjectEngineerReviewControl.cs` — the human-review UI: reviewer identity
  fields, evidence panel, all 14 bounded/free questions, classification, comment, Previous / Next /
  Next Pending navigation, and an explicit Save.

## Production files modified

- `src/EngiSentry.AutoCAD2021/Commands.cs` — `ES_ENGINEERREVIEW` command.
- `src/EngiSentry.AutoCAD2021/VisualResultsPalette.cs` — `ShowProjectEngineerReview()` host surface.
- `src/EngiSentry.AutoCAD2021/EngiSentry.AutoCAD2021.csproj` — compile entry.
- `AGENTS.md` — human engineering judgment protocol.

## Test files added / modified

- `tests/PhaseProjectEngineerReviewTests.cs` (added) — focused assertions.
- `tests/EngiSentry.Tests.csproj`, `tests/UnitTests.cs` (modified) — integration.

## Optional review-root override (testability, default behavior unchanged)

The queue loader materializes only the four real P10.3 pilot requests, so a pre-seeded synthetic
request would not otherwise appear in the UI. The smallest safe testability change was added:

- `ProjectEngineerReviewService.ReviewRootOverrideVariable = "ENGISENTRY_ENGINEERING_REVIEW_ROOT"`,
  following the existing `ENGISENTRY_*` environment-variable convention.
- The parameterless constructor resolves the override (`ResolveRoot()`); when the variable is unset or
  blank the default `%LOCALAPPDATA%\EngiSentry\artifacts\qa\engineering-review` root is used, so
  production behavior is unchanged and the override is disabled unless explicitly set.
- `LoadQueue()` additionally materializes pre-seeded persisted requests **only** when the root is not
  the default root. The production default root holds only the four P10.3 pilot requests, so the real
  queue and production queue semantics are unchanged; no arbitrary non-P10.3 request is ever included.
- No change to P9 governance/approval behavior, and no change to queue semantics on the default root.

Focused-test coverage for this override: disabled unless explicitly set, an isolated override root
materializes a pre-seeded test-only request with its exact identity while still carrying the four
pilot requests, a pre-seeded request stays PENDING until answered, and the default/production root is
untouched while the override is set.

## Build / test / deployment evidence

- Release x64 rebuild with `-warnaserror` (MSBuild 17.14): PASS, 0 warnings, 0 errors.
- Focused Project Engineer Review assertions: PASS, 64 assertions (55 + 9 override assertions).
- Full Release test executable `tests/bin/x64/Release/EngiSentry.Tests.exe`: PASS, exit code 0;
  final line `All EngiSentry M2.2 through M5.0 tests passed.`
- Deployment: current Release build copied into
  `%APPDATA%\Autodesk\ApplicationPlugins\EngiSentry.bundle\Contents\Win64`; SHA-256 equality verified
  for all 11 deployed DLLs against the Release output.

## AutoCAD runtime acceptance (AutoCAD 2021 x64)

- Disposable drawing: `artifacts\runtime-validation\per-acceptance\98102-AJR-000-PD-PLP-0001-B05_1.dwg`
  (disposable runtime fixture; not a protected source drawing).
- Review-root override set only for the acceptance AutoCAD process (process scope; never persisted to
  User/Machine scope; cleared afterwards).
- Exactly one unmistakably test-only synthetic request (`WorkflowSourceType=TEST_ONLY`,
  case `AA-TEST-ONLY-RUNTIME-UI-CASE`) pre-seeded into the isolated override root; the four real P10.3
  pilot requests were also materialized into that isolated root only and left unanswered.
- `ES_ENGINEERREVIEW` executions: exactly 1, invoked through AutoCAD COM `SendCommand` after
  `NETLOAD`; the modeless review window opened enabled.
- STEP 17 control verification: combos=13 (12 bounded question combos with the exact
  empty/Yes/No, empty/Yes/No/N/A and empty/Yes/No/Needs review vocabularies, plus the classification
  combo with exactly empty/PASS/USABILITY_ISSUE/SUSPECTED_PRODUCT_DEFECT/DRAWING_EVIDENCE_LIMITATION/
  ENGINEERING_REVIEW_REQUIRED), edits=6 (reviewer name, reviewer role, read-only evidence, two
  free-text question answers, comment), buttons=4 (Previous disabled on case 1; Next, Next Pending,
  Save enabled); all four question sections and all fourteen question labels present; evidence panel
  showed the synthetic TEST_ONLY identity.
- Incomplete Save blocked: clicking Save with empty reviewer fields produced
  `Review not saved - Reviewer display name is required before saving.` and wrote no response file.
- STEP 18 explicit Save: reviewer name/role, all twelve bounded answers, classification
  (`USABILITY_ISSUE`), comment and both free-text answers entered through the real control surfaces;
  one Save click wrote exactly ONE response, bound to the synthetic request only
  (schema `PER-1`, matching `ReviewRequestId`/`RequestSha256`/`DrawingName`/`ReviewedCaseId`, 14
  answers, UTC timestamp, `ResponseId` present, classification preserved, no `.tmp` left, request
  hash unchanged by the save). The UI then reported `This case is already completed.`
- P10.3 real queue: requests=4, pending=4, real responses written=0. The production review root was
  never written to during acceptance and remained 4 requests / 0 responses throughout.
- DBMOD: before `0`, after NETLOAD `0`, after `ES_ENGINEERREVIEW` `0`, at quit `0`; unchanged.
- DWG integrity: SHA-256 before `DBD6D6BFDE04770EF250488DD926DC162EA5E246B3A7971B2745B65F85AC8CAA`
  == after; size `2890464` unchanged; last-write UTC `2026-09-23T09:57:04.2133300Z` unchanged;
  drawing was not saved; AutoCAD exited cleanly with NO save-changes prompt.
- Network / TypeSafe / Jev calls: 0. Benchmark executions: 0.
- Production authority safety: no approval created, no authority policy created, no trust store
  modified, no private key created, no rule activated.
- Test-owned runtime artifacts (isolated override review root and the seeder binary) were removed
  afterwards; the production review root is unchanged at 4 requests / 0 responses.
- Automation method (deterministic, no cursor): AutoCAD COM `SendCommand` for command entry; Windows
  UI Automation tree reads for window/control identity; real control-surface interaction via Win32
  messages against the actual modeless control HWNDs (`CB_SETCURSEL` + `CBN_SELCHANGE` for combos,
  `WM_SETTEXT`/`WM_GETTEXT` for edits, `BM_CLICK` for Save/Next). Cursor-based input injection is
  unavailable in this environment, so no mouse/capture path was used.

## UI polish + AutoCAD 2021 visual acceptance (follow-up)

The accepted workflow's review surface was visually polished for professional engineer use. UI/UX
only: no review logic, persistence, P10.3 question set, answer vocabularies, classifications,
governance, or authority behavior changed, and no new dependency was introduced.

Files touched:

- `src/EngiSentry.AutoCAD2021/ProjectEngineerReviewControl.cs` — presentation-only rework.
- `src/EngiSentry.AutoCAD2021/VisualResultsPalette.cs` — the review host window opens at
  `1000x960` instead of `960x800`.
- `tests/PhaseProjectEngineerReviewUiTests.cs` (new) + `tests/EngiSentry.Tests.csproj` +
  `tests/UnitTests.cs` — UI-contract assertions that pin the polished surface (button labels and
  the primary Save styling, one answer control per question with the exact vocabularies, the
  classification combo, empty-by-default inputs, header status/counter, evidence as readable
  key/value content with truncation-ready hashes, navigation, blocked incomplete save, and layout
  sanity with no sibling overlaps at the accepted window size).

Presentation changes:

- Visual hierarchy: a header band (title / status badge / case counter), then labelled sections —
  Engineer Identity, Case Summary, Software Evidence, Review Questions, Classification,
  Engineer Comment — and a footer action bar with an inline validation/status line.
- Section grouping: each section is a card with an uppercase small-caps header on the shared dark
  palette; the four question groups (Criteria UX / Results UX / Engineering interpretation /
  Usability) are separated by their own strips.
- Case summary and Software Evidence are stacked full-width with key/value rows; long values
  (including the 64-hex request and drawing hashes) ellipsis-truncate and expose the full value in
  a tooltip.
- All 14 questions render in a two-column question/answer grid with a dedicated answer column;
  required questions carry a `*` marker and a note that nothing is stored until Save Review.
- Reviewer identity is two labelled fields with an explicit "recorded as evidence only" note; the
  engineer comment is a genuine multiline box; classification is its own wide combo.
- Previous / Next / Next Pending are flat dark navigation buttons; Save Review is a wider,
  higher-contrast primary button set apart at the right end of the action bar.
- Resize behavior: the summary/evidence middle absorbs growth, the questions section keeps a
  guaranteed usable height, and the whole palette scrolls when the host window is short.

Two real visual defects were found by the runtime acceptance and fixed once:

1. Software Evidence and Case Summary value labels were collapsing to 1-11 px wide. Root cause:
   the evidence grid placed each key/value pair panel into the narrow key column, and `AutoSize`
   on the grids shrank the percent column. Fixed by making the evidence grid a single full-width
   column, giving the summary/evidence/questions grids explicit heights, and stacking the two
   sections full-width.
2. Shrinking the window to a moderate 840x720 squeezed the questions section to an unusable
   ~47 px sliver. Fixed by giving the questions section a guaranteed height and letting the
   summary/evidence row absorb resize, with palette-level scrolling for short windows.

Build / test / deployment:

- Release x64 rebuild with `-warnaserror` (MSBuild 17.14): PASS, 0 warnings, 0 errors.
- Focused tests: PASS — Phase Project Engineer Review 64 assertions, Phase Project Engineer Review
  UI 74 assertions. Full Release test executable: PASS, exit code 0, final line
  `All EngiSentry M2.2 through M5.0 tests passed.`
- Deployment: current Release build copied into
  `%APPDATA%\Autodesk\ApplicationPlugins\EngiSentry.bundle\Contents\Win64`; SHA-256 equality
  verified for all 10 deployed DLLs against the Release output.

AutoCAD 2021 visual runtime acceptance (visual-only; no review was filled or saved):

- Disposable drawing: `artifacts\runtime-validation\per-acceptance\98102-AJR-000-PD-PLP-0001-B05_1.dwg`
  (disposable runtime fixture; not a protected source drawing).
- Review-root override set only for the acceptance AutoCAD process (process scope; never persisted
  to User/Machine scope; cleared afterwards). The isolated root received only the four real P10.3
  pilot requests materialized by the queue loader; the production review root was never read.
- `ES_ENGINEERREVIEW` executed exactly once, through AutoCAD COM `SendCommand` after `NETLOAD`
  from the deployed bundle; the modeless review window opened enabled at `1000x920`.
- Surface inventory: combos=13 (12 bounded answers with the exact empty/Yes/No, empty/Yes/No/N/A
  and empty/Yes/No/Needs review vocabularies, plus the classification combo), edits=5 (reviewer
  name, reviewer role, two optional free-text answers, comment), buttons=4 (Previous disabled on
  case 1; Next, Next Pending enabled; Save Review a 162x28 primary control, wider than the
  navigation buttons and set apart at the right end).
- All ten section headers and all fourteen question labels were present and readable; the status
  badge reported PENDING and the case counter read "Case 1 of 4" over the real four-case queue;
  evidence rows rendered as readable key/value content with the 64-hex hashes at full width
  (759 px value columns) — no label collapsed.
- Incomplete-save validation presentation only: clicking Save with empty reviewer fields showed
  `Review not saved - Reviewer display name is required before saving.` inline and wrote no
  response.
- Navigation: Next advanced through cases 2-4 and Previous moved back; every real P10.3 case
  remained PENDING; Next Pending landed on a pending case. Navigation wrote no responses.
- Moderate resize: enlarging to 1220x1040 and shrinking to 840x720 left the control inventory
  unchanged (13/5/4) with every field still usable, and the case counter survived.
- P10.3 real queue: requests=4, pending=4, real responses written=0. The production review root was
  never written and remained 4 requests / 0 responses throughout.
- DBMOD: before `0`, after NETLOAD `0`, after `ES_ENGINEERREVIEW` `0`; unchanged.
- DWG integrity: SHA-256 before
  `DBD6D6BFDE04770EF250488DD926DC162EA5E246B3A7971B2745B65F85AC8CAA` == after; size `2890464`
  unchanged; last-write UTC `2026-09-23T09:57:04.2133300Z` unchanged; drawing was not saved; AutoCAD
  exited with no save-changes prompt.
- Production authority safety: no approval created, no authority policy created, no trust store
  modified, no private key created, no rule activated.
- Test-owned runtime artifacts (the isolated override review root) were removed afterwards; the
  production review root is unchanged at 4 requests / 0 responses.
- Automation method (deterministic, no cursor): AutoCAD COM `SendCommand` for command entry;
  UI Automation control-view tree walks (by window class) for window/control identity and geometry;
  Win32 `BM_CLICK` against the real button HWND for the validation presentation; `MoveWindow` for
  resize. Cursor-based input injection is unavailable in this environment, so no mouse/capture
  path was used.

## UI polish follow-up pass (presentation only)

A second presentation-only pass refined the accepted polished surface. No review logic, persistence,
P10.3 question set, answer vocabularies, classifications, governance, or authority behavior changed.

- Card order was rebalanced so the primary review flow fits the first screen: Engineer Identity,
  Review Questions, then Engineer Disposition (classification + comment) now sit above the fold at
  the accepted 1000x960 window, with all 14 questions and all four section headers visible without
  scrolling. Case Summary and Software Evidence follow as audit reference material.
- The live "N required answers remaining" and evidence counter chips were clipping their text at a
  default 100 px width; they are now sized for their longest text, right-aligned, and
  truncation-ready, so a counter never clips mid-update.
- Card headers gained a subtly tinted band with a hairline divider for clearer section hierarchy.
- Dark-theme polish: the queue progress rail is a rounded pill, the evidence key/value list is
  faintly zebra-banded for scanability, disabled navigation buttons read as buttons again, and the
  header status stack (badge / case counter / progress rail) was widened and centered.

Build / test:

- Debug x64 rebuild: PASS, 0 errors. Focused UI contract assertions: PASS, 74 assertions (including
  the no-sibling-overlap and field-sizing layout sanity at the accepted window size); full test
  executable: PASS, final line `All EngiSentry M2.2 through M5.0 tests passed.`

## Status

- Implementation: complete.
- Build / focused tests / full tests / deployment / AutoCAD runtime visual acceptance: PASS.
- Real P10.3 engineering reviews: still PENDING and untouched — no engineer answer was fabricated.
- Commits: `feat: add governed engineer review workflow`, then `ui: polish project engineer review`,
  then a second presentation-only polish pass (`ui: polish engineer review surface`).
- Push: NO.
- Next: REAL PROJECT ENGINEER completes the four pending P10.3 reviews.

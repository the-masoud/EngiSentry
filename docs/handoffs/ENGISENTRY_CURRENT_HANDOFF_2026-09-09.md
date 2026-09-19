# EngiSentry Current Handoff — 2026-09-09

## Product and baseline

EngiSentry — Engineering QA & Drawing Intelligence

- Runtime: AutoCAD 2021 x64 / .NET Framework 4.8
- Starting released baseline: v1.2.0
- Starting commit: `fa8064d1f73f1c874d10008450705482c02bafc1`

## Accepted roadmap status

- P6.1 — Deterministic Runtime Authority Context: PASS / CLOSED
- P6.2 — Authority Output Contracts / PlotQA Report: PASS / CLOSED
- P6.3 — ES_PLOTQA Runtime Integration: PASS / CLOSED
- P6.4 — Engineering Review Exposure: PASS / CLOSED
- P6.5 — Focused Safety Tests: PASS / CLOSED
- P6.6 — AutoCAD 2021 Runtime Acceptance: PASS / CLOSED

P6 — Standards Authority Runtime Integration: PASS / CLOSED.

## P6 architecture

The accepted P6 design provides:

- deterministic Standards Authority runtime context;
- `AuthorityResolution`;
- `ExecutionGate`;
- `ReviewProjection`;
- `PlotPlanStandardsAuthorityReport`;
- ES_PLOTQA runtime integration; and
- read-only Engineering Review exposure in the Results surface.

ES_PLOTQA constructs the authority-bearing PlotQA report through the shared production path. The ReviewProjection is carried for the in-memory Engineering Review surface and is excluded from the existing PlotQA JSON contract.

## P6 safety guarantees

- 4 authority rules;
- 4 execution-gate decisions;
- 4 review items;
- 0 auto-executable rules;
- the flare rule remains `SOURCE_QUARANTINED`;
- no authority-generated QA finding;
- no automatic engineering verdict;
- ReviewProjection is excluded from PlotQA JSON; and
- Engineering Review remains read-only.

Exact accepted gate states:

- `runtime.plotplan.authority.epr.legacy` → `EXPERT_REVIEW_REQUIRED`
- `runtime.plotplan.authority.gsf.primary` → `EXPERT_REVIEW_REQUIRED`
- `runtime.plotplan.authority.pcd.flare` → `SOURCE_QUARANTINED`
- `runtime.plotplan.authority.pcd.general` → `EXPERT_REVIEW_REQUIRED`

## Final AutoCAD 2021 runtime evidence

- ES_PLOTQA demand-load: PASS
- real AutoCAD 2021 runtime: PASS
- StandardsAuthority present: PASS
- authority rules / gate decisions / review items: 4 / 4 / 4
- auto-executable: 0
- automatic standards findings: 0
- automatic standards RuleResults: 0
- automatic authority Pass/Fail: No
- ReviewProjection serialized in PlotQA JSON: No
- Engineering Review Results exposure: PASS
- Engineering Review grid read-only: PASS
- DBMOD unchanged: PASS

Accepted artifact:

`C:\Users\M.Zeynali\AppData\Local\EngiSentry\artifacts\qa\plotplan\PLOTPLAN-A0-E-FINAL.dwg.20260908T194908658Z.plotplan.qa.json`

Runtime validation DWG:

`C:\Users\M.Zeynali\Documents\EngiSentry\artifacts\runtime-validation\e-final\PLOTPLAN-A0-E-FINAL.dwg`

DWG integrity:

- SHA256: `3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4`
- Size: 56,576,036 bytes
- Integrity: PASS

## Working model

- ChatGPT owns architecture, review, and runtime validation.
- Codex owns implementation, builds, and tests.
- Deterministic engineering evidence comes first.
- Engineering facts must never be guessed.

## P7 accepted roadmap

- P7.0 — Scope Definition / Gap Audit: PASS / CLOSED
- P7.1 — Source / Clause Traceability Enrichment: PASS / CLOSED
- P7.2 — Deterministic Review-Package Contract and Artifact: PASS / CLOSED
- P7.3 — AutoCAD 2021 Runtime Acceptance: PASS / CLOSED

**P7 — Standards Review Traceability & Decision Package: PASS / CLOSED.**

### P7.1 architecture

P7.1 enriches `StandardClauseAuthorityResolution` with optional source/clause provenance, reusing `StandardSourceRegistry` and `StandardClauseRegistry`. It does not create parallel P7 source/clause DTOs. Existing `RuleId`, `ClauseId`, `SourceId`, and `PolicyId` identities are preserved; missing metadata remains absent/null. `ReviewProjection` receives the enriched provenance, while the existing `PlotPlanQaReport` continues to exclude `ReviewProjection` from JSON.

Accepted source identities:

- `trc.pcd-pd-135.rev1` — SHA256 `A59E7C6C05832565AEED40D827A702A61E4CB9AF213E8AC95EE39711BF8D572E`
- `ips.e-pr-190.rev1.2009` — SHA256 `FC0FF48E0CD3A4658BD6A19DE285A0D9C383BAF4484CE9A9156CE44EAAF4E152`
- `ips.g-sf-150.rev1.2023` — SHA256 `B767EE1829C2F901CD6D2A1E59C97FB1030789C65F90FD530CDA6A67C3E99B4B`

### P7.2 architecture

`StandardAuthorityReviewPackage` is a standalone package with `SchemaVersion = "1.0"` containing `DrawingName`, `ScanTimestampUtc`, `StandardsAuthority`, and `ReviewProjection`. `StandardAuthorityReviewPackageBuilder` performs deterministic detached/deep-copy packaging with deterministic `RuleId` and `ClauseId` ordering. No second authority runtime context is constructed. The package uses the same PlotPlanQaReport authority/review evidence created by ES_PLOTQA, is emitted as a separate sidecar, and is not embedded in `PlotPlanQaReport`.

Artifact naming is `<drawing>.<snapshot timestamp>.standards.review.json` under `%LOCALAPPDATA%\\EngiSentry\\artifacts\\qa\\plotplan`. The writer uses `DataContractJsonSerializer` and `FileMode.CreateNew`, reuses the existing snapshot timestamp, and uses neither a random ID nor `DateTime.UtcNow`.

### P7 safety guarantees

- Authority rules: 4
- Execution gate decisions: 4
- Review items: 4
- Auto-executable rules: 0
- Authority-generated `QaFindings`: 0
- Authority-generated `QaRuleResults`: 0

Accepted gate states:

- `runtime.plotplan.authority.epr.legacy` → `EXPERT_REVIEW_REQUIRED`
- `runtime.plotplan.authority.gsf.primary` → `EXPERT_REVIEW_REQUIRED`
- `runtime.plotplan.authority.pcd.flare` → `SOURCE_QUARANTINED`
- `runtime.plotplan.authority.pcd.general` → `EXPERT_REVIEW_REQUIRED`

P7 introduces no automatic standards PASS/FAIL, no executable distance, no DWG mutation, and Engineering Review remains read-only.

Flare safety is explicit: `RuleId = runtime.plotplan.authority.pcd.flare`, `ClauseId = trc.pcd-pd-135.3.4.13.b`, page 24, with clause evidence containing `90 meters`. The phrase “90 meters” is preserved only as source/clause evidence. It is not an executable engineering distance rule, drawing comparison, automatic PASS/FAIL condition, finding, or RuleResult. Flare remains `SOURCE_QUARANTINED` with `AutoExecutionAllowed=false`.

### P7.3 AutoCAD 2021 runtime evidence

- Runtime: AutoCAD 2021 x64
- Accepted fixture: `C:\Users\M.Zeynali\Documents\EngiSentry\artifacts\runtime-validation\e-final\PLOTPLAN-A0-E-FINAL.dwg`
- Accepted PlotPlan artifact: `C:\Users\M.Zeynali\AppData\Local\EngiSentry\artifacts\qa\plotplan\PLOTPLAN-A0-E-FINAL.dwg.20260909T070105398Z.plotplan.qa.json`
- Accepted review sidecar: `C:\Users\M.Zeynali\AppData\Local\EngiSentry\artifacts\qa\plotplan\PLOTPLAN-A0-E-FINAL.dwg.20260909T070105398Z.standards.review.json`
- Same snapshot timestamp: PASS
- SchemaVersion: 1.0
- Authority/gate/review: 4/4/4
- Auto-executable: 0
- Three source SHA identities: PASS
- Flare clause/page/evidence: PASS
- ReviewProjection absent from PlotPlan JSON: PASS
- ReviewProjection present in review sidecar: PASS
- StandardAuthorityReviewPackage not embedded in PlotPlan JSON: PASS
- Engineering Review surface present: PASS
- Engineering Review grid read-only: PASS
- DBMOD at runtime: 0
- DWG saved: No
- AutoCAD closed after validation: Yes

DWG final integrity is PASS / unchanged: SHA256 `3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4`, size 56,576,036 bytes, `LastWriteTimeUtc=2025-01-27T13:21:43Z`.

The validation script caused ES_PLOTQA to execute twice within the same single AutoCAD launch because a blank script line repeated the previous command. Both executions succeeded. The accepted artifact pair is the `20260909T070105398Z` pair above. This was a validation-script issue, not a product defect.

### P7 validation

- P7.1/P7.2 final build: PASS
- Warnings: 0
- Errors: 0
- Test harness: PASS, exit 0
- Test final line: `All EngiSentry M2.2 through M5.0 tests passed.`
- P7.3 real AutoCAD 2021 runtime: PASS

## P8 — Human Engineering Review Lifecycle: PASS / CLOSED

- P8.0 — Scope / Gap Audit: CLOSED
- P8.1 — Human Review Decision Contract: CLOSED
- P8.2 — Deterministic Decision Persistence: CLOSED
- P8.3 — Evidence / Staleness Validation: CLOSED
- P8.3H — Append-only History Resolution: CLOSED
- P8.4 — AutoCAD Engineering Review Decision UI: CLOSED
- P8.5 — Human Review Safety / Regression Closure: CLOSED
- P8.6 — Positive AutoCAD Runtime Acceptance: CLOSED
- P8.7 — Negative Runtime Safety: CLOSED
- P8.8 — Forensic Closure: CLOSED
- P8.9 — Handoff Closure: CLOSED

P8 provides immutable human engineering-review decision records with a deterministic `DecisionRecordId`, exact persisted review-package SHA256 binding, reviewer/state/comment input, explicit confirmation before append-only write, and an explicit `SupersedesDecisionRecordId` chain. Supported human states are `REVIEW_ACCEPTED`, `REVIEW_REJECTED`, and `NEEDS_CLARIFICATION`; evidence is `CURRENT` or `STALE`; history is `ACTIVE` or `SUPERSEDED`.

**REVIEW_ACCEPTED IS NOT APPROVED.** Human review has zero automatic executable engineering effect. It creates no automatic engineering rule, finding, PASS/FAIL verdict, or DWG mutation.

### P8.6 C1 timestamp-precision defect and resolution

The initial post-save decision incorrectly became `STALE` with `SCAN_TIMESTAMP_CHANGED`. `DrawingScanner.ScanTimestampUtc` could contain sub-millisecond ticks while `DataContractJsonSerializer` persisted `DateTime` at millisecond precision. `StandardAuthorityReviewPackageBuilder` now normalizes `ScanTimestampUtc` to UTC whole-millisecond precision before package persistence, hashing, and UI use. Validator equality was not weakened. Regression and real AutoCAD runtime prove a newly persisted decision is `CURRENT`.

### P8.6 positive AutoCAD 2021 runtime evidence

- Accepted review package: `PLOTPLAN-A0-E-FINAL.dwg.20260909T115400285Z.standards.review.json`
- Package SHA256: `09FE1EB3BD4413D2EDD4E65C8F6D60A876D4D4174E504AD238CEC4963A7CB1BB`
- Authority / gate / review: 4 / 4 / 4; auto-executable: 0.
- Flare: `runtime.plotplan.authority.pcd.flare` = `SOURCE_QUARANTINED`, `NEVER_AUTO_ENFORCE`, `AutoExecutionAllowed=false`.
- Decision A: `633A8B80E23B17A76AC0DD1AB041113B479C3D51F7A547C0546E6287829CAF19`; `REVIEW_ACCEPTED`; immediate evidence `CURRENT`; initial history `ACTIVE`; file SHA256 `76542D527E9E1BE8B87000094D1A5A4CBC46D364C21C28D991025988B6E22EEF`.
- Decision B: `69F2EB1AD31D541BA91701466A36772893FDB822243220CFDA2921326C59EEAC`; `NEEDS_CLARIFICATION`; supersedes A; evidence `CURRENT`; history `ACTIVE`; file SHA256 `A98CBD4E2AA9D5840E2510BF49991FCDE279222C12A835B845E660AB43D64760`.
- After B: A = `SUPERSEDED`; B = `ACTIVE`. Successful UI refresh after immutable writes proves the internal DBMOD before/after guard did not trip. No drawing save occurred.

### P8.7 negative runtime evidence

- Confirmation cancel: PASS. The actual confirmation was shown; Cancel wrote no third decision and history was unchanged.
- Package tamper guard: PASS. Pristine SHA256 `09FE1EB3BD4413D2EDD4E65C8F6D60A876D4D4174E504AD238CEC4963A7CB1BB`; tampered SHA256 `71FF1E2C2155D35B1C45E56A25E54C9768430975BACF272A6F3F8ECB37402E5C`; the UI blocked write with: `Review package changed since ES_PLOTQA; rerun ES_PLOTQA before recording a decision.`
- Active-document mismatch: PASS. A1 was activated in the same AutoCAD PID through `AcLauncher.exe /O`; an A0 Results save was blocked with the active-document mismatch protection. Decision count remained two and A/B were unchanged.
- Stale re-review: PASS. Rerunning A0 `ES_PLOTQA` made B `STALE` while remaining `ACTIVE`, with `REVIEW_PACKAGE_SHA_CHANGED` and `SCAN_TIMESTAMP_CHANGED`. Decision C `0D348B24C16933F3E258384C857D21E488331DA62722042C8995C3721151FBAA` is `REVIEW_ACCEPTED`, `CURRENT`, `ACTIVE`, supersedes B, and has file SHA256 `5BB9BDB7BE19AC4D7247EB55FBC1688CB06ADAFA9A733F6E7CACA9C14ABF2684`.

### P8.8 forensic closure

Final chain: A (`REVIEW_ACCEPTED`) → B (`NEEDS_CLARIFICATION`) → C (`REVIEW_ACCEPTED`); A and B are `SUPERSEDED`, C is `ACTIVE` and binds the final package SHA256 exactly.

- Final package: `PLOTPLAN-A0-E-FINAL.dwg.20260909T123021044Z.standards.review.json`
- Final package SHA256: `16D1737B46A795AD275502D48F87EBC8F449BD996E7E51E9150584BA81311D0E`
- Final authority / gate / review: 4 / 4 / 4; auto-executable: 0.
- Gate states: EPR legacy = `EXPERT_REVIEW_REQUIRED`; GSF primary = `EXPERT_REVIEW_REQUIRED`; PCD flare = `SOURCE_QUARANTINED`; PCD general = `EXPERT_REVIEW_REQUIRED`.
- C flare evidence binds `trc.pcd-pd-135.3.4.13.b`, `trc.pcd-pd-135.rev1`, and SHA256 `A59E7C6C05832565AEED40D827A702A61E4CB9AF213E8AC95EE39711BF8D572E`.
- Across A/B/C decision JSON, `APPROVED`, executable distance fields, `EngineeringDistanceRuleDefinition`, and `90 meters` are absent; `AuthenticatedReviewerIdentity` is null/absent. `90 meters` remains P7 source evidence only.

### P8 runtime safety and installation

- Disposable/source A0 SHA256: `3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4` — PASS.
- Disposable/source A1 SHA256: `BC317D2492FA22728EF4BAF4086FDEFBF3ADFCF384989E89894ED1E1A5DEA24F` — PASS.
- Installed Release/bundle identity: identical. AutoCAD DLL `DAF4365B50A021895ACB709A2B9DAF7C4AC20EEAD01D1AE4B45E30652DD9CE4A`; Contracts DLL `AAACC5C6EBA411B7A027D6DD494556036B0D65245715AF4B7CDF27AD57FEC918`; Standards DLL `EC8E89331447C02DFAE04A66468059330DC4C7CFCEB50364CB6D5424D12B6AF6`.
- Runtime automation note: AutoCAD command entry can use WPF `AutoCompleteEdit_1` with `AttachThreadInput`/SendKeys; the Engineering Review DataGridView is weak in UIA but exposes deterministic MSAA `IAccessible` rows; `AcLauncher.exe /O` can activate a second DWG in the same AutoCAD instance for active-document-mismatch validation.

## P9 — Project Approval Governance: PASS / CLOSED

- P9.0 — Scope / Gap Audit: CLOSED / PASS
- P9.1 — Immutable Engineering Approval Evidence: CLOSED / PASS
- P9.2A — Approval Catalog / History / Currentness: CLOSED / PASS
- P9.2B — Governing Project Approval Authority: CLOSED / PASS
- P9.2C1 — Governed Rule Resolution and Detached Provenance: CLOSED / PASS
- P9.2C2A — Signed Project-Authority Policy Trust Layer: CLOSED / PASS
- P9.2C2B — Pure Governance Aggregate: CLOSED / PASS
- P9.2C2C — ES_PLOTQA Runtime Governance Integration: CLOSED / PASS
- P9 — Formal Closure: CLOSED / PASS

P9.2C2C real AutoCAD 2021 x64 manual runtime acceptance passed. Governance contained exactly two items: Road and Fence, both `PROJECT_RULE_ONLY` with null `AuthorityState`; exactly three existing project-distance evaluations ran, with zero `std.plotplan.*` evaluations, and UnitBoundary behavior remained unchanged. P7 authority/gate/review remained 4/4/4 with auto-executable count 0; flare remained `SOURCE_QUARANTINED`. No approval, trust, signed-policy, private-key, or P8 decision artifact was fabricated. The protected verification DWG remained byte-identical.

The failed `/b` automation experiment modified the canonical A0 disposable DWG; the cause is not attributed to EngiSentry. The changed bytes were preserved at `C:\Users\M.Zeynali\Documents\EngiSentry\artifacts\runtime-validation\e-final\forensics\PLOTPLAN-A0-E-FINAL.post-p92c2c-b-script.D22F1B6D.dwg` with SHA256 `D22F1B6DD58EF9F59B9A38938DE71722D64863CD0334E8BB09A3BE491EDB4B8C`. The original BAK remained the exact baseline, and the canonical A0 was restored byte-identically from it with SHA256 `3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4`.

## Next phase

**P10.2 — Pilot Results & Criteria UX.**

Accepted phases A through P10.1 must not be reopened unless a future source change directly affects them.

## P10.1 — Engineer Pilot Bundle and Installation Baseline: CLOSED / PASS

- Pilot identity: `v1.3.0-rc1`.
- Canonical manifest AppVersion: `1.3.0`.
- AutoCAD 2021 x64 / R24.0 demand-load targeting is preserved.
- The clean pilot bundle is `artifacts/release/v1.3.0-rc1`.
- Runtime set: `EngiSentry.AutoCAD2021.dll`, `EngiSentry.Contracts.dll`, `EngiSentry.Core.dll`, `EngiSentry.CrossDocument.dll`, `EngiSentry.PlotPlan.dll`, `EngiSentry.Revision.dll`, `EngiSentry.Rules.dll`, `EngiSentry.Semantics.dll`, `EngiSentry.Standards.dll`, and `ExcelDataReader.dll`.
- `SHA256SUMS.txt` and `PILOT-README.md` are included in the pilot package.
- Installation target: `%APPDATA%\Autodesk\ApplicationPlugins\EngiSentry.bundle`.
- The local clean-install baseline is validated without stale backup DLLs or user-data deletion.
- Clean-install AutoCAD 2021 runtime acceptance passed on the protected `PLOTPLAN-A0-E-FINAL-P92C2C-VERIFY.dwg`.
- New QA artifact timestamp: `20260910T160941507Z`; new standards review artifact timestamp: `20260910T160941507Z`.
- Runtime governance: 2 items, both `PROJECT_RULE_ONLY` (Fence and Road); authority state remained null.
- Engineering-distance evaluations: 3 project evaluations; `std.plotplan.*` evaluations: 0.
- P7 authority / gate / review: 4 / 4 / 4; auto-executable: 0.
- Protected DWG SHA256 remained unchanged: `3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4`.
- Installed bundle contained exactly 10 clean runtime files and all package/install hashes were identical.
- No new criteria, approval, trust, signed-policy, private-key, or P8 human-review artifact was created.
- P10.1 runtime acceptance is CLOSED / PASS.

Next active task: **P10.2 — Pilot Results & Criteria UX**.

## P10.2 — Pilot Results & Criteria UX: CLOSED / PASS

- ES_PLOTQA Results now exposes three read-only project-distance rows: `Equipment → Road`, `Equipment → Plant / Security Fence`, and `Equipment → Unit Boundary`.
- Each row shows the configured minimum, observed distance and resolved-placement count from existing basis/applicability-matched runtime measurement evidence, the existing evaluation state with engineer-facing wording, and a concise interpretation.
- Unresolved measurements display `<not resolved>`; no measurement is recomputed or fabricated.
- `PROJECT_RULE_ONLY` is explained as a project criterion with no active approved standards-governance basis.
- The current QA artifact path is visible in the Results surface; `Open Output Folder` remains available.
- The criteria dialog now clearly identifies project criteria, the project directory, and the Plant / Security Fence label; validation, persistence, and Cancel behavior are unchanged.
- No engineering calculations, rule execution, geometry association, governance, artifact schema, review persistence, or DWG behavior changed.
- Release|x64 rebuild: PASS, 0 warnings, 0 errors. Existing automated harness: PASS.
- AutoCAD 2021 pilot UX runtime acceptance: PASS; accepted artifact timestamp: `20260912T055851013Z`.
- Runtime project criteria were Road `3 m`, Plant / Security Fence `30 m`, and Unit Boundary `15 m`; Plant / Security Fence was PASS with resolved observed-distance evidence, while Road and Unit Boundary were INSUFFICIENT_EVIDENCE.
- Runtime governance contained 2 `PROJECT_RULE_ONLY` items; P7 authority / gate / review remained 4 / 4 / 4 with auto-executable count 0.
- Protected DWG SHA256 remained unchanged: `3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4`.
- Accepted AutoCAD DLL SHA256: `8BA1FA2EAC97C4020FA02F27A1AC9134352F2EA75C6DA345E7430F535FC50659`.
- The v1.3.0-rc1 pilot package is synchronized to the accepted runtime; package/install runtime hashes are identical 10/10.

Next active task: **P10.3 — Supervised Engineer Pilot**.

## P10.3 — Supervised Engineer Pilot: ACTIVE

Preparation record: `docs/handoffs/P10.3-SUPERVISED-ENGINEER-PILOT-RECORD.md`.
Operational pilot: 4 / 4 PASS across A5, Plot Plan A0, Plot Plan A1, and INS-PD-141; all four pilot DWGs retained their exact pre-run SHA256 values and no operational runtime errors were recorded.

P10.3 remains ACTIVE. D1 Engineering Review text clipping is RESOLVED / runtime PASS. The pilot package is synchronized to accepted runtime DLL SHA256 `9203B9B392AA2EC95A89D9B44425FA9C58431D91A60FACF19DCFDF18CAAFAF5C`.
Engineering review remains PENDING. Next action: qualified engineer review of at least 3 representative pilot drawings.

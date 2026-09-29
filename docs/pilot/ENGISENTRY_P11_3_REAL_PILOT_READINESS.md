# EngiSentry — P11.3 Real Pilot Readiness

Status of the EngiSentry engineering-review capability as of repository HEAD
`03e25d5b2ef3d1887148798ccaec232b69a870cc`. This document is factual and stands alone; it is the
output of the P11.3A unattended readiness audit.

Scope of the product at this point: an AutoCAD 2021 (R24.0, x64, .NET Framework 4.8) plugin that
generates deterministic equipment-placement proposals (`ES_SUGGEST`), presents a bilingual
(English / فارسی) engineer review and resolution surface (`ES_ENGINEERREVIEW`, `ES_PLACEMENTRESOLVE`),
and records append-only, evidence-bound engineering decisions. It performs no engineering calculations of
its own beyond deterministic proposal generation, and it never fabricates engineering judgment.

## A. ALREADY PROVEN

- **P11.1 live AutoCAD 2021 acceptance** — closed / PASS. Existing evidence supports: bundle autoload,
  `ES_SUGGEST` single-proposal generation, proposal SHA/fingerprint binding, the real placement-resolution
  palette, CANCEL writes nothing, TEST_ONLY APPROVED/REJECTED recorded, a manual MODIFIED free-space pick,
  trusted FREE_SPACE resolution (EPR-1), immutable original proposal, DBMOD unchanged, drawing SHA
  unchanged, entity count unchanged, protected pilot unchanged, and TEST_ONLY cleanup/restoration to the
  canonical baseline (review=10, dispositions=2, resolutions=1).
- **P11.2 engineer review UI** — implemented and PASS.
- **P11.2B bilingual implementation** — implemented and PASS. English + Persian resources with exact key
  parity; live language switch; per-user preference persistence; Persian RTL and technical-value LTR.
- **Unattended software gates (this audit)** — Release x64 build, `--p11.2b`, `--p11.2-ui`, `--p11.1` and
  the default managed suite all PASS; no canonical token drift; no source-behavior change outside the
  approved P11.2B scope; no string-reversal or Persian-digit substitution.

## B. SOFTWARE MANUAL ACCEPTANCE REQUIRED

- **P11.2B bilingual visual acceptance** — PENDING. The current installed bundle does **not** match the
  P11.2B Release build (see the release candidate manifest), so the bilingual surface must be deployed and
  then visually accepted by a software operator. This is a software/UI check and does **not** require a
  project engineer. Procedure: `ENGISENTRY_P11_2B_BILINGUAL_VISUAL_ACCEPTANCE.md`.

## C. QUALIFIED PROJECT ENGINEER REQUIRED

- **Real project-engineer review acceptance** — PENDING. A qualified project engineer must perform a real
  governed review on representative equipment/proposal evidence and record an actual engineering decision
  through the review surface. Procedure: `ENGISENTRY_P11_3_REAL_ENGINEER_ACCEPTANCE_RUNBOOK.md`.
  Software/operator acceptance does **not** substitute for this.
- **Engineering-judgment sign-off** — PENDING. Any decision that constitutes engineering judgment must be
  made by the qualified project engineer; the software only captures it.

## D. REAL PROJECT AUTHORITY / PROJECT DATA REQUIRED

- **Real project authority bindings** — REQUIRED. Production supplies no authority binding, so the real path
  is fail-closed at 0 auto-executable rules. A real pilot that relies on project distance rules needs a
  governed deployment that supplies validated authority bindings (approval / currentness / trust).
- **Real project data / drawings** — REQUIRED. The pilot must run against real project drawings under the
  project's own governance, not the disposable test DWG.
- **Qualified project-engineer identity** — REQUIRED. The reviewer identity/role recorded must be a real
  qualified project engineer; `TEST_ONLY` identities are acceptance-only and are not pilot authorization.

## E. INSTALLER / RELEASE CLOSEOUT REQUIRED

- **Deployment of the P11.2B build to the installed bundle** — REQUIRED (deployment is a separate task; it
  was not performed by this audit).
- **Final installer / release closeout** — PENDING. Package/manifest finalization and release acceptance
  remain outstanding.
- **Versioning** — note: assembly/file version is `1.3.0.0` (`1.3.0-rc1` informational); bundle AppVersion
  is `1.3.0`.

## F. OPTIONAL POLISH

- Pin the prominent equipment-tag label (and header sub-line) explicitly to LTR for strict consistency with
  the other technical-value controls (currently the value renders correctly via bidi; only alignment
  follows the mirrored layout). Non-blocking.
- Localize the two stale-proposal exception details appended to an otherwise localized disposition error.
  Non-blocking.
- Translation wording refinement (Persian prose quality). Non-blocking.

## Not production-complete

This document does **not** assert the product is production-complete. Engineering judgment, real project
authority/data, and release closeout remain outstanding.

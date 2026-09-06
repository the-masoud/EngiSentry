# EngiSentry Plot Plan v1.1 — Standards Authority Matrix

Status: DESIGN BASELINE
Date: 2026-09-05
Scope: Plot Plan spacing, layout, flare siting, occupied-building siting, access and engineering review.

## Non-negotiable engineering policy

1. Deterministic geometry may measure; standards context decides what a measurement means.
2. Spacing/distancing is not assumed to be a universal numeric PASS/FAIL problem.
3. A rule is auto-enforceable only when applicability, source authority, units, reference points and project overrides are unambiguous.
4. Conflicting, superseded, conditional or risk-based requirements produce `EXPERT_REVIEW_REQUIRED`, not an invented answer.
5. Every standards finding must preserve source, edition/revision, clause/table reference, applicability basis and rule authority.
6. Project/client/company requirements can be stricter than general references, but cannot silently override a newer governing requirement without an explicit approved project decision.
7. Flare-location clauses from `PCD-PD-135` are quarantined because the document provider explicitly warned that major errors exist in those terms.
8. `IPS E-PR-190` is treated as legacy/conflict-reference because the document provider explicitly warned that it is outdated and conflicts materially with `IPS G-SF-150`.

## Authority classes

| Class | Meaning | Default engine action |
|---|---|---|
| GOVERNING_PROJECT | Approved project/client requirement | Enforce only when applicability is explicit |
| ACTIVE_PRIMARY | Current authoritative industry/national reference | Enforce deterministic clauses; review conditional clauses |
| ACTIVE_SUPPORTING | Current supporting reference | Context/cross-check; no silent override |
| ENGINEERING_GUIDANCE | Risk/siting guidance | Advisory or expert review |
| LEGACY_CONFLICT | Older/conflicting reference | Never auto-enforce |
| QUARANTINED | Known reliability concern | Evidence only; expert review |

## Source authority matrix

| Source | Verified edition/status | Authority | Plot Plan use | Auto-enforce? |
|---|---|---|---|---|
| Project/Client approved criteria | Per controlled project issue | GOVERNING_PROJECT | Project-specific layout and spacing | YES, only explicit deterministic clauses |
| IPS-G-SF-150(1):2023 / INSO 23591 | Project-provided 2023/1402 copy | ACTIVE_PRIMARY (Iran baseline) | General spacing/layout | CONDITIONAL; controlled-copy status must be confirmed |
| API Std 521 | 7th Ed., 2020; 8th Ed. currently in editing | ACTIVE_PRIMARY | Pressure relief/depressuring; flare consequence/siting context | NO fixed-distance inference; calculation/risk context required |
| API Std 537 | 4th Ed., 2024 + A1 (2025) + A2 (2026); 5th Ed. in development | ACTIVE_PRIMARY | Flare system/details; used with API 521 | NO stand-alone siting verdict without required inputs |
| ISO 23251:2019 | Ed.2; ISO confirmed current in 2024 | ACTIVE_SUPPORTING | Pressure-relieving/depressuring cross-check | CONDITIONAL; it supplements API 521 6th Ed. (2014) |
| EI Model Code Part 15 | 5th Ed., Oct 2024 | ACTIVE_SUPPORTING | Hazardous area / ignition-source location | CONDITIONAL on hazardous-area inputs |
| CCPS Guidelines for Siting and Layout of Facilities | 2nd Ed., Mar 2018 | ENGINEERING_GUIDANCE | Risk-based siting/layout and separation philosophy | ADVISORY / EXPERT REVIEW |
| API RP 752 | 4th Ed., Jan 2024 | ACTIVE_PRIMARY when permanent buildings are in scope | Permanent occupied-building siting | CONDITIONAL / risk evaluation |
| API RP 753 | 2nd Ed., Jan 2024 | ACTIVE_PRIMARY when portable buildings are in scope | Portable-building siting | CONDITIONAL / risk evaluation |
| API RP 756 | 2nd Ed., Jan 2024 | ACTIVE_PRIMARY when tents are in scope | Tent siting | CONDITIONAL / risk evaluation |
| PCD-PD-135 Rev.1 | Company design criteria; local controlled document | GOVERNING_PROJECT except flagged clauses | General project criteria | CONDITIONAL |
| PCD-PD-135 flare-location terms | Provider warned of major mistakes | QUARANTINED | Flare location | NEVER |
| IPS E-PR-190(1), Mar 2009 | Legacy local reference; provider warned of major conflicts | LEGACY_CONFLICT | Historical comparison only | NEVER |

## Precedence decision model

`Approved Project/Client Requirement -> Applicable Current Governing Standard -> Current Supporting Standard -> Engineering Guidance -> Legacy/Quarantined evidence`

Precedence is not a blind numeric rank. If two applicable requirements conflict, EngiSentry records both and returns `STANDARD_CONFLICT / EXPERT_REVIEW_REQUIRED` unless an approved project decision resolves the conflict.

## Rule decision states

| State | Meaning |
|---|---|
| PASS | Deterministic requirement satisfied with sufficient evidence |
| FAIL | Deterministic requirement violated with sufficient evidence |
| ADVISORY | Guidance/recommendation is relevant but not a deterministic project requirement |
| EXPERT_REVIEW_REQUIRED | Engineering judgment or missing process/risk context is required |
| STANDARD_CONFLICT | Two applicable controlled sources conflict |
| NOT_APPLICABLE | Applicability predicate is false |
| INSUFFICIENT_EVIDENCE | Drawing/project data cannot prove the requirement |
| SOURCE_QUARANTINED | Rule comes from a source/term that must not drive a verdict |

## Initial Plot Plan v1.1 rule-family policy

| Rule family | Default result model | Notes |
|---|---|---|
| Equipment tag/list consistency | Deterministic PASS/FAIL/REVIEW | No standards ambiguity; evidence-driven |
| Coordinate parsing/placement | Deterministic PASS/FAIL/REVIEW | Must avoid equipment-tag/coordinate collisions |
| Road and access geometry | Deterministic where project/current source is explicit | Otherwise advisory/review |
| Fence / boundary clearance | Deterministic only with explicit applicable source | Preserve reference-point definition |
| Equipment-to-equipment spacing | Mixed | Equipment/service/category applicability required |
| Maintenance clearance | Usually EXPERT_REVIEW_REQUIRED | May become deterministic for explicit equipment-specific clauses |
| Fire access | Mixed | Geometry can be deterministic; operational sufficiency may require review |
| Occupied-building siting | Risk-based / EXPERT_REVIEW_REQUIRED by default | Use API RP 752 family as applicable |
| Flare siting | EXPERT_REVIEW_REQUIRED | No hard-coded universal 50 m / 90 m rule |
| Hazardous-area / ignition-source relationship | CONDITIONAL / REVIEW | Requires classification inputs; EI 15 supporting source |
| Revision comparison | Deterministic evidence comparison | Not a standards verdict |

## Flare-specific guardrail

EngiSentry may detect the flare, measure distances, identify nearby receptors/equipment, and display applicable source context. It must not issue a flare-location PASS/FAIL from the preliminary distance language in PCD-PD-135. A flare siting verdict requires the approved project basis and the relevant process/safety inputs (for example radiation/dispersion or other consequence-analysis outputs as applicable).

## External edition verification (checked 2026-09-05)

- API Standards Plan: API 521 7th Ed. published 2020-06-02; 8th Ed. is in editing.
- API Standards Plan: API 537 4th Ed. published 2024-11-15; Addendum 1 published 2025-01-23; Addendum 2 published 2026-06-22; 5th Ed. is in development.
- ISO official catalogue: ISO 23251:2019 Ed.2 remains published/current and was confirmed in 2024; ISO states it supplements API 521 6th Ed. (2014).
- Energy Institute: EI Model Code Part 15, 5th Ed., published October 2024.
- AIChE/CCPS: Guidelines for Siting and Layout of Facilities, 2nd Ed., published March 2018.
- API: RP 752 4th Ed., RP 753 2nd Ed., RP 756 2nd Ed., all published January 2024.

Official/reference pages:
- https://www.api.org/products-and-services/standards/standards-plan
- https://www.api.org/products-and-services/standards/important-standards-announcements/standard521
- https://www.iso.org/standard/75144.html
- https://www.energyinst.org/technical/publications/topics/asset-integrity/ei-model-code-of-safe-practice-part-15-area-classification-for-installations-handling-flammable-fluids
- https://ccps.aiche.org/publications/books/guidelines-siting-and-layout-facilities-2nd-edition
- https://www.api.org/news-policy-and-issues/news/2024/01/16/api-releases-new-updates-to-standards-for-hazard-management-in-process-plant-str

## Implementation rule

No numeric standard requirement enters production code merely because it appears in a PDF. It must first be represented as a reviewed rule record with applicability, source edition, clause/table identifier, units, measurement reference points, authority state, conflict state, and whether expert approval is required.

# EngiSentry — P11.3 Real Project Engineer Acceptance Runbook

Audience: a **qualified project engineer** performing the first real pilot review, assisted by a software
operator. This runbook describes **workflow only**. It does not prescribe any engineering standard, clearance
value, or project authority, and it does not tell the engineer what decision to make.

The engineer makes the engineering judgment. The software presents evidence, captures the decision, and
records it. The software never infers, fabricates, or substitutes engineering judgment.

## Roles

### SOFTWARE OPERATOR ACTIONS
- Ensure the P11.2B (or later) build is deployed and the surface opens.
- Open the relevant project drawing under the project's governance (not the disposable test DWG).
- Run the commands: `ES_SUGGEST` (generate the proposal), then `ES_PLACEMENTRESOLVE` (open the review and
  resolution surface).
- If a free-space pick is required (a MODIFIED decision), perform the pick when the engineer directs it.
- Record the engineer's identity and role exactly as supplied. Do **not** type a placeholder or `TEST_ONLY`
  identity for a real pilot.
- Do not press "Record Decision" until the engineer has given the decision.

### PROJECT ENGINEER DECISIONS
- Review the presented evidence and answer the review questions.
- Choose the Classification and the Disposition.
- Supply the reason/comment and the reviewer identity/role.
- Decide whether the evidence is sufficient; if not, record the appropriate classification/disposition
  rather than approving.

## What evidence is presented

For the selected equipment/proposal the surface shows: the equipment tag, the review/proposal status, the
original location, the proposed location, whether review is required, the captured pick evidence, and — under
"Technical Details" (collapsed by default) — the drawing SHA256, full Proposal ID, proposal fingerprint,
generator identifier, Resolution ID, Disposition Record ID, handles, layer and coordinates. Technical values
are shown left-to-right and are selectable/copyable. No evidence is hidden; the collapsed section only
defers it.

## What equipment/proposal is being reviewed

A single deterministic placement proposal for one equipment tag (e.g. `T-1001`), bound to the current
drawing's SHA256. The proposal is immutable: its ID, fingerprint, original point and proposed point do not
change during review. The proposal is identified by its Proposal ID and its fingerprint.

## What the review questions mean

Each question is a bounded confirmation the engineer answers:
- **Picked point is the equipment location** — does the captured point represent the equipment's location?
- **Easting/Northing axes are correct** — are the coordinate axes interpreted correctly for this point?
- **Drawing units confirmed for this coordinate** — are the drawing's insertion units correct for this
  coordinate?

Answers are Yes/No (بله/خیر). The stored answer vocabulary is fixed; the UI only displays it in the selected
language.

## What Classification records

Classification records the engineer's assessment **category** of the reviewed item. The available values are
`PASS`, `USABILITY_ISSUE`, `SUSPECTED_PRODUCT_DEFECT`, `DRAWING_EVIDENCE_LIMITATION`,
`ENGINEERING_REVIEW_REQUIRED`. The classification is a category label; it does not by itself grant authority.

## What Disposition records

Disposition records the engineer's **decision about the proposal**:
- `APPROVED` — the proposal is accepted as presented.
- `REJECTED` — the proposal is not accepted.
- `MODIFIED` — the engineer accepts a corrected placement; a new pick is captured and bound.
- `CANCELLED` — the proposal is withdrawn.
`PENDING_REVIEW` is the initial state before a decision. `SUPERSEDED` marks a prior decision replaced by a
later one.

## What APPROVED / REJECTED / MODIFIED mean in workflow terms

- **APPROVED** records the engineer's acceptance of the proposal's proposed placement. It creates an
  evidence-bound disposition record and a trusted resolution bound to it.
- **REJECTED** records the engineer's non-acceptance. No trusted placement resolution is created for the
  proposal.
- **MODIFIED** records the engineer's acceptance of a *different* placement. The engineer directs a pick; the
  captured free-space point (or entity) is recorded as the trusted resolution and bound to the disposition.
All three are append-only governance records; a later decision supersedes, never rewrites, an earlier one.

## How missing / incomplete evidence is handled

If a required question is unanswered, the reviewer name/role is empty, the classification is unset, the
required pick is missing, the comment is empty, or the proposal is out of the supported coordinate range, the
primary action stays disabled and an inline message states the first missing requirement. Nothing is written
until all required evidence is present. No modal dialogs are used for routine validation.

## What evidence is persisted

On an explicit "Record Decision": a proposal-disposition record (schema PPD-1) capturing the disposition,
classification, reason/comment, engineer identity/role, the proposal ID and fingerprint, the bound review
request/response IDs, and UTC timestamps; and, for APPROVED/MODIFIED, an equipment-placement resolution
record (schema EPR-1) bound to the disposition. Records are append-only. The UI language preference is
**not** part of these records.

## What engineer identity/role is recorded

The engineer's actual display name and role/title, exactly as supplied. `TEST_ONLY` identities are for
controlled acceptance only and must never be used for a real pilot.

## Boundary

This runbook does not define engineering standards, clearances, or project authority, and it does not direct
the engineer's decision. Where engineering judgment is required, the qualified project engineer decides.

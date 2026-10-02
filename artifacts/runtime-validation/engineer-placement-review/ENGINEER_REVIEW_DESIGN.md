# Engineer placement candidate review design (unaccepted)

Baseline: `65f03e7edf51c9126f92aab300638bb34653182e`, `master`. The accepted shared-axis resolver was not changed.

The implementation in the working tree adds exact-occurrence ACCEPT/REJECT buttons to `EquipmentPlacementResolutionControl` and a command-line form, `ES_PLACEMENTCANDIDATE`. The control shows candidate coordinates, basis, evidence type and summary, handles, shared axis, provisional Road/Fence clearances, and current decision. No bulk action was added.

`EquipmentPlacementCandidateDecisionService` writes append-only decisions beneath the existing external equipment-placement-resolution root and binds each decision to a persisted `ProjectEngineerReviewService` request/response. The decision includes drawing SHA256, exact occurrence ID and tag handle, a fingerprint of the current candidate evidence, reviewer identity and UTC time. `PlotPlanBuilder` checks the current candidate against that binding after inference. ACCEPT makes only that occurrence Complete and eligible for governed measurement. REJECT leaves it UNRESOLVED. Existing authoritative exact-occurrence coordinates are applied first and retain precedence. No DWG writes are present in the decision path.

Status: **UNACCEPTED**. The full regression suite stopped at the existing UI action-label contract. The permitted build correction/rerun was already used, so no real-pilot decision test or commit followed.

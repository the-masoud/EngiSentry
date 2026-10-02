# Shared-axis placement acceptance (2026-10-02)

Baseline: `2bdc1577183bf37f774f6048c6bbcd352c967535`. AutoCAD 2021 x64, fresh byte-identical disposable copy of protected `1003-00-EB-PI-PLP-0503-A1.dwg`; one Core Console run. Source report: `artifacts/qa/plotplan/1003-00-EB-PI-PLP-0503-A1-shared-axis-accept-4cae73b2.dwg.20261002T192046202Z.plotplan.qa.json`.

Build: one initial run failed on a test fixture property (`FenceLabels`); one direct correction and rerun passed. Existing System.Memory MSB3277 and nullable warnings remain. Focused placement tests passed; full regression passed, including P14 and P14.1. Reverse-enumeration shared-axis test passed.

Pilot: 25 exact occurrences, 0 RESOLVED, 6 REVIEW_REQUIRED, 19 UNRESOLVED. All six review suggestions carry candidate X/Y, X/Y handles and an explicit structural evidence description. Four use a shared X, two a shared Y. Each accepted occurrence has one group assignment; no competing group was selected. 0 normal spatial measurements; 492 provisional measurements (486 Road, 6 SecurityFence). No inferred placement enters governed PASS/FAIL.

| Tag (handle) | X | Y | Shared | X handle | Y handle | Minimum provisional Road mm | Minimum provisional Fence mm |
| --- | ---: | ---: | --- | --- | --- | ---: | ---: |
| P-01A (57D2CC) | 5581750 | 5319500 | X | 57D2C1 | 57D2CB | 23367.92 | 31957.76 |
| P-01B (57D2CE) | 5581750 | 5317500 | X | 57D2C1 | 57D2CD | 22290.35 | 33951.86 |
| P-02A (57D14C) | 5636500 | 5287395 | X | 57D159 | 57D14B | 12790.05 | 21993.18 |
| P-02B (57D14E) | 5636500 | 5285395 | X | 57D159 | 57D14D | 13905.99 | 23119.50 |
| P-05A (57D30C) | 5587800 | 5307900 | Y | 57D30B | 57D306 | 13273.11 | 42684.70 |
| P-05B (57D30E) | 5589800 | 5307900 | Y | 57D30D | 57D306 | 11292.48 | 42183.71 |

Shared-axis evidence class: paired EQ-TAG occurrences aligned in the drawing, distinct EQ-ID annotations of the orthogonal axis with matching tag-to-label displacement and coordinate step, and exactly one opposite-axis EQ-ID annotation at the bounded group periphery. This is inferred annotation structure, not authored ownership or authoritative coordinates. Engineer review remains required.

Unresolved reasons: 15 lack a unique structured graph; 4 are combined/non-EQ-TAG annotations. P-03/P-04 duplicate and combined occurrences and TK duplicates were not collapsed. P-06 has two Y candidates per occurrence and no X candidate. No proximity-only assignment was made.

Integrity: Core Console exit 0, DBMOD 0 before/after, `_Y` discard, NORMAL_NO_SAVE. Disposable and protected SHA256 before/after each `7E51EC0BDCE424DEFA46AFC5B63EA63ED4CBEB19A7785A79E0D198DC972BD807`. Evidence: `shared-axis-console.log`, `shared-axis-sha-before.txt`, `shared-axis-sha-after.txt`, `shared-axis-full-tests.log`, `shared-axis-build.log`, and `shared-axis-build-rerun.log` in this directory. Historical prior failed acceptance evidence remains unchanged.

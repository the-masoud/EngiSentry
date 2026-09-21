# EngiSentry Phase J-B — Jev Calibration Benchmark

Baseline HEAD: `e441013a05d6a3818bfe70a34b45cc8bac526f65`.

Calibration packages are rooted at `%LOCALAPPDATA%\EngiSentry\artifacts\qa\jev-calibration\<CalibrationPackageId>`. The package id is SHA-256 over schema version, sanitized drawing name, and ordinally ordered evidence-id/projection-hash pairs. It contains no timestamp, machine, identity, path, or credentials.

Generated package files are `calibration-cases.json`, `engineer-labels.template.json`, and `LABELING_INSTRUCTIONS.md`; each uses create-new or identity-validated reuse. `engineer-labels.json` is never created or overwritten. Its schema, package id, evidence ids, projection hashes, duplicate rows, labels, and bounded comments are validated locally. Labels and comments are never sent to TypeSafe.

J-B reuses the J-A sanitizer, typed Choice labels, client, classifier, and Core Console boundary. `ES_JEVCALIBRATE` creates/reuses a package and presents `AWAITING_ENGINEER_LABELS` when no valid labels exist. The Core Console path returns `JEV_DISABLED` before scan, network, GUI, or artifact mutation.

JEV REMAINS ADVISORY ONLY.

ENGINEER LABELS ARE NEVER SENT TO TYPESAFE.

NO PRODUCTION CONFIDENCE THRESHOLD WAS SELECTED.

NO DETERMINISTIC SEMANTIC STATE WAS CHANGED.

NO STANDARDS AUTHORITY WAS CHANGED.

NO GOVERNANCE STATE WAS CHANGED.

NO RULE WAS ACTIVATED.

NO PLACEMENT PROPOSAL WAS CHANGED.

NO DWG WAS MODIFIED.

Focused deterministic package tests passed. Release x64 `/warnaserror` build passed with 0 warnings and 0 errors. Full regression passed with `All EngiSentry M2.2 through M5.0 tests passed.`

Generated package reuse now deserializes and compares deterministic logical content; identity text alone is not accepted. The host-independent benchmark contracts and runner provide fixed-label confusion matrix rows, confidence buckets, observational threshold rows, per-label summaries, nullable zero-denominator rates, and immutable benchmark-report reuse keyed by package and input hashes.

Release x64 `/warnaserror` passed with 0 warnings and 0 errors. Full regression passed with `All EngiSentry M2.2 through M5.0 tests passed.` No engineer-label file was created and no real benchmark was executed during software-only closure; interactive runtime acceptance is recorded below.

For validated local labels, `ES_JEVCALIBRATE` hashes the actual package input files and checks an existing immutable benchmark report before transport creation. A matching report is reused without a TypeSafe call; changed inputs fail closed. With no report and enabled/configured J-A policy, only labeled ambiguity cases are submitted through the existing J-A client/classifier as one batch, then projected into an immutable advisory report. Report model is populated only from one actual classified response model; conflicting models fail closed.

Final software closure adds a host-independent execution coordinator for report reuse, policy gating, the optional single classifier call, and report creation. The AutoCAD command uses this coordinator, has no terminal `LABELS_READY` state, and verifies DBMOD before every post-scan return. Focused J-B validation passed 69 assertions using temporary directories, synthetic inputs, fake decision clients, and a fake HTTP handler; no live TypeSafe request was made. The final Release x64 `/warnaserror` build passed with 0 warnings and 0 errors, and the full regression final line was `All EngiSentry M2.2 through M5.0 tests passed.`

Runtime status: PASS.

## Interactive AutoCAD 2021 acceptance

`ES_JEVCALIBRATE` was run once on the accepted disposable Phase E drawing in interactive AutoCAD/Civil 3D 2021. The command found 2 ambiguous semantic cases and created the deterministic calibration package:

`EC0860837579DAA03C847959254E908C542433D0C486D24AB256A60E371F93AD`

Runtime package root:

`%LOCALAPPDATA%\EngiSentry\artifacts\qa\jev-calibration\EC0860837579DAA03C847959254E908C542433D0C486D24AB256A60E371F93AD`

Created files:

- `calibration-cases.json`
- `engineer-labels.template.json`
- `LABELING_INSTRUCTIONS.md`

The command did not create `engineer-labels.json` and did not create `benchmark-report.json`. Because there were zero engineer-labeled rows, the runtime stopped at `AWAITING_ENGINEER_LABELS` and made zero TypeSafe requests.

The existing EngiSentry Results palette showed `ES_JEVCALIBRATE`, `JEV CALIBRATION`, `AWAITING_ENGINEER_LABELS`, `ADVISORY ONLY`, `NO PRODUCTION THRESHOLD`, and `READ-ONLY`, with 2 ambiguous cases and 0 labeled cases.

DBMOD remained unchanged: the palette is reached only after the accepted J-B DBMOD guard passes. The disposable drawing retained SHA-256 `3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4` and mtime `2026-09-20T23:12:29.0437840+03:30`. The protected source drawing retained the same accepted SHA-256 and mtime `2025-01-27T16:51:43.0000000+03:30`.

J-B acceptance therefore closes the calibration-package/export workflow and the host-independent benchmark/reuse engine. No real engineer-labeled benchmark has yet been executed; that requires an operator-created `engineer-labels.json` and is intentionally outside this acceptance run.

# EngiSentry Phase J-A — Jev Advisory Semantics

Baseline HEAD: `b09914a9a878d4f2bff765abb1b68dcfe6dc1da9`.

Status: **PASS_CANDIDATE** — implementation, focused tests, Release regression, live Jev Choice transport, and interactive AutoCAD 2021 acceptance passed in advisory-only mode.

## TypeSafe integration

The installed `typesafe-ai` skill and the current official TypeSafe API and Choice documentation were consulted. The implementation uses a small .NET Framework 4.8 `HttpClient` transport to the documented HTTPS System One endpoint, with the documented Bearer authorization header and the `jev-latest` model selector. No SDK, Node.js, Python, daemon, server, or configurable hostname was introduced.

The only secret source is `TYPESAFE_API_KEY`. The only opt-in source is `ENGISENTRY_JEV_ENABLED`; a request is possible only when it equals `1` and the key is non-empty. Keys, authorization headers, and HTTP traces are neither serialized nor displayed.

The outbound whitelist is: anonymous stable evidence id, geometry kind, optional layer name, current deterministic state, existing candidate kinds, bounded nearby text fragments, bounded local spatial descriptors, and existing measurement-basis hints. Excluded categories include DWG bytes, drawing names and paths, project/customer/company identity, title data, standards content, approvals, policy/trust data, identities, credentials, and HTTP headers.

The exact typed Choice labels are `ACCESS_ROAD_EDGE`, `SECURITY_FENCE`, `UNIT_BOUNDARY`, `UNRELATED`, and `UNKNOWN`. The client validates the complete probability distribution and confidence bounds before recording a result. There is no confidence threshold and no automatic resolution.

## Shadow-mode outcome

`JEV IS ADVISORY ONLY.`

`JEV DID NOT CHANGE DETERMINISTIC SEMANTICS.`

`JEV DID NOT CHANGE STANDARDS AUTHORITY.`

`JEV DID NOT CHANGE GOVERNANCE.`

`JEV DID NOT ACTIVATE ANY RULE.`

`JEV DID NOT CREATE OR MODIFY A PLACEMENT PROPOSAL.`

`JEV DID NOT MODIFY A DWG.`

The isolated `EngiSentry.Intelligence` assembly is referenced only by the AutoCAD host and test project. `ES_JEVASSIST` performs a read-only scan, derives only existing ambiguous Road/Boundary evidence, writes an advisory sidecar with every mutation flag false, verifies DBMOD unchanged, and projects the advisory fields in the existing Visual Results Palette. Existing commands remain unchanged.

## Validation

Focused J-A fake-client coverage passed without any live TypeSafe requests. The Release x64 `/warnaserror` rebuild succeeded with 0 warnings and 0 errors. The Release regression executable ended with `All EngiSentry M2.2 through M5.0 tests passed.`

The first interactive runtime exposed one concrete J-A transport defect: `DataContractJsonSerializer` serialized dictionary maps in its default array form, and the official TypeSafe endpoint rejected that payload with `HTTP 422`. This was corrected by enabling `UseSimpleDictionaryFormat = true` for both request and response transport serialization. A focused fake-HTTP regression now verifies that `state`, `questions`, `criteria`, and returned `answers` are JSON objects.

The J-A correction also blocks `ES_JEVASSIST` before scan, transport construction, sidecar creation, or palette display when `AutoCADHostBoundary.IsCoreConsole()` is true. That boundary returns `JEV_DISABLED`, performs zero HTTP requests, and preserves read-only behavior.

Safe result error categories are retained without exception text, bodies, headers, or credentials: `network`, `timeout`, `http-401`, `http-403`, `http-422`, `http-429`, `http-529`, `http-5xx`, and `invalid-response`. Transport failures are `JEV_UNAVAILABLE`; invalid typed responses are `INVALID_RESPONSE`.

After the transport correction, the Release x64 `/warnaserror` rebuild passed with 0 warnings and 0 errors and the Release regression executable again ended with `All EngiSentry M2.2 through M5.0 tests passed.`

Interactive AutoCAD 2021 runtime acceptance for `ES_JEVASSIST`: **PASS** on the disposable Phase E drawing with `ENGISENTRY_JEV_ENABLED=1` and the user-scoped TypeSafe key inherited into the newly launched AutoCAD process.

Two existing ambiguous semantic cases were sent in one live Choice request. TypeSafe returned model `jev-1.13.0` and both results remained advisory-only:

- case 1: `ACCESS_ROAD_EDGE`, confidence `0.81`, probabilities `ACCESS_ROAD_EDGE=0.85`, `UNKNOWN=0.15`, all other labels `0`
- case 2: `ACCESS_ROAD_EDGE`, confidence `0.49`, probabilities `ACCESS_ROAD_EDGE=0.60`, `UNKNOWN=0.38`, `UNRELATED=0.02`, all other labels `0`

The Visual Results Palette showed `ES_JEVASSIST`, `JEV SEMANTIC ASSIST`, `CLASSIFIED`, `ADVISORY ONLY`, `NO ENGINEERING DECISION`, and `READ-ONLY`. The sidecar mutation flags remained false for deterministic model, governance, rule execution, placement proposal, and DWG.

Because the command reached sidecar creation and the result palette after both DBMOD guards, DBMOD remained unchanged. The disposable runtime DWG retained SHA-256 `3D5BCC2D521A35B7AC1DB7EA29850EB5BEC230B2FBEFCE3F8485E99781F19DC4` and mtime `2026-09-20T23:12:29.0437840+03:30`; the protected source DWG retained the same accepted SHA-256 and remained unchanged.

Known limitations: J-A deliberately has no calibration, no engineer-labelled benchmark, no authority role, and no automatic semantic resolution. The observed 0.49 confidence result is therefore still recorded only as advisory evidence and has no deterministic effect.

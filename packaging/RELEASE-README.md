# EngiSentry v1.2.0

Supported environment:

- Windows x64
- AutoCAD 2021 x64

## Installation

Copy `EngiSentry.bundle` to `%APPDATA%\Autodesk\ApplicationPlugins\EngiSentry.bundle`, then start AutoCAD 2021. The packaged release does not require `NETLOAD`.

## Commands

`ENGISENTRY`, `ES_HELLO`, `ES_SCAN`, `ES_SEMANTIC`, `ES_INDEX`, `ES_QA`, `ES_PLOTQA`, `ES_PROFILEQA`, `ES_CROSSQA`, `ES_REVBASELINE`, `ES_REVISION`, and `ES_DISTANCESETTINGS` are available.

`ES_DISTANCESETTINGS` edits Project Distance Criteria for the saved drawing's project scope. Current schema-2 criteria are Road, Plant, and Unit Boundary; defaults are 3 m, 30 m, and 15 m. Saved project values are the numerical authority for that project and are stored outside the DWG.

Development/repository execution writes reports under `<repo>\artifacts\...`. Installed packaged execution writes reports under `%LOCALAPPDATA%\EngiSentry\artifacts\...`.

## Validated v1.2.0 scope

P1 hardens real-world semantic extraction and grouped equipment tags. P2 adds native XLSX Equipment Register support and deterministic cross-document matching. P3 adds standards source, clause, and rule provenance infrastructure. P4 adds unit-safe project engineering-distance criteria and deterministic evidence-backed geometry qualification. P5 adds standards authority classification, resolution, execution gating, engineering-review projection, and an accepted real PCD/IPS authority catalog.

P5 authority and review features are accepted library capability; they are not currently wired as automatic ES_PLOTQA regulatory PASS/FAIL decisions.

## Limitations

EngiSentry is read-only for analyzed DWGs: it does not save or auto-fix drawings. It does not claim regulatory or safety certification, infer unsupported engineering distances, turn quarantined PCD flare values into automatic rules, infer `STANDARD_CONFLICT` without explicit conflict evidence, or provide Plant 3D or AI auto-design capability.

## Uninstallation

Close AutoCAD and delete `%APPDATA%\Autodesk\ApplicationPlugins\EngiSentry.bundle`.

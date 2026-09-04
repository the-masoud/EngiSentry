# EngiSentry v1.0.0

Supported environment:

- Windows x64
- AutoCAD 2021 x64

## Installation

Copy `EngiSentry.bundle` to:

`%APPDATA%\Autodesk\ApplicationPlugins\EngiSentry.bundle`

Then start AutoCAD 2021. The packaged release does not require `NETLOAD`.

If AutoCAD displays its normal security prompt, review and approve it normally.

## Commands

`ENGISENTRY`, `ES_HELLO`, `ES_SCAN`, `ES_SEMANTIC`, `ES_INDEX`, `ES_QA`, `ES_PLOTQA`, `ES_PROFILEQA`, `ES_CROSSQA`, `ES_REVBASELINE`, and `ES_REVISION` are available.

`ES_PROFILEQA` accepts an Engineering Standard Profile JSON file. `ES_CROSSQA` accepts an External Equipment Register CSV file. `ES_REVISION` accepts an EngiSentry revision baseline JSON file. `ES_REVBASELINE` creates a revision baseline JSON file.

Generated reports use EngiSentry's repository/artifact output structure.

## Validated v1.0 scope

EngiSentry provides CAD evidence extraction, engineering semantic extraction, deterministic QA, Plot Plan coordinate and geometry association, explicit coordinate-to-geometry validation, spatial measurements, profile-driven clearance rules, external Equipment Register cross-document QA, deterministic revision comparison, traceable evidence, and read-only execution.

EngiSentry does not modify or save the analyzed DWG.

This release does not claim regulatory certification, guaranteed correctness, safety certification, P&ID or Plant 3D support, AI Copilot, auto-fix, graphical redlines, or enterprise batch processing.

## Uninstallation

Close AutoCAD and delete the `EngiSentry.bundle` directory from `%APPDATA%\Autodesk\ApplicationPlugins\EngiSentry.bundle`.

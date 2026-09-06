# EngiSentry v1.1.0

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

`ES_PROFILEQA` resolves the active standards profile automatically and falls back to BuiltIn rules when no external profile is installed. `ES_CROSSQA` resolves the active Equipment Register automatically. `ES_REVBASELINE` creates and activates a revision baseline; `ES_REVISION` uses the active baseline automatically.

Visual Results are shown inside AutoCAD for all commands. QA findings can be selected and, when source evidence is available, `Zoom To Evidence` centers the drawing on the related object.

Development/repository execution writes reports under `<repo>\artifacts\...`.

Installed packaged execution writes reports under `%LOCALAPPDATA%\EngiSentry\artifacts\...`.

## Validated v1.1.0 scope

EngiSentry provides CAD evidence extraction, engineering semantic extraction, deterministic QA, Plot Plan coordinate and geometry association, explicit coordinate-to-geometry validation, spatial measurements, profile-driven clearance rules, external Equipment Register cross-document QA, deterministic revision comparison, traceable evidence, and read-only execution.

Version 1.1.0 hardens visible nested Model-space evidence extraction, excludes Paper-space Road/Boundary legend evidence, adds deterministic fail-closed Road/Boundary geometry association, strengthens revision baseline trust, scopes CrossQA registers to the active drawing/project, and improves Results evidence navigation.

EngiSentry does not modify or save the analyzed DWG.

This release does not claim regulatory certification, guaranteed correctness, safety certification, P&ID or Plant 3D support, AI Copilot, auto-fix, graphical redlines, or enterprise batch processing.

## Uninstallation

Close AutoCAD and delete the `EngiSentry.bundle` directory from `%APPDATA%\Autodesk\ApplicationPlugins\EngiSentry.bundle`.

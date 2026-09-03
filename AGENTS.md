# EngiSentry contribution guardrails

- Target AutoCAD 2021 x64 only.
- AutoCAD plugin targets .NET Framework 4.8.
- Never reference Autodesk assemblies from AutoCAD 2022+.
- Autodesk managed references must have Copy Local = false.
- M0.1 must not modify any DWG.
- No AI.
- No cloud.
- No database.
- No Plant 3D.
- No auto-fix.
- No unnecessary dependencies.
- Do not modify user sample drawings.
- Keep architecture ready for a future .NET 10 engine separated from the AutoCAD process.

The M0.1 plugin is intentionally isolated in `src/EngiSentry.AutoCAD2021`.

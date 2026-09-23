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

## Human engineering judgment protocol

- Never fabricate, infer, or substitute project-engineer judgment, engineering sign-off, or engineering approval.
- When engineering judgment is required, present a dedicated human-review UI containing the relevant evidence and precise bounded questions.
- Capture the actual engineer response, reviewer identity, UTC timestamp, reviewed item identity, and evidence binding.
- Apply the response only through existing EngiSentry review/governance semantics.
- Human review must never create or imply production approval, authority policy, trusted-issuer status, private-key authority, or automatic rule activation.
- If no engineer response exists, preserve the pending/review-required state and continue unrelated automatable work.
- Reuse existing review, evidence, artifact, and governance components; do not create a parallel authority system.

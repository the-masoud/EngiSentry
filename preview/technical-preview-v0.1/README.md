# EngiSentry — Technical Preview v0.1

This is a self-contained, local management preview. No web server, build step, external CDN, AutoCAD connection, database, cloud service, or AI service is required.

## Open

Open `index.html` directly in a modern desktop browser (double-click it, or use **Open with** Chrome, Edge, or Firefox). The preview loads `data.js` locally, so it also works from a `file:///` URL.

## Validated source artifact

The preview data was generated from this exact validated M2.2 Plot Plan QA artifact:

`C:\Users\M.Zeynali\Documents\EngiSentry\artifacts\qa\plotplan\m22-manual-acceptance.dwg.20260904T100824032Z.plotplan.qa.json`

`data.js` intentionally contains only management-preview data: report and Plot Plan statistics, the 15 rule summaries, compact findings, and the nine compact M2.2 spatial constraints. It does **not** contain the full `PlotPlan.Geometry.Primitives` collection or any DWG data.

## Regeneration provenance

`build_preview_data.py` is the standard-library-only extraction used to derive `data.js` from the exact artifact above. Running the preview itself requires no Python or other tooling.

The preview is presentation-only. It does not change EngiSentry engineering logic, load a DWG, invoke AutoCAD, or modify any drawing.

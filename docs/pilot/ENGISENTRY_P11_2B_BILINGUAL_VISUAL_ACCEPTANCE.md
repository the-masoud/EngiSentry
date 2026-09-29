# EngiSentry — P11.2B Manual Bilingual Visual Acceptance Runbook

Audience: **software operator**. This is a UI/visual acceptance only. It does **not** require project-engineer
approval, and it must be performed **manually** — no GUI automation, no scripted input.

Prerequisite: the P11.2B Release build must be deployed to
`%APPDATA%\Autodesk\ApplicationPlugins\EngiSentry.bundle\Contents\Win64\` (deployment is a separate task).
The currently installed bundle is **stale** and will not show the P11.2B UI.

Disposable test drawing (read-only source; you may open it):
`artifacts\runtime-validation\p111-live\dwg\P11.1-LIVE-TEST-ONLY.dwg`

## Checks

1. **Confirm deployment.** Verify the installed `EngiSentry.AutoCAD2021.dll` SHA256 equals the P11.2B
   Release build output (see the release candidate manifest). If it does not match, stop — deploy first.
2. **Launch AutoCAD 2021 manually.** Open AutoCAD 2021 by hand.
3. **Open the disposable test drawing.** Open `P11.1-LIVE-TEST-ONLY.dwg`.
4. **Run the command.** Type `ES_PLACEMENTRESOLVE` and press Enter.
5. **Inspect the English UI.** Confirm: title "Equipment Placement Resolution"; sections
   EQUIPMENT / PROPOSAL, PLACEMENT SELECTION, ENGINEER REVIEW, DECISION, REVIEWER, TECHNICAL DETAILS;
   the primary action reads "Record Decision"; the header shows the drawing name, units and an open-item
   count (no full SHA256 in the header).
6. **Switch language without restart.** Click the language selector (top-right, reads "فارسی"). Confirm the
   whole surface re-renders to Persian **without restarting AutoCAD**.
7. **Verify Persian RTL layout.** Confirm human-readable labels/sections read right-to-left and the layout
   is mirrored; nothing is clipped or overlapping.
8. **Verify technical values stay LTR.** Confirm technical values read left-to-right inside the Persian
   layout.
9. **Verify specific values** remain correct and copyable (select and copy each):
   - equipment tag (e.g. `T-1001`)
   - coordinates (e.g. `E = -61979.3263944098`, `N = 20752.3800604182`)
   - SHA256 (64 hex chars)
   - Proposal ID
10. **Verify the Classification label.** Confirm an explicit "طبقه‌بندی" (Classification) label is visible
    beside its dropdown.
11. **Verify the Disposition label.** Confirm an explicit "تصمیم درباره پیشنهاد" (Disposition) label is
    visible beside its dropdown.
12. **Expand / collapse Technical Details.** Confirm "جزئیات فنی" is collapsed by default, expands on click,
    shows the raw evidence (SHA256, Proposal ID, fingerprint, Resolution ID, Disposition Record ID), and
    collapses again.
13. **Verify disabled primary action readability.** With required fields empty, confirm the "ثبت تصمیم"
    button is clearly readable while disabled and a concise inline message states what is missing.
14. **Verify enabled primary action readability (without saving).** Fill the required fields and select a
    placement so the button becomes enabled, confirm it is clearly readable — **then do not save**.
15. **Resize / scroll check.** Shrink the palette so it is narrower and shorter; confirm the header stays
    usable, the bottom action/status area stays reachable, the content scrolls (no clipping), and Technical
    Details remains reachable.
16. **Close and reopen the review UI.** Close the palette and re-run `ES_PLACEMENTRESOLVE`.
17. **Verify the selected language persists.** Confirm the surface reopens in Persian (the language chosen in
    step 6) without any Windows/AutoCAD language change.
18. **Stop without creating a new governed disposition.** Close the surface without saving. Do not record a
    decision. Optionally switch back to English.

## Result

Record: date/time, operator, deployed DLL SHA256, each check PASS/FAIL, and any screenshot evidence.
A FAIL here is a software/UI finding — not an engineering-judgment matter.

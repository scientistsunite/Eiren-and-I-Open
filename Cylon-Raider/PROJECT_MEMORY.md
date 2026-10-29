# Cylon Raider — Public Project Memory

## 2026-10-29 — Windows v0.5 preliminary UI

- User approved scoped source, original artwork and build-file use and public publication for Cylon Raider v0.5. Publication review excludes private history, memory, logs and unrelated applications.
- One application window with ten sections. History/exports and configuration views are embedded; every v0.4 action is mapped in `Windows/docs/v05/UI-REVIEW.md`.
- Silent Meteor Windows colors and rounded-control styling serve as a read-only reference. Other applications are unchanged.
- App-only session index retains all observed application identities. Virtual grid updates preserve viewport and selection. Capture, persistence, reports and service implementations are unchanged except version identity metadata.
- Explicit launches open the window; automatic sign-in launches retain tray preferences.
- Windows run 40892953194 on source 4185c4b passed original measurement 19, history 60, quality/provenance 76, native Options 78 and unified UX 2,456 assertions; installer compilation and artifact upload succeeded. Local deterministic checks and zero-warning cross-builds also passed.
- Source d425fc1 passed Windows run40894514248: original19/history60/quality76/nativeOptions78/UX2506, installer and artifacts. Preview inspection exposed a test-harness issue: oversized ClientSize requests on a smaller Windows desktop can retain phantom client dimensions. The fixture now uses fitted actual display bounds and stronger child/ancestor containment assertions. Caption/button/toolbar widths are constrained for compact layouts. Actual-fit run40896750539 found History toolbar clipping at150%font. History and diagnostic tools now scroll independently above space-reserved lists; the global status/actions area is also height-bounded. Replacement native validation is pending; no failed result is represented as installer acceptance.
- Distribution remains preliminary and unsigned. Physical Windows, multi-monitor/DPI, real ETW, service and installer lifecycle acceptance remains outstanding. Existing releases/tags/assets are untouched.
- Windows source and tests are present; Ubuntu/macOS remain placeholders. No new open-source reuse license has been granted.

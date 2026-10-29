# Cylon Raider — Public Project Memory

## 2026-10-29 — Windows v0.5 preliminary

- Owner approved scoped publication of reviewed Cylon Raider Windows source, original artwork, synthetic tests and dedicated build files. No private Git history, memory or internal logs are imported. Other application files and releases are unchanged; later Cylon collection publication is recorded below.
- One application window contains Dashboard, Live Traffic, Top Applications Observed, Monitoring, History, Measurement Health, Storage, Startup Service, Settings and About & Maintenance. History/exports and configuration are embedded. Every existing action is mapped in `Windows/docs/v05/UI-REVIEW.md`.
- Silent Meteor Windows theme is a read-only reference: dark blue-gray palette, rounded buttons, native dark title bar and one-second explicit tooltips. No Silent Meteor files are copied or changed.
- The app-only session index retains all observed identities; the virtual application table preserves scroll and selection. Capture, measurement, history, report, storage and service implementations remain unchanged apart from version provenance.
- Explicit launches open the window; automatic sign-in launches retain tray preferences. Apply and Discard provide explicit handling of settings drafts.
- Compact layouts reserve actual native input heights and keep tools in bounded scrolling areas above usable lists. Window bounds are fitted after DPI changes. Tests use actual display bounds rather than oversized cached client dimensions.

## Verified build

- Source: `b82a8d0426a78bbff04608b2a26d9d26273a9765`.
- Windows run: https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/40903515615 — passed.
- Original measurement: 19, history: 60, quality/provenance: 76, native settings: 78 and unified UX: 3,823 assertions passed; network API guard, self-contained Windows x64 publishing, Inno Setup compilation and artifact upload passed.
- The UX fixture covers 1,300+ identities, retained vertical/horizontal scroll and offscreen selection, all ten sections, no second application window, draft discard, compact History and actual fitted layouts at 100/125/150/200% font sizes. No collector was started; no service or existing user data was used.
- Installer, portable/source archives, SHA256 checksums and provenance are in the downloadable artifact. See `Windows/docs/v05/BUILD-RESULTS.md` for links and installer checksum.
- Final documentation records this verified source; it does not alter application, tests, installer or workflow inputs.

## Limits

Unsigned preliminary Windows x64 build for user testing. Physical DPI/multi-monitor, real ETW, boot-service and installer lifecycle/upgrade acceptance were not rerun in this UI workflow. Existing behavior is retained, not newly certified. Ubuntu/macOS remain placeholders. No new open-source reuse license is granted.

Public shared memory was reviewed. Status is recorded here within the authorized app scope; unrelated shared files remain unchanged.

## 2026-10-29 — Owner-requested version collection publication

- User explicitly authorized adding v0.5 preliminary to existing release 427955131 and renaming it `Cylon Raider — Windows`, with versions grouped on one page.
- Publication completed in [run 40917034009](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/40917034009). The same release ID and original `cylon-raider-windows-v0.4.0` tag are retained; tag commit remains `3f15a615e9cc1a09692abbdb8d4a183eadccf43e`. Prerelease status remains true.
- All three original v0.4 assets retain their original IDs, names, sizes, SHA256 hashes and URLs. Their downloaded bytes were verified before and after publication. Six versioned v0.5 assets were added; every downloaded new file was hash-verified.
- The v0.5 installer is byte-identical to the verified source/build artifact; there was no rebuild or application change. Installer SHA256 remains `03d28df0802e904d8b336d43fafdb3e6d0d83c27d942c895cbd1ab1649ab1fe9`. A separate unauthenticated public installer download also matched.
- The release body now has v0.5 and v0.4 sections with separate downloads and checksums; original v0.4 notes are preserved. No other application or release was changed. Future authorized Windows versions should be added to this same collection without replacing historical assets.
- Publisher guards passed 16 synthetic tests, and the receipt is recorded in `Release-Tools/v05/publication-40917034009.json`. The first two publisher attempts stopped before any mutation on API request-format errors; the corrected workflow completed all checks.
- [Windows version collection](https://github.com/scientistsunite/Eiren-and-I-Open/releases/tag/cylon-raider-windows-v0.4.0) now supplies persistent public downloads; temporary Actions artifacts remain supplementary.

## 2026-10-29 — Windows v0.5.1 prerelease preparation

- Owner requested fixes after display testing: clipped header, chart caption overlap, ambiguous retention wording and monitoring state, repeated close, and tray left-click restoration.
- Header uses measured automatic rows rather than a capped scrolling viewport. A persistent icon/text indicator distinguishes receiving measurements, stopped desktop capture, waiting, and disconnected boot preview; it does not equate missing data with zero traffic. A single state-aware global Start/Stop button replaces duplicate embedded controls; restart remains available in Monitoring and Exit in About & Maintenance and the tray. Live process details can be expanded, leaving filters and lists room.
- Chart reserves font-dependent space for its scale caption. Retention text explains 0 using the history setting (normally 15 recorded days, or indefinite).
- Window-close initiates asynchronous exit after the initial closing event returns; restore ignores shutdown and embedded forms are disposed during exit. Single tray left-click restores the existing window. Continue-in-tray preference is retained.
- Runtime compilation passed on Linux with Windows targeting, zero warnings/errors. Native Windows regression/build evidence is pending. Early native layout tests rejected compact 200% font layouts; the toolbar caps are now reapplied on layout, and all six History views use a full-width selector to retain list height. The original measurement/history/service implementations are unchanged; no other app files are edited.

### Verified v0.5.1 build

- Source `21c3c9c655606fb318d642b710a93387c89aa079`; [Windows run 40941810651](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/40941810651) passed. Original measurement 19, history 60, quality/provenance 76, native settings 78 and unified UX 3,916 assertions passed; offline source guard, self-contained publishing, installer compilation and artifact upload passed.
- All six History views retain at least 80 pixels of list height in the compact font-scale fixture. Persistent status and its single state-aware Start/Stop action fit every section; actual tray left-click and one-close shutdown are tested. All original monitoring/backend contracts remain unchanged.
- Installer SHA256 `1c8998c80a1b4d3c2f598caf53e58edd728ce8010166630925a710e6692bed40`. Full results and artifact link are in `Windows/docs/v051/BUILD-RESULTS.md`.
- Early layouts were rejected and corrected. The final pipeline also corrected Windows source-check encoding and a source-archive workflow filename. Failed artifacts were not published. Physical M7/4K/DPI, real ETW, boot-service, multi-monitor and installer lifecycle/upgrade testing remain outstanding.
- The owner-requested Windows collection policy applies: append this version alongside v0.4/v0.5, preserving all nine existing assets and the historical tag. The app-scoped publication workflow pins the successful artifact and verifies old/new download bytes; publication completed in run 40942543004. All nine earlier downloads and all six additions were hash-verified. The original release ID, title, prerelease status and historical tag are preserved; the curated receipt is `Release-Tools/v051/publication-40942543004.json`. A separate public installer download also matched the build SHA256.

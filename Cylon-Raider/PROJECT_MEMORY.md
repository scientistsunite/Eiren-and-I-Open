# Cylon Raider — Public Project Memory

## 2026-10-29 — Windows v0.5 preliminary

- Owner approved scoped publication of reviewed Cylon Raider Windows source, original artwork, synthetic tests and dedicated build files. No private Git history, memory or internal logs are imported. Other application files and existing releases/tags/assets are unchanged.
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

- User explicitly authorized adding the verified v0.5 preliminary files to existing Cylon Raider release 427955131 and renaming it `Cylon Raider — Windows`, with related versions grouped on that page.
- The original `cylon-raider-windows-v0.4.0` tag and all three existing v0.4 asset IDs, names and checksums must remain unchanged. No new compilation, tag movement, release replacement, merge or other application changes are required.
- A narrowly scoped publication workflow uses the successful run 40903515615 artifact 12910193270, checks its exact source/digest and package checksums, and appends versioned files only. Current status: publication guards and workflow are being prepared; no release change is yet claimed.

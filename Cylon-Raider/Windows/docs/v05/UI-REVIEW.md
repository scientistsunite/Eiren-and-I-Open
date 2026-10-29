# v0.5 preliminary UI review

## Scope

Owner-approved publication of reviewed Cylon Raider Windows source, original artwork, synthetic tests and app-specific packaging. No private Git history, project memory, internal logs or unrelated application source is included. The Silent Meteor Windows palette is a read-only reference; none of its files are changed or imported.

## Preserved action mapping

| Section | Controls and information |
| --- | --- |
| Dashboard | Live rates, chart, session totals, storage summary, recent integrity evidence; shortcuts to applications and measurement health |
| Live Traffic | Name/PID/path filter, combined/download/upload sort, application grouping, double-click expansion, process identity and scrollable hosted-service details, details refresh |
| Top Applications Observed | Every session-observed identity and download/upload/combined totals; deliberate column sorting; stable scroll and selection |
| Monitoring | Current status; Start, Pause, Restart |
| History | Recorded date/hour/sort/Refresh; application, one-second and lifecycle lists; hourly/second CSV exports; UTC range, reason, protocol and PID filters; attribution/integrity exports; integrity and recording-session lists; report status and report folder |
| Measurement Health | Coverage, collection/recording diagnostics, unknown-byte charts and reasons, historical evidence shortcut |
| Storage | Separate history/report paths; Browse, Use Default and Open Folder for each; storage usage and destination validation |
| Startup Service | Existing Off, sign-in and boot service modes; storage shortcut |
| Settings | Auto-monitor, diagnostic detail and retention, indefinite history, hourly reports, automatic-launch tray/window preferences, continue in tray |
| About & Maintenance | Version, provenance, privacy and notices; Clear history/reports, Retry logging, Uninstall |

Apply Settings validates and saves pending settings across the configuration sections. Discard unsaved changes restores all saved settings without changing capture or recordings. Existing asynchronous-operation, deferred-close, export and error safeguards remain. Global Start/Pause and Exit remain accessible. Normal folder/save dialogs and explicit confirmation/UAC dialogs are retained.

Alt+Home opens Dashboard. Ctrl+1 through Ctrl+9 and Ctrl+0 select sections. Sidebar navigation supports arrow keys. Tooltips explain controls after one second. List viewports fill available space; long tool areas scroll independently.

## Validation

- Cross-build: application and UX test project compile with zero warnings/errors on .NET SDK 10.0.403 using Windows targeting.
- Source network API guard passed locally.
- Windows run 40892953194 passed measurement/history/quality, 78 native settings assertions and 2,456 unified UX assertions, compiled the installer and uploaded artifacts. A replacement run validates final work-area and draft-discard refinements.
- Dedicated Windows workflow runs original measurement, history, quality/provenance, native settings lifecycle/layout and new UX tests before producing an installer.
- New UX fixtures use synthetic data for over 1,300 application identities and verify scroll, horizontal position, selection and stable rows. The unified-shell fixture is restricted to disposable hosted Windows, refuses preexisting app/service data, and never starts capture.
- Windows native and installer-build results are recorded by the workflow and its artifact provenance. A successful cross-build alone does not establish Windows UI correctness.
- Physical DPI, multi-monitor behavior, installer upgrade/uninstall, real ETW, boot service and long-duration acceptance remain unverified by this UI build. No old release, tag or asset is replaced.

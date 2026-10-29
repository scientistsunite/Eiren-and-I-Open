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

The final [Windows run40903515615](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/40903515615) passed original measurement19, history60, quality/provenance76, native settings78 and unified UX3823 assertions, offline guard, self-contained publishing, installer compilation and artifact upload. [Build results](BUILD-RESULTS.md) identify the exact source and download.

The fixture tests actual display-fitted windows at100/125/150/200% font sizes, all sections, control containment and non-overlap, reachable Apply/Discard actions, useful History viewports and stable scrolling with over1,300 identities. Synthetic app-only previews were inspected. No collector/service/user profile was opened.

Physical DPI/multi-monitor, real ETW, boot service, installer upgrade/uninstall and long-duration acceptance remain unverified by this UI workflow. No old release, tag or asset is replaced. Cross-builds also passed with zero warnings/errors, and the public scaffold layout check passed.

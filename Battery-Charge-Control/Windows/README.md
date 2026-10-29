# Battery Charge Control for Windows

**Version:** v0.1 — experimental development preview
**Publication status:** Source-only prerelease candidate; **not** certified, production-ready, signed, or approved for hardware operation.
**Evidence cutoff:** 29 October 2026. This summary records historical internal development evidence; it does not assert that equivalent tests have been rerun against the first public commit.

> **Do not install or execute the production application, installer, Windows service, or ASUS charging commands on a real computer merely because the source compiles.** Synthetic tests and installer fixtures do not certify native behavior or charging safety.

## Purpose and approach

A Windows 11 C#/.NET 10 implementation of Battery Charge Control, with a single application and optional vendor-specific controller modules. It aims to display battery status on supported Windows systems and eventually permit carefully validated, consent-based charging thresholds on specific laptop models. The original Ubuntu/MacBook charging-control implementation is **not** ported or included in this Windows publication.

The design recognizes manufacturer names/aliases for 15 manufacturer families but ships only an **experimental ASUS charging-controller candidate**. Recognition is not compatibility. Unknown, unsupported, driver-unavailable, or incompletely verified devices must remain **monitoring-only**. No universal Windows API guarantees charge-limit setting.

### Implemented in the source (not necessarily runtime-verified)

| Area | Current implementation | Verification boundary |
| --- | --- | --- |
| General interface | WinForms UI, battery read via Windows API, tray icon, preferences and status/reporting | Source/compile and simulated logic; production UI not executed in acceptance tests |
| Model recognition | BIOS/registry-based vendor detection and capability classification | Synthetic tests, not real-device compatibility |
| Vendor architecture | Modular controller contract; optional ASUS assembly; other vendors fall back to monitoring | ASUS firmware writes not hardware-tested |
| ASUS candidate thresholds | FA607 candidate 30–100% in 5% steps; other ASUS candidates 60/80/100 | **Unverified**; no promise any model supports these values |
| Privilege model | Ordinary-user GUI, explicit approval/UAC for privileged operations, policy/recovery state | Source and simulations; real UAC, SCM and firmware paths not tested end-to-end |
| Installer | Inno Setup recipe; choice of monitoring-only/vendor mode; optional desktop link; one Start menu app shortcut; Installed apps integration | Production installer compiled, **never executed** in the cited verification |
| Maintenance | Guarded modify/repair/uninstall and recovery-first source logic | Synthetic tests, **not** native production lifecycle acceptance |
| Boot handling | Optional idle service; startup reapplication and best-effort preshutdown recovery | Native service startup/shutdown not tested |
| ARM64 | Source/payload build and independent synthetic installer fixture | Not proof of ARM64 production GUI or installer support |
| Localization | English source UI | Turkish localization remains future work |

## Critical battery and alternate-boot safety

A limit written by firmware may persist even if Windows closes, crashes, loses power, hibernates, or the computer boots another operating system or external disk. An optional Windows service may **attempt** to restore the verified original setting on an orderly shutdown and reapply a saved Windows-specific preference at the next boot, but this is **not guaranteed**. Fast Startup, unavailable service/driver, BIOS changes, crashes or abnormal shutdowns may prevent it. The software must never guess that the original value was 100%.

The current recovery design requires a recognizable original limit and writes a durable `RestorationRequired` obligation **before** applying a charging command. Shutdown recovery is triggered by either saved enablement or that outstanding obligation. Verified original-state readback is required before clearing the obligation; unreadable or mismatching state blocks further changes. Policy failures must not silently disable future recovery. A firmware command returning success or matching readback is **not evidence that charging physically stopped**.

**Hardware writes remain outside this release's validation and outside the authorization given by this documentation.** No ASUS TUF Gaming A16 FA607NUQ-RL048 physical battery test has been completed.

## What has actually been tested?

Evidence labels: **SOURCE** = code/static inspection; **SIMULATION** = in-memory managed tests with fake battery/controller; **BUILD** = compiler/packaging only; **FIXTURE** = separately named, no-firmware installation fixture; **NOT TESTED** = no completed acceptance evidence. Historic results below refer to the internal implementation at the time, and are not current public GitHub Actions results. All future runs must record the public commit and complete outcome independently.

### Milestones and completed results

| Milestone | Work achieved | Observed outcome | Not established |
| --- | --- | --- | --- |
| M1 — Windows foundation | Universal app architecture, 15 manufacturer families, plugin interface, battery monitoring, experimental ASUS adapter, installer and service source | SOURCE + Windows build subsequently confirmed | Real compatibility and physical cutoff |
| M2 — build and analysis | .NET 10 x64/ARM64 builds, x64 Inno installer generation, static/CI checks; addressed C# platform and unused-variable diagnostics, Actions runtime deprecations and an Inno uninstall identifier | BUILD succeeded in historical Windows workflow runs; corrections were implemented and checked | Executed production installer/GUI |
| M3 — isolated shared runtime | Production-domain/charging logic compiled into a separate simulation-only graph; fake transport, battery, policy, lifecycle and preview; native adapters excluded structurally | 145 managed simulations passed, plus documented boundary/source checks. Earlier policy-persistence stage had 117 simulations and five boundary tests | Native hardware, service or GUI behavior |
| M4 — recovery and maintenance guards | Durable recovery marker, original-limit persistence before writes, readback-based restore, BIOS/device-change guards, fail-closed maintenance and service-removal gating | SOURCE/SIMULATION regression coverage for failed saves, failed rollback, unavailable originals, maintenance cancellation, service-state decisions | Production SCM races, real file interruption and production maintenance execution |
| M5 — isolated installer fixture | Compiled a **separate** synthetic preview payload and CI-only Inno fixture; ordinary-user account/profile, install, modify, repair, uninstall/reinstall and cleanup | **Passed** on Server 2025 x64 and Windows 11 ARM64, after two failed experiments corrected below | Windows 11 x64 desktop, actual app, ASUS firmware or production installer |
| M6 — desktop preview and later compatibility | Bounded Windows 11 x64 preview plan, case identifiers D01–D09 and prerequisites prepared | **NOT EXECUTED**; no approved desktop-specific package/runtime acceptance yet | All local UI, installer, production and physical claims |

### Managed simulation and source tests

Historical checks exercised all 15 recognized manufacturer families and aliases, no battery, unreadable readings, unknown/unsupported devices, controller unavailable, compatibility classifications, UI-consent default-No logic, privilege/source boundaries, startup/stop/shutdown decisions, policy corruption, baseline save-before-write, failed save/readback/rollback, BIOS changes, manual restore/prepare-for-other-OS, and a recovery obligation surviving a failed preference commit. The managed harness compiles a tightly controlled subset of production logic and refuses native device, process, service, registry, WMI and firmware adapters. **This is not an OS sandbox**; the public test graph and dependencies must be reviewed with each change.

Historical source-review/compile-only Windows evidence includes successful runs on 25 October 2026 with **145 managed simulations** and separate PowerShell/Python regression counts at different checkpoints: **25 PowerShell + 27 Python** checks in one corrective build, and **34 PowerShell + 29 Python** in a later corrective build. These numbers describe different revisions/stages, **not one additive final suite**. Historical workflow and test records were retained separately; they have not yet been replicated publicly.

### Milestone 5: failures, corrections and success

1. **Experiment 1 — failed before installation:** Both x64 and ARM64 approved workers exited during ordinary-user profile initialization because an empty `Path` was passed. No Inno commands ran, and no installer-lifecycle pass was claimed. Added authenticated-SID profile validation, initialization and sanitized diagnostics.
2. **Experiment 2 — failed closed before installation:** An outside-profile `LocalApplicationData` path was rejected. The source of the path was not independently established. Corrected bootstrap to use the fresh account's SID-rooted canonical directories and independently verify both registration views. Installer cases remained **NotRun** for this attempt.
3. **Experiment 3 — passed (25 October 2026):** On **Windows Server 2025 x64**, 10/10 synthetic fixture checks passed; on **Windows 11 ARM64**, 10/10 passed. Seven setup/modify/repair/uninstall/reinstall command records per architecture exited zero; no Failed/NotRun cases in this successful experiment. Ordinary nonadministrator identity, payload allowlist/hash, Installed apps registration, one Start menu shortcut, desktop link addition/removal, deleted synthetic-payload repair, first uninstall, reinstall and second uninstall/cleanup were checked.

The fixture workers reported empty diagnostics and no restart requirements. The cleanup harness found fixture files/registration/shortcuts absent after uninstall; supervisors reported temporary account/profile/staging cleanup, but **there was no independent post-deletion OS/profile inventory**. On ARM64, the Inno engine may itself use emulation. The test **did not launch the payload** or execute any production application, service, driver, firmware command or production installer. The temporary approval variables used for those historical runs were subsequently reported removed; that does **not** authorize a rerun.

Historical diagnostic ZIPs from the successful experiment were independently hashed during review (SHA-256):

- x64: `8dab7278cbff61395698f5a74be211e53a8b7b020697ee2725121188cdd49d61`
- ARM64: `c6bb7143adb48f7d60a3c39d5e6d51cd0a3155a2d6339ae98394c031edc0e219`

These are **fixture diagnostic archive** hashes, not signatures or hashes of this public source prerelease. Raw logs and personal/private machine data are not distributed here.

### Reusable testing lessons

A common testing framework was derived from this work: explicit approval gates, synthetic fixtures with names/identities separate from production, ordinary-user profile verification, architecture-specific outcomes, file hash/allowlist verification, cleanup attestations, and retention of failed attempts alongside successes. This Windows publication does not claim that the entire private shared testing infrastructure was copied or that generic fixtures are production safe.

## Next milestones and acceptance gates

- **M6a — public source verification:** independent review of copied sources, license and any third-party dependencies; source SHA-256 manifest; clean public compile, Python boundary regressions, 145 managed simulation cases where still applicable, failure counts and Windows runner identities. A new public CI run is required before calling this public revision tested.
- **M6b — synthetic Windows 11 x64 desktop preview:** produce a **separately reviewed**, portable, explicitly synthetic preview with no native or production fallback; check approved D01–D09 (package identity, extraction, startup, DPI/focus, 20 refreshes, synthetic classifications, minimize/close, process exit, folder-only cleanup). All currently **Not Executed**. The current preview depicts synthetic ASUS/65% and has **no production tray**; N/A is not a passed tray test.
- **M6c — isolated desktop installation fixture:** separately review a desktop-only per-user installer for only the synthetic preview; verify Installed apps, one Start-menu link, optional desktop shortcut, modify, repair, reinstall, uninstall and cleanup without device/service/firmware adapters. Never repurpose approval-locked cloud fixtures on a personal computer.
- **M7 — safe production interface integration:** inject and review fake dependencies before production UI initialization; isolate *all* reachable device, registry, process/UAC, service, installer, report and firmware routes; test real tray/focus/accessibility and ordinary-user cancellation only in a separately approved test build.
- **M8 — model-specific read-only hardware characterization:** on an approved ASUS FA607NUQ-RL048 test device, check official drivers, manufacturer/model/BIOS/battery reporting and DSTS readback **without writing**. If original firmware state cannot be recognized/restored, stop.
- **M9 — explicitly authorized charging/restoration evaluation:** only after independent hardware/interface review, use conservative model-confirmed limits and consent; separately verify command acceptance, original-state restoration, observed real charging cessation, orderly boot/shutdown, competing vendor utilities, failure states and other-OS limitations. Do not induce power loss or forced discharge as an initial test.
- **M10 — product readiness:** Windows 11 x64 and ARM64 native end-to-end tests, installer upgrades and clean removal, service reliability/performance/idle resource measurements, signing and supply-chain review, user-facing Turkish/localization, accessibility and broader manufacturer controllers **only when independently implemented and validated**.

No timetable or physical compatibility claims are implied by these proposed milestones.

## Development and build boundaries

Expected layout: `src/` for the main .NET implementation, `tests/` for simulation and separated synthetic entry points, `scripts/` for review/build helpers, and `installer/` for Inno recipes. For a developer-controlled Windows environment with .NET 10 SDK and optional Inno Setup 6, start with source and boundary checks, then compilation; read each script before running it. **The production build script may generate an installer, but the production executable/installer must not be run solely on the strength of these checks.**

This is a **source-only** experimental release. It contains no approved runnable installer, no executable release asset, no firmware driver, no private repository history, and no internal CI logs. Source code does **not** constitute hardware acceptance. The exact source snapshot is documented in `SOURCE-FILES-SHA256.txt` alongside this README. Future public CI results and their limits should be added here and to the public test matrix.

## License and contributions

The Windows implementation is distributed under the license present in [`LICENSE`](LICENSE) once explicitly approved and added. The repository's other projects may have different licenses or none. Each contributor is responsible for avoiding third-party code copied without the required permissions; future developments may be offered under different terms where contributor rights permit.

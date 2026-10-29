# Cylon Raider — Windows

Windows versions are collected on this page. Each version has its own downloads and checksums; earlier files remain available. The existing release URL and historical v0.4.0 tag are retained.

## v0.5.1 prerelease — latest addition

Fixes from v0.5 display testing:

- A measured, non-scrolling header with a prominent icon and explicit ON, OFF or WAITING status. One Start/Stop button changes with state; duplicate controls are removed from Monitoring. Exit is in About & Maintenance and the tray.
- Font-aware chart caption spacing and wider wrapping Live Traffic filters. Process details can be expanded. All six History views remain available through a full-width selector that preserves list height.
- Clear diagnostic retention wording: 0 uses traffic-history retention, normally the newest 15 recorded days, or indefinitely when that setting is enabled.
- One window-close request completes orderly shutdown when Continue in tray is disabled. Single tray left-click restores the existing application window. The existing tray/background preference is preserved.

The capture engine, collector, history storage, reporting and service implementations are unchanged. Runtime changes are confined to presentation, navigation, monitoring controls and the requested window lifecycle fixes, plus version provenance.

- **[Windows x64 installer](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-raider-windows-v0.4.0/CylonRaider-Setup-0.5.1-prerelease.exe)**
- [Portable Windows x64 archive](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-raider-windows-v0.4.0/CylonRaider-Portable-0.5.1-prerelease-Windows-x64.zip)
- [Version-specific source and build recipe](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-raider-windows-v0.4.0/CylonRaider-Source-0.5.1-prerelease.zip)
- [SHA256 checksums](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-raider-windows-v0.4.0/cylon-raider-windows-v0.5.1-prerelease-SHA256SUMS.txt) · [Build provenance](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-raider-windows-v0.4.0/CylonRaider-0.5.1-prerelease-BUILD-PROVENANCE.json) · [Testing notes](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-raider-windows-v0.4.0/CylonRaider-0.5.1-prerelease-READ-ME-FIRST.txt)

Source: `21c3c9c655606fb318d642b710a93387c89aa079`. [Windows validation and installer build](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/40941810651): measurement 19, history 60, quality/provenance 76, native settings 78 and unified UX 3,916 assertions, source network API guard, self-contained Windows x64 publishing and Inno Setup compilation.

Unsigned prerelease for testing. Synthetic font-scale checks at 100/125/150/200%, all ten sections, all six History views, monitoring-state transitions, actual tray left-click handler and one-close shutdown passed. Physical Samsung M7/4K/DPI, multi-monitor, real ETW, boot-service and installer lifecycle/upgrade acceptance remain for device testing. All v0.4 and v0.5 assets remain available and unchanged.

## v0.5.0 preliminary

A UI and user-experience update: one application window with the requested ten sections, embedded History and settings, a complete session application list with stable scrolling, rounded dark controls and one-second explanations. Measurement, logging, reports, storage and service logic are preserved apart from version provenance.

- **[Windows x64 installer](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-raider-windows-v0.4.0/CylonRaider-Setup-0.5.0-preliminary.exe)**
- [Portable Windows x64 archive](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-raider-windows-v0.4.0/CylonRaider-Portable-0.5.0-preliminary-Windows-x64.zip)
- [Complete version-specific source and build recipe](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-raider-windows-v0.4.0/CylonRaider-Source-0.5.0-preliminary.zip)
- [SHA256 checksums](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-raider-windows-v0.4.0/cylon-raider-windows-v0.5.0-preliminary-SHA256SUMS.txt) · [Build provenance](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-raider-windows-v0.4.0/CylonRaider-0.5.0-preliminary-BUILD-PROVENANCE.json) · [Testing notes](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-raider-windows-v0.4.0/CylonRaider-0.5.0-preliminary-READ-ME-FIRST.txt)

Verified source: `b82a8d0426a78bbff04608b2a26d9d26273a9765`. [Passing Windows build](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/40903515615): measurement 19, history 60, quality/provenance 76, native settings 78 and unified UX 3,823 assertions, plus source network guard, self-contained publishing and installer compilation.

Unsigned preliminary build for user testing. Physical DPI/multi-monitor, real ETW, boot-service and installer lifecycle/upgrade acceptance remain outstanding. This addition publishes the already-verified build without rebuilding it. Build metadata and notes retain their original bytes and use versioned filenames here.

## v0.4.0 — preserved downloads

- [Windows installer](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-raider-windows-v0.4.0/CylonRaider-Setup-0.4.0.exe)
- [Portable archive](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-raider-windows-v0.4.0/CylonRaider-Windows-0.4.0-Portable.zip)
- [SHA256 checksums](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-raider-windows-v0.4.0/cylon-raider-windows-v0.4.0-SHA256SUMS.txt)

<details>
<summary>Original v0.4.0 release notes, preserved verbatim</summary>

# Cylon Raider — Windows 0.4.0

PRERELEASE: sequential Windows VM validation passed, but physical-machine acceptance testing was still outstanding when this package was selected.

Package source: individually selected release assets, checked locally before public upload.
SHA-256 checksums are attached. No project-wide open-source license has yet been selected.
GitHub's automatically generated Source code archives currently include public repository scaffolding, not this application's implementation.

</details>

GitHub's automatically generated Source code archives follow the historical tag and are not the newer implementations. Use the explicit version-specific source archives above. No new project-wide open-source reuse license is granted.

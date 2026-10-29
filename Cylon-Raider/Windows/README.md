# Cylon Raider Windows — v0.5.1 prerelease

This prerelease update reorganizes the existing Windows application into one application window. It is intended for user testing; physical Windows acceptance remains outstanding.

Navigation: Dashboard, Live Traffic, Top Applications Observed, Monitoring, History, Measurement Health, Storage, Startup Service, Settings, About & Maintenance.

- Dashboard contains the live monitor overview. History and all CSV exports are embedded in the same window.
- All applications observed in the viewer session remain in their own list, including inactive applications. Values update without rebinding the grid or moving the viewport. Click a column heading to sort; rows remain stable between deliberate sorts. Explicit Start/Restart begins a new viewer session, as before.
- Existing capture, attribution, persistence, report, retention, service and security behavior is preserved. This is a UI/UX update, not a measurement-engine revision.
- Buttons use rounded corners and a dark blue-gray theme. The native Windows title bar is dark gray; tooltips appear after one second. High-contrast system colors are respected.
- Monitoring status stays visible above every section. One Start/Stop button changes with connection state; waiting for measurements is explicit.
- History provides all six views through a full-width selector. Diagnostic retention explains how 0 uses the traffic-history retention setting.
- One close request exits when Continue in tray is disabled; a single tray left-click restores the same window.
- An explicit Start-menu launch opens the window. Automatic `--startup` launches retain the existing window/tray preferences.

Use the prerelease Windows x64 installer for a normal installation; it retains the existing application ID and preserves data by default. Portable builds use their existing separate portable profile. The installer is unsigned.

Build: `.github/workflows/cylon-raider-v051-prerelease.yml`, Windows hosted runner, .NET SDK 10.0.403, Inno Setup 6, Python 3.13. Run `./packaging/v051/build.ps1` from a clean Windows checkout. Outputs include installer, portable archive, source archive, SHA256 checksums and build provenance. The workflow does not install the application or register a service.

See [v0.5.1 changes](docs/v051/CHANGES.md), [UI action mapping and validation](docs/v05/UI-REVIEW.md) and [distribution notices](docs/THIRD-PARTY.md). Application source and artwork remain copyright Eiren & I Apps; no new open-source reuse license is granted.

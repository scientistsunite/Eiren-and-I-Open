# v0.5.1 prerelease build results

- Verified source: `21c3c9c655606fb318d642b710a93387c89aa079`.
- [Successful Windows build, tests and installer](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/40941810651).
- [Downloadable complete build artifact](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/40941810651/artifacts/12926868897).
- Original measurement: 19; history: 60; quality/provenance: 76; native settings: 78; unified UX: 3,916 assertions passed. Source network API guard, self-contained Windows x64 publishing, Inno Setup compilation and artifact upload passed.
- Synthetic checks cover all ten sections, every History view at 100/125/150/200% fonts, header control/status visibility through ancestor viewports, chart caption separation, monitoring state and button transitions, actual tray left-click handler, one-request close, retained selection/scroll and 1,300+ session identities.

Installer SHA256: `1c8998c80a1b4d3c2f598caf53e58edd728ce8010166630925a710e6692bed40`.

Portable SHA256: `99dc67c2b409620bf899369d058f3b601ef65812940466feb2de9164f0583104`.

Source archive SHA256: `ff17749f8e24e06a5d7d7e7c579bf5c20c87cd5d65e9b03fd69a3c7b180f587c`.

The final build fixed two packaging-only failures: the source guard now reads UTF-8 explicitly on Windows, and the source-archive recipe uses the correct versioned workflow filename. Earlier compact-font layout failures were resolved before packaging. No failed build was published.

The application, tests, installer recipe and build inputs remain identical to the verified source above; this result document and release tooling were added afterward.

Unsigned Windows x64 prerelease. Physical Samsung M7/4K/display-DPI, multi-monitor, real ETW, boot-service, long-duration and installer lifecycle/upgrade acceptance are not claimed by this workflow. No collection, installation, service registration or existing user profile was used by the UI fixture. Other application files/releases and Ubuntu/macOS are unchanged.

## Public download

- [v0.5.1 prerelease installer](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-raider-windows-v0.4.0/CylonRaider-Setup-0.5.1-prerelease.exe).
- [Cylon Raider — Windows version collection](https://github.com/scientistsunite/Eiren-and-I-Open/releases/tag/cylon-raider-windows-v0.4.0), with version-specific portable/source archives, checksums and provenance.
- [Verified collection publication](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/40942543004). All nine prior assets and the historical tag are retained; six versioned files were added. Their downloaded bytes were verified, and an independent public installer download matched its build hash. No other application or release was changed.

# v0.5.2 prerelease build results

- Verified source: `36e4d96f8ed4933c108f942c5cd7a81da7bc840b`, based on merged public main `776d6c1` containing PR #4.
- [Successful Windows build and validation](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/40952435191).
- [Complete downloadable build artifact](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/40952435191/artifacts/12931728476).
- Passed: measurement 19, history 60, quality/provenance 76, native settings 78, unified UI 3,916, and 52 checks in the targeted tray-close suite. Its complete nine-case native matrix passed in this versioned build.
- All nine tray cases (Dashboard, Monitoring, History × normal/maximized/minimized) report managedVisible=false, nativeVisible=false, recreated=false and disposed=false. Actual tray restoration, maximized-state preservation and one-close shutdown also passed.
- Source network API guard, self-contained Windows x64 publishing, Inno Setup compilation and artifact upload passed.

Installer SHA256: `494d724f7f10707253028822b11f43776e9177654ca64b33f712acda5a48efd3`.

Portable SHA256: `c011163ec5280e4891a81a646be746468b0b39abe22806c33f2c12571a14b488`.

Source ZIP SHA256: `f43cbcfc385e4df9003f65fd683210febbaf851f74a18fe961d34b581da8b4fa`.

The application/build inputs are unchanged from the verified source. This result document and release tooling were added afterward.

Unsigned Windows x64 prerelease. Synthetic hosted checks do not establish physical M7/4K/DPI, multi-monitor, real ETW, boot-service or installer lifecycle/upgrade acceptance. No real collection, installation, service registration or existing user profile was used by the UI fixture. Other app files/releases and Ubuntu/macOS are unchanged.

## Published downloads

- [v0.5.2 prerelease installer](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-raider-windows-v0.4.0/CylonRaider-Setup-0.5.2-prerelease.exe).
- [Cylon Raider — Windows version collection](https://github.com/scientistsunite/Eiren-and-I-Open/releases/tag/cylon-raider-windows-v0.4.0), including portable/source archives, version-specific checksums and provenance.
- [Publication run 40953046225](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/40953046225) verified every old/new download. [Evidence-note update 40953323784](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/40953323784) retained the exact same assets and corrected only the targeted assertion count from the earlier suite's 53 to this run's actual 52.
- Independent public installer download matched SHA256 above. All 15 prior assets and the historical tag remain unchanged; six files were added. No other app files or releases were changed.

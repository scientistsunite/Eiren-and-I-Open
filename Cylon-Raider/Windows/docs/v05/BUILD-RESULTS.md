# Preliminary v0.5 Windows build results

Verified source: `b82a8d0426a78bbff04608b2a26d9d26273a9765`.

[Passing Windows build](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/40903515615)

[Download installer bundle](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/40903515615/artifacts/12910193270) — available until 12 November 2026; GitHub sign-in may be required. Extract the ZIP and run `CylonRaider-Setup-0.5.0-preliminary.exe`. The bundle also contains portable/source archives, checksums, build provenance and preliminary testing notes.

Installer SHA256:

```text
03d28df0802e904d8b336d43fafdb3e6d0d83c27d942c895cbd1ab1649ab1fe9
```

Passed: original measurement: 19, history: 60, quality/provenance: 76, native settings: 78 and unified UX: 3,823 assertions; offline source API guard; Windows x64 self-contained app/collector/service publishing; Inno Setup installer compilation. Synthetic controls were rendered for layout inspection; no real desktop or user data was captured.

The UX tests retain every assertion and inspect all ten sections at actual fitted100/125/150/200% font sizes, with no second application window, no overlapping controls, reachable configuration actions and usable History lists. They also cover all 1,300+ synthetic application identities and stable scroll/selection.

This is unsigned preliminary software. Actual physical DPI/multi-monitor, real ETW, boot-service and installer lifecycle/upgrade acceptance remain for user testing. The workflow does not install the application or start capture. No existing release, tag or asset is replaced.

Documentation recorded after this build leaves the verified application, test, packaging and workflow input trees unchanged. Artifact provenance identifies the actual compiled source above.

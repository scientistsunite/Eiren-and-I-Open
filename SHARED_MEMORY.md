# Eiren & I Open — Shared Project Memory

**Scope:** public repository `scientistsunite/Eiren-and-I-Open` exclusively.
**Created:** 2026-10-29.

## Verified public baseline

- This repository was started with a fresh README-only initial commit, without importing a private Git history.
- Six application roots are reserved: Archive Reader, Battery Charge Control, Silent Meteor, System Chronicle, Cylon Raider, and Cylon Detector.
- Each has Windows, Ubuntu, and macOS **placeholder** directories and an independent public `PROJECT_MEMORY.md`.
- Shared testing policy, matrix, synthetic fixture guidance, and a structure check live in `testing/`.
- No application implementations, installers, releases, or functional tests have been introduced as part of this scaffold.
- The initial scaffold had no license; the Windows BCC source is now published under its separately reviewed component-level `LICENSE`. Each of the six application directories now has a matching app-scoped MIT license for project-owned published material, subject to third-party notices and historical package rights.

## Agreed engineering principles

- Keep this public project independent of private repositories and confidential notes.
- Introduce reviewed applications individually. Do not import any private code or history without explicit, specific authorization.
- Publish reproducible validation and describe its limits accurately; distinguish a scaffolding check from application tests.
- Keep cross-application decisions here, application-level decisions in each `PROJECT_MEMORY.md`, and procedures in `MEMORY_SYNC.md`.
- Future testing, build, and platform requirements must be established using actual public implementation needs.

## Open decisions

- Review rights, dependency provenance, and third-party licenses before adding further source or publishing new packages.
- Select the first application/platform for public development.
- Establish application-specific tests and release criteria as code is introduced.

Update only with information suitable for permanent public disclosure.

## First application candidate — 2026-10-29

Battery Charge Control Windows v0.1 is the first reviewed experimental **source-only** publication candidate. Other platform/application folders are placeholders. Its Windows README distinguishes historical simulation/CI fixture evidence from new public testing, which remains pending. No production runtime, ASUS charging tests or firmware permissions are implied. The Windows license is application-scoped.

## Public inventory and license decision — 2026-10-29

- **Decision:** The six application directories each have an identically worded MIT License for project-owned source, documents and original material, subject to external rights. BCC Windows retains its existing component-specific MIT license.
- **Reason:** Consistent permissive terms across the public app families, without treating unrelated third-party assets as project-owned or altering prior MIT grants.
- **Scope:** Current public application directories only. This update neither copies private repositories nor automatically relicenses historical downloadable archives and binaries.
- **Current repository inventory:** BCC Windows and Cylon Raider Windows have public implementation source. Archive Reader, Silent Meteor and System Chronicle have historical release downloads but source directories remain placeholders. Cylon Detector is planned only.
- **Verification:** Documentation/licensing edits only. No new application runtime or hardware tests are claimed.
- **Follow-up:** Before any source import, binary redistribution or Release note update, review the actual material and third-party notices separately.

## Cylon Detector Windows implementation — 2026-10-30

- **Scope:** Owner-authorized reuse of public Cylon Raider 0.5.2 foundations for an independently identified Cylon Detector Windows 0.1 application. No private source/history is imported; other application implementations and releases are unchanged by this work.
- **Behavior:** Read-only selected-user Windows microphone/camera usage metadata, one-second application network counts, a bounded five-minute RAM context, default sensor-triggered network detail, optional continuous detail, and device/anomaly observations share one main log. Separately located interval summaries include minute-level network aggregates and late committed observations. No audio/video/payload acquisition or application network requests are added.
- **Status:** Windows source, app-specific tests and packaging are implemented. Final complete source-bound Windows CI, downloadable artifacts and release publication are pending. Physical sensor/ETW, pre-sign-in service, upgrade and display acceptance are not claimed. Ubuntu/macOS remain placeholders.
- **Licensing:** Detector project-owned published material follows its existing app-scoped MIT license, subject to dependency notices and third-party rights. This does not change any other app's license.
- **Follow-up:** Record the exact passing source/run, checksums and release URL in Detector memory and the public test matrix once verified.

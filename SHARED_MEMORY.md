# Eiren & I Open — Shared Project Memory

**Scope:** public repository `scientistsunite/Eiren-and-I-Open` exclusively.
**Created:** 2026-10-29.

## Verified public baseline

- This repository was started with a fresh README-only initial commit, without importing a private Git history.
- Six application roots are reserved: Archive Reader, Battery Charge Control, Silent Meteor, System Chronicle, Cylon Raider, and Cylon Detector.
- Each has Windows, Ubuntu, and macOS **placeholder** directories and an independent public `PROJECT_MEMORY.md`.
- Shared testing policy, matrix, synthetic fixture guidance, and a structure check live in `testing/`.
- No application implementations, installers, releases, or functional tests have been introduced as part of this scaffold.
- The initial scaffold had no license; the Windows BCC source is now published under its separately reviewed component-level `LICENSE`. Other apps remain unlicensed placeholders.

## Agreed engineering principles

- Keep this public project independent of private repositories and confidential notes.
- Introduce reviewed applications individually. Do not import any private code or history without explicit, specific authorization.
- Publish reproducible validation and describe its limits accurately; distinguish a scaffolding check from application tests.
- Keep cross-application decisions here, application-level decisions in each `PROJECT_MEMORY.md`, and procedures in `MEMORY_SYNC.md`.
- Future testing, build, and platform requirements must be established using actual public implementation needs.

## Open decisions

- Select appropriate licenses before publishing implementations.
- Select the first application/platform for public development.
- Establish application-specific tests and release criteria as code is introduced.

Update only with information suitable for permanent public disclosure.

## First application candidate — 2026-10-29

Battery Charge Control Windows v0.1 is the first reviewed experimental **source-only** publication candidate. Other platform/application folders are placeholders. Its Windows README distinguishes historical simulation/CI fixture evidence from new public testing, which remains pending. No production runtime, ASUS charging tests or firmware permissions are implied. The Windows license is application-scoped.

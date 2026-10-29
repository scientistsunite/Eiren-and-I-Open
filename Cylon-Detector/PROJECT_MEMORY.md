# Cylon Detector — Public Project Memory

## 2026-10-30 — Owner-authorized Windows 0.1 implementation

- User explicitly authorized public reuse of Cylon Raider 0.5.2 source/assets/build files, an independent Windows x64 Detector app, PRs/squash merging and publication of 0.1 installer/source/checksums and related files. No private sources or private history are used.
- Core requirements: observable microphone/camera application access metadata; per-application one-second network measurements; always-on five-minute RAM prebuffer while monitoring; default event-triggered disk network detail with five-minute postroll; optional continuous detail; unified main SQLite log; separate configurable interval summaries; nonjudgmental anomaly observations at all times; local-only/no audio/video or payload capture; independent optional pre-sign-in service.
- Derived runtime network/storage/service/UI foundations match the public Raider 0.5.2 source `36e4d96f8ed4933c108f942c5cd7a81da7bc840b` (public main differs only in version text/metadata). All development is isolated under Detector and its dedicated workflows; Raider and other app files/releases are unchanged.
- Microphone/camera evidence uses Windows CapabilityAccessManager selected-owner metadata with explicit availability and observation-time limits. It does not promise universal device detection or infer inactive state from missing records.
- Implementation, deterministic/native validation and release evidence are in progress. No published Detector binary or physical acceptance is claimed yet. Ubuntu/macOS remain placeholders; no new open-source reuse license is granted.

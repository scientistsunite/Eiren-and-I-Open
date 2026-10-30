# Cylon Detector — Public Project Memory

## 2026-10-30 — Owner-authorized Windows 0.1 implementation

- User explicitly authorized public reuse of Cylon Raider 0.5.2 source/assets/build files, an independent Windows x64 Detector app, PRs/squash merging and publication of 0.1 installer/source/checksums and related files. No private sources or private history are used.
- Core requirements: observable microphone/camera application access metadata; per-application one-second network measurements; always-on five-minute RAM prebuffer while monitoring; default event-triggered disk network detail with five-minute postroll; optional continuous detail; unified main SQLite log; separate configurable interval summaries; nonjudgmental anomaly observations at all times; local-only/no audio/video or payload capture; independent optional pre-sign-in service.
- Derived runtime network/storage/service/UI foundations match the public Raider 0.5.2 source `36e4d96f8ed4933c108f942c5cd7a81da7bc840b` (public main differs only in version text/metadata). All development is isolated under Detector and its dedicated workflows; Raider and other app files/releases are unchanged.
- Microphone/camera evidence uses Windows CapabilityAccessManager selected-owner metadata with explicit availability and observation-time limits. It does not promise universal device detection or infer inactive state from missing records.
- Windows implementation, dedicated tests and packaging passed [main Windows run 41064238823](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/41064238823) at `05b362909d5eb6714cad385c2b0eb77b94f372a2`. The [initial Windows x64 prerelease](https://github.com/scientistsunite/Eiren-and-I-Open/releases/tag/cylon-detector-windows-v0.1.0) is published; physical acceptance remains unclaimed. Ubuntu/macOS remain placeholders. Project-owned material follows the existing app-scoped MIT license, subject to third-party notices.

## Architecture and current verification limits

- **Device evidence:** Read only the configured owner's loaded CapabilityAccessManager registry scope. An unloaded hive, unsupported application/device path, malformed timestamps or stale metadata remains unavailable/uncertain evidence. No device is opened; no microphone samples, video, images or packet contents are acquired.
- **Network privacy:** While monitoring runs, keep the five-minute per-application network context in RAM with a bounded delivery overlap and a 128 MiB estimated-data budget. Default detailed persistence spans five minutes before an observed sensor change through the half-open five-minute tail after the last fresh active evidence; overlapping windows extend it. Stale/missing observations do not fabricate a stop or extend detail indefinitely. Resource/transport/clock gaps remain explicit. Continuous detail is a separate opt-in setting.
- **Main log and summaries:** Device observations, selected network detail, unfamiliar-network-application/rate-change indicators and lifecycle/coverage records use one Detector-specific main SQLite log. Minute-level aggregate context can be retained outside detailed capture windows for summaries. The separate configurable Reports folder contains interval CSV summaries and their internal report index. Summaries consume committed row IDs so delayed writes remain reportable, and distinguish temporal association from causation or threat findings.
- **Isolation:** Detector has independent installer/uninstaller, executable, mutex, pipe, service, startup and data identities. Foreign main/report databases are rejected. Optional pre-sign-in monitoring uses the configured owner's protected storage and sensor scope; that user's unloaded hive can prevent device evidence before sign-in.
- **Hosted Windows verification:** [Main run 41064238823](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/41064238823) passed all required suites, native close/restore, both source audits, license checks, self-contained x64 publishing, Inno installer compilation and the disposable monitoring-off installer fixture through cleanup. This is source-bound hosted evidence. No physical device, real ETW, boot/upgrade, long-duration, multi-monitor or actual-DPI acceptance is claimed.

## 2026-10-30 — Verified main build and initial publication

- **Source:** [`05b362909d5eb6714cad385c2b0eb77b94f372a2`](https://github.com/scientistsunite/Eiren-and-I-Open/commit/05b362909d5eb6714cad385c2b0eb77b94f372a2), squash-merged implementation PR #8. [Windows run 41064238823](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/41064238823) completed successfully.
- **Scope:** The installer fixture verified independent installation, normal responsive launch with monitoring off, full application exit, data-preserving uninstall, cleanup and read-only Raider isolation. It did not exercise real sensor/ETW capture or register the optional boot service. Both runtime source audits passed; the Detector privacy/identity audit inspected 51 source/project/manifest files.
- **Packaging:** The self-contained distribution includes the exact project MIT grant, full dependency license texts and resolved-package notice inventory for 20 dependencies. The scoped source ZIP separately verifies the project grant and the committed full upstream license texts.

| Suite / native probe | Passing assertions |
| --- | ---: |
| Measurement | 19 |
| History | 66 |
| Quality / provenance | 76 |
| Native settings / UI | 78 |
| Unified UX | 4,897 |
| Sensor metadata | 19 |
| Privacy windows | 921 |
| Requester identity | 10 |
| Summary reports | 21 |
| Separate native tray-close / restore | 61 |

- **Artifact:** `CylonDetector-0.1.0-initial-Windows-x64`, ID `12982522119`, 99,727,535 bytes. Artifact ZIP SHA256: `9c440edbddc17e6499f8e4e0c22c53b948c283bf45968e4d2f2ecd5717b640c5`. It contains installer, portable ZIP, scoped source ZIP, checksums and build/testing metadata.
- **Publication:** [Cylon Detector — Windows](https://github.com/scientistsunite/Eiren-and-I-Open/releases/tag/cylon-detector-windows-v0.1.0), release ID `429084349`, is public (`draft=false`, `prerelease=true`). [Publication run 41067141042](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/41067141042), job `124101401262`, passed all steps and verified all six final downloads. The lightweight `cylon-detector-windows-v0.1.0` tag points to the verified main source above.
- **Downloads:** [Windows x64 installer](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-detector-windows-v0.1.0/CylonDetector-Setup-0.1.0-initial.exe); [portable ZIP](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-detector-windows-v0.1.0/CylonDetector-Portable-0.1.0-initial-Windows-x64.zip); [scoped source/build recipe](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-detector-windows-v0.1.0/CylonDetector-Source-0.1.0-initial.zip); [release SHA256 manifest](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-detector-windows-v0.1.0/cylon-detector-windows-v0.1.0-initial-SHA256SUMS.txt). Installer, portable and source ZIP bytes match the pinned verified build. Original provenance/testing metadata bytes retain versioned public filenames; the release checksum manifest names those public files.
- **Preservation:** The publisher's before/after inventories matched for other releases, asset metadata and tag objects; it made no writes to those resources. The current publisher guard suite has 24 passing tests, including bounded read-only visibility checks added after the original 16 guards. Existing pinned release-note/build-validation counts were kept unchanged when resuming the verified draft. The curated receipt is maintained under `Release-Tools/v01/publication-receipt.json`.
- **Remaining acceptance:** This unsigned initial prerelease still requires physical microphone/camera, real ETW, boot/pre-sign-in, upgrade, long-duration, multi-monitor and actual display-DPI testing. Capability metadata and anomaly indicators do not prove hardware inactivity, causation or malicious behavior.
- **Independent public check:** Anonymous installer and source downloads matched the public SHA256 manifest. The seven complete license texts in the public source ZIP matched the source after Windows/Linux line-ending normalization. Other application source tree IDs also match the pre-Detector baseline.

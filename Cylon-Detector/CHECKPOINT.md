# Current status — 2026-10-30

The historical checkpoint below is preserved as a record, not current resume instructions. Implementation PR #8 was squash-merged as `05b362909d5eb6714cad385c2b0eb77b94f372a2`; [main Windows run 41064238823](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/41064238823) passed all required checks and the hosted installer fixture through cleanup. The [initial Windows x64 prerelease](https://github.com/scientistsunite/Eiren-and-I-Open/releases/tag/cylon-detector-windows-v0.1.0) is published; [publication run 41067141042](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/41067141042) verified all six final downloads and preservation inventories for other releases/assets/tags. The [installer](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-detector-windows-v0.1.0/CylonDetector-Setup-0.1.0-initial.exe), [source](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-detector-windows-v0.1.0/CylonDetector-Source-0.1.0-initial.zip) and [checksums](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-detector-windows-v0.1.0/cylon-detector-windows-v0.1.0-initial-SHA256SUMS.txt) are available. Current build evidence, counts and limitations are recorded in [PROJECT_MEMORY.md](PROJECT_MEMORY.md). Release pins and final publication receipts are maintained separately under `Release-Tools/v01/`.

---

# Safe development checkpoint — 2026-10-30

The user requested this checkpoint before the session usage limit. Resume the existing work; do not restart the implementation. No Detector release or tag has been published and no implementation PR has been merged yet.

## Authorization and scope

- The user authorized a new independently installable Windows x64 Cylon Detector 0.1, public reuse of Cylon Raider 0.5.2 components, PR creation, squash merging and publication of installer/source/checksums to `scientistsunite/Eiren-and-I-Open`.
- Preserve Raider and every other application's source, tags, releases and assets. Use public material only. Existing app-scoped MIT licensing and dependency notices apply.
- Work is on `cylon-detector-v01`, based on main `0b1a292d340e72a0d0852eb164975b02497bb736`. Read root AGENTS.md, Detector PROJECT_MEMORY.md, Windows README and testing policy/matrix before resuming.

## Implemented

- Independent app/collector/boot-service identities, installer, storage roots, local named pipes, startup entries and single-window UI derived from public Raider 0.5.2 foundations.
- Read-only selected-owner Windows CapabilityAccessManager microphone/webcam evidence. Explicit stale/unavailable/partial attribution and pre-sign-in hive gaps; no device, audio/video or packet-content capture.
- One-second application upload/download measurements. Always-maintained five-minute RAM context while monitoring, bounded delivery overlap, 128 MiB estimated-data budget and explicit gaps.
- Default sensor-triggered detail from five minutes before a change through five minutes after fresh active evidence; overlapping windows extend capture. Optional continuous detail. Device/network/anomaly/lifecycle observations share the Detector main database.
- Continuous nonthreat anomaly observations and minute-level network context. Separate configurable interval CSV summaries, durable committed-row cursors, late/backfill handling and before/during/after context without causal accusations.
- Independent database application IDs reject foreign logs/report indexes. Clear history clears RAM and familiarity state explicitly. No application-initiated networking, DNS, updater or telemetry; source guards cover listed APIs but are not runtime proof.
- Dark rounded UI with conspicuous monitoring state, activity timeline, inherited detailed network pages, summary/log settings and boot-service controls. Exact architecture and limitations are in PROJECT_MEMORY.md.

## Latest Windows evidence and saved fixes

- Last completed run: https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/40964915633 at `b5fc3281ee700bb28c3e4fe4493b3f2721b37a8f`; job `123766833195`. **Failed**, so not release eligible.
- Passed there: measurement 19, history 65, quality 76, native settings/UI 78, sensor 19, identity 10, summaries 21 and native tray-close/restore 61 assertions.
- Privacy reached all 921 assertions, then Windows cleanup failed because its final fixture SQLite connection used default pooling. Saved fix: `Pooling=false`; production connections already disable pooling. All 921 pass locally after the fix.
- Full UX failed at 200% font scaling because activity explanations consumed the timeline viewport. Saved fix: independently scrolling, bounded activity controls reserving list height. The original >=80px assertion remains; added long-explanation tests at all four font scales. Windows-target compilation passed; native execution of this fix is pending.
- Saved an additional history assertion that verifies the unified journal explicitly records recovered RAM context; current History suite has 66 locally passing assertions.
- Both source audits pass locally (51 runtime source/project/manifest files for Detector audit). Repository scaffold check passes (41 expected files). Publisher synthetic guard suite passes 16 tests.
- Packaging metadata now accurately distinguishes the hosted installer smoke fixture from physical-machine acceptance.

## Build and publication resume steps

1. Inspect the newest push run for branch `cylon-detector-v01` in `.github/workflows/cylon-detector-v01.yml`; this checkpoint push should trigger it. Review all nine suites, native tray-close, source guards, self-contained x64 compilation, Inno packaging and `installer_smoke.ps1`. Fix failures and rerun as needed. Do not publish a failed/partial run. Before the final packaging run, verify full license texts are actually shipped: current scoped source export selects `Cylon-Detector/Windows` and the workflow, but the app-scoped MIT text is at `Cylon-Detector/LICENSE`; explicitly include that license in the source ZIP and installed/portable payload, and inspect dependency notice inclusion rather than relying only on reference URLs.
2. The installer smoke test runs only on disposable hosted Windows, with monitoring off: fresh install, normal responsive UI launch, full exit, data-preserving uninstall and read-only Raider isolation checks. It does not test real sensors/ETW, boot startup, upgrade or firewall-blocked runtime. Physical acceptance remains unclaimed.
3. Once branch gates pass, open the implementation PR and squash merge as authorized. Main is configured to run the dedicated Windows build on Detector changes. Prefer the successful post-merge main run and its exact source SHA for the final release pins.
4. Artifact name: `CylonDetector-0.1.0-initial-Windows-x64`. It contains installer, portable ZIP, scoped source ZIP, BUILD-PROVENANCE.json, READ-ME-FIRST.txt and SHA256SUMS.txt. Record actual run counts, artifact ID/size/digest and printed payload SHA256 values. Do not infer success from artifact existence alone.
5. Guarded publisher and its 16 tests are saved under `Release-Tools/v01/`; workflow `.github/workflows/cylon-detector-v01-release.yml` only publishes on the owner's push to **`cylon-detector-v01-release`**. This development branch cannot trigger publication. **pins.json is deliberately absent** until a verified build exists. See publisher README for its exact schema and procedure.
6. Create that release branch from reviewed main, add exact verified pins and source/run evidence, review, then push. Publisher verifies origin/provenance/archive/payload checksums, creates only `cylon-detector-windows-v0.1.0`, stages six assets in a draft prerelease, verifies downloads and other release/tag inventories, then publishes **Cylon Detector — Windows**. It refuses overwrite/collision states. No new permission is needed for this already authorized scoped action.
7. Verify public downloads independently, retain a curated public receipt, update Detector memory/README and its shared test-matrix row with exact evidence, and squash merge release-tooling/docs changes as appropriate. Final response should link the installer and release and disclose limited Windows device observability and unsigned initial-build status.

## Workspace and tool notes

- Local repo: `/workspace/Eiren-and-I-Open`. SDK: `/workspace/work/dotnet/dotnet`; `DOTNET_CLI_HOME=/workspace/work/dotnet-cli`, `NUGET_PACKAGES=/workspace/work/nuget`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`. Local .NET mutex needs the existing elevated execution approval; native WinForms tests run on Windows CI.
- GitHub connector fetch can read approved API URLs, workflow jobs/logs and artifact metadata. Parse fetch `structuredContent.content` as JSON. Local direct api.github.com access is restricted; use the connector and dedicated GitHub Actions publication workflow.
- The untracked `Cylon-Raider/Release-Tools/v052/` is unrelated pre-existing material. Leave it untouched and exclude it from commits.
- No credentials, personal device logs, audio/video or private material belong in this checkpoint or publication. Generated caches, binaries, database files and synthetic preview images are not source commits.

# Public Test and Platform Matrix

As of 2026-10-29, most directories remain **placeholders**. BCC Windows is experimental **source only**, not certified runtime or hardware support.

| Application | Windows | Ubuntu | macOS |
| --- | --- | --- | --- |
| Archive Reader | Placeholder | Placeholder | Placeholder |
| Battery Charge Control | Experimental source; historical simulation/CI-fixture results only, **public revision not yet verified** | Placeholder | Placeholder |
| Silent Meteor | Placeholder | Placeholder | Placeholder |
| System Chronicle | Placeholder | Placeholder | Placeholder |
| Cylon Raider | Placeholder | Placeholder | Placeholder |
| Cylon Detector | Windows 0.1 [hosted CI verified](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/41064238823), including isolated installer smoke; [initial prerelease published](https://github.com/scientistsunite/Eiren-and-I-Open/releases/tag/cylon-detector-windows-v0.1.0); physical acceptance not claimed | Placeholder | Placeholder |

## Repository-level baseline

| Check | Purpose | Command |
| --- | --- | --- |
| Scaffold layout | Checks required public placeholder files are present and nonempty | `python3 testing/scripts/verify_structure.py` |

Do not mark app tests or releases passed until there is a documented run against public implementation, with date, commit, platform, and limitations.

## BCC Windows history and next public gate

The [BCC Windows evidence summary](../Battery-Charge-Control/Windows/README.md) records prior synthetic simulation and a separately isolated installer fixture's results. They were **not executed for this public commit**. Run and cite a public-commit-bound workflow before changing this row to public CI verified. Production GUI, firmware, service and physical acceptance remain Not Tested.

## Cylon Detector Windows 0.1

The [Detector Windows documentation](../Cylon-Detector/Windows/README.md) describes the implemented metadata, network-window, history, summary and independent-service scope. The dedicated `cylon-detector-v01.yml` workflow checks measurement, history, quality/provenance, native settings/UI, sensor decoding, privacy windows, identity isolation and summary behavior, plus source audits and installer packaging. The [complete main run 41064238823](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/41064238823) passed at source [`05b362909d5eb6714cad385c2b0eb77b94f372a2`](https://github.com/scientistsunite/Eiren-and-I-Open/commit/05b362909d5eb6714cad385c2b0eb77b94f372a2). Passing assertions: measurement 19, history 66, quality/provenance 76, native settings/UI 78, unified UX 4,897, sensor 19, privacy 921, identity 10, reports 21 and a separate native tray-close/restore probe 61. Both source guards passed (51 runtime source/project/manifest files in the Detector audit), dependency notice packaging covered 20 resolved dependencies, and the disposable hosted installer fixture passed through cleanup with monitoring off. The [initial Windows x64 prerelease](https://github.com/scientistsunite/Eiren-and-I-Open/releases/tag/cylon-detector-windows-v0.1.0) is public. [Publication run 41067141042](https://github.com/scientistsunite/Eiren-and-I-Open/actions/runs/41067141042) passed 24 publisher guards, verified all six final downloads and confirmed matching other-release/asset/tag preservation inventories. The [installer](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-detector-windows-v0.1.0/CylonDetector-Setup-0.1.0-initial.exe), [source](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-detector-windows-v0.1.0/CylonDetector-Source-0.1.0-initial.zip) and [SHA256 manifest](https://github.com/scientistsunite/Eiren-and-I-Open/releases/download/cylon-detector-windows-v0.1.0/cylon-detector-windows-v0.1.0-initial-SHA256SUMS.txt) are available without treating these hosted checks as physical acceptance.

Physical microphone/camera observation coverage, real ETW behavior, boot/pre-sign-in operation, upgrade, long-duration use, actual display DPI and multiple monitors remain unverified for Detector. Capability-use metadata is incomplete evidence and is not proof of hardware activity or inactivity.

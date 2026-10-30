# Public Test and Platform Matrix

As of 2026-10-29, most directories remain **placeholders**. BCC Windows is experimental **source only**, not certified runtime or hardware support.

| Application | Windows | Ubuntu | macOS |
| --- | --- | --- | --- |
| Archive Reader | Placeholder | Placeholder | Placeholder |
| Battery Charge Control | Experimental source; historical simulation/CI-fixture results only, **public revision not yet verified** | Placeholder | Placeholder |
| Silent Meteor | Placeholder | Placeholder | Placeholder |
| System Chronicle | Placeholder | Placeholder | Placeholder |
| Cylon Raider | Placeholder | Placeholder | Placeholder |
| Cylon Detector | Windows 0.1 source and dedicated validation/packaging; final complete public CI run pending; physical acceptance not claimed | Placeholder | Placeholder |

## Repository-level baseline

| Check | Purpose | Command |
| --- | --- | --- |
| Scaffold layout | Checks required public placeholder files are present and nonempty | `python3 testing/scripts/verify_structure.py` |

Do not mark app tests or releases passed until there is a documented run against public implementation, with date, commit, platform, and limitations.

## BCC Windows history and next public gate

The [BCC Windows evidence summary](../Battery-Charge-Control/Windows/README.md) records prior synthetic simulation and a separately isolated installer fixture's results. They were **not executed for this public commit**. Run and cite a public-commit-bound workflow before changing this row to public CI verified. Production GUI, firmware, service and physical acceptance remain Not Tested.

## Cylon Detector Windows 0.1

The [Detector Windows documentation](../Cylon-Detector/Windows/README.md) describes the implemented metadata, network-window, history, summary and independent-service scope. The dedicated `cylon-detector-v01.yml` workflow checks measurement, history, quality/provenance, native settings/UI, sensor decoding, privacy windows, identity isolation and summary behavior, plus source audits and installer packaging. Complete source-bound Windows acceptance and the hosted installer smoke result remain pending; partial or local checks are not a completed release gate.

Physical microphone/camera observation coverage, real ETW behavior, boot/pre-sign-in operation, upgrade, long-duration use, actual display DPI and multiple monitors remain unverified for Detector. Capability-use metadata is incomplete evidence and is not proof of hardware activity or inactivity.

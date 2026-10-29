# Eiren & I Open

Public home for Eiren & I software projects. **Purpose and availability are different:** some applications have public source, some have downloadable releases without the matching source trees here, and some remain concepts.

## Applications and intended uses

| Application | Intended use | Public repository status |
| --- | --- | --- |
| [Archive Reader](Archive-Reader/) | Browse and search locally exported ChatGPT conversation archives, including large histories and paginated results. | Windows and Ubuntu packages in [Releases](https://github.com/scientistsunite/Eiren-and-I-Open/releases); source tree remains a placeholder. |
| [Battery Charge Control](Battery-Charge-Control/) | Monitor laptop battery state and, on explicitly supported devices, control charging thresholds with restoration safeguards. | Experimental [Windows v0.1 source](Battery-Charge-Control/Windows/) is public; Ubuntu installer archive is in Releases, but its source is not in this tree. **No unverified firmware execution.** |
| [Silent Meteor](Silent-Meteor/) | Customize idle-display/screensaver visuals with meteor animations and supported images or videos. | Windows and Ubuntu releases; source tree remains a placeholder. |
| [System Chronicle](System-Chronicle/) | Record local performance and thermal readings to investigate heat, load, and resource-use trends. | Windows and Ubuntu releases; source tree remains a placeholder. |
| [Cylon Raider](Cylon-Raider/) | Observe per-application network traffic, activity history and reports while keeping the monitor itself network-isolated. | [Windows source and prerelease tooling](Cylon-Raider/Windows/) are public, as are Windows prerelease downloads. |
| [Cylon Detector](Cylon-Detector/) | Review observable microphone/camera application-use metadata, correlated network activity and unusual-behavior indicators, without recording device contents or declaring malware. | [Windows 0.1 source and build tooling](Cylon-Detector/Windows/) are implemented; complete Windows CI and initial release publication are pending. |

Having a release download does not mean corresponding source is included in the current `main` tree, or that the application is production-certified. Consult individual app READMEs and the [Releases page](https://github.com/scientistsunite/Eiren-and-I-Open/releases) for exact features, assets, checksums and limitations.

## Licensing

Each of the six application directories has its own **MIT License**, using identical terms, for project-owned source, documentation and original material in that directory unless separately stated. Battery Charge Control Windows also retains its existing, identical [Windows-specific LICENSE](Battery-Charge-Control/Windows/LICENSE). These app-scoped licenses do not automatically grant rights to third-party material or relicense earlier uploaded release archives/binaries; those retain applicable accompanying notices. Shared top-level scripts and documentation are not automatically covered by any application-scoped license.

Public visibility alone is not an additional reuse grant. New releases must still review provenance, dependencies, third-party notices and platform-specific testing.

## Shared development

- [AGENTS.md](AGENTS.md) — instructions and public-only safeguards.
- [SHARED_MEMORY.md](SHARED_MEMORY.md) and [MEMORY_SYNC.md](MEMORY_SYNC.md) — public decisions.
- [testing/](testing/) — tests, matrix, fixtures and shared policy.
- [.github/workflows/](.github/workflows/) — public CI.
- [scripts/](scripts/) — reviewable public release-note maintenance.

All branches, pull requests and workflows here are public. Do not import private Git history, personal data, credentials, recordings or unreviewed assets.

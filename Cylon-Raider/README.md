# Cylon Raider

**Intended use:** Understand which Windows applications send and receive network traffic, inspect live activity, compare observed applications and examine local history and reports. The intended security boundary is that Cylon Raider observes traffic using local operating-system facilities without initiating its own network communications. It is an observability tool, not a firewall or a promise of packet-content inspection.

**Public status:** Windows source, tests and build/release tooling are present in [Windows](Windows/), with Windows installer, portable and version-specific source downloads in [Releases](https://github.com/scientistsunite/Eiren-and-I-Open/releases). Some physical-device, ETW, boot-service and installer acceptance testing remains outstanding; see release-specific evidence. Ubuntu/macOS source directories are placeholders.

## Reserved platforms

- [Windows](Windows/) — public Windows source and prerelease distributions.
- [Ubuntu](Ubuntu/) — placeholder, not a support commitment.
- [macOS](macOS/) — placeholder, not a support commitment.

## Licensing and development

Project-owned source and original project material in this directory are available under the [MIT License](LICENSE), subject to [third-party distribution notices](Windows/docs/THIRD-PARTY.md) and any material-specific restrictions. The [repository-wide MIT grant](../LICENSING.md) expressly covers project-owned material in existing downloads; the downloads, their checksums and third-party notices remain unchanged.

See [project memory](PROJECT_MEMORY.md) and the [shared testing policy](../testing/TEST_POLICY.md).

# Shared Testing Policy

Initial cross-project standards; no application test claims are made by this document.

## Principles

1. **Accurate evidence:** label scaffold, unit, contract, integration, end-to-end, security/privacy, and performance tests separately. Record exact commands, platforms, and limitations.
2. **Synthetic fixtures:** never publish real user data, recordings, private logs, secrets, credentials, identifiers, or proprietary fixtures.
3. **Isolation:** use disposable test environments for installation, system changes, privilege elevation, or destructive cases.
4. **Least privilege:** routine CI must not alter real firmware, firewall rules, boot settings, host devices, or production data.
5. **No unexpected networking:** tests and applications must document expected network behavior. Do not add telemetry or background network access implicitly.
6. **Lifecycle coverage:** where applicable, cover fresh install, update, repair, reboot/startup, rollback, uninstall, and preservation of user data.
7. **Reliability:** where relevant, test bounded CPU/memory/disk/network use, error handling, recovery, and long-running behavior.
8. **Separate platforms:** do not infer Windows, Ubuntu, or macOS support from a shared directory placeholder.
9. **Secure publication:** independently review licensing, credentials, dependency provenance, and potentially sensitive content before pushing.
10. **Traceability:** update `TEST_MATRIX.md` and application memory alongside actual verification, linking the commit or CI run.

## Adding real tests

Add app/OS tests beside approved public source and genuinely shared helpers under `testing/`. Document dependencies, setup, execution, side effects, and cleanup. Do not require private infrastructure or privileged CI credentials by default.

# Battery Charge Control — Public Project Memory

Updated: 29 October 2026, initial Windows source-publication proposal.

## Scope and state

- Windows v0.1 is experimental, source-only and not approved for production execution or physical charging control.
- The Windows source includes an ordinary-user GUI and a separately privileged ASUS controller candidate, source-level installer/maintenance logic and a no-firmware simulation suite. Other manufacturer controllers are not implemented.
- Windows test evidence inherited as **historical descriptive summary**: managed simulations, CI production compilation (not execution), and Milestone 5 no-firmware installer-fixture lifecycle on Server 2025 x64/Windows 11 ARM64. Do not relabel it as public-revision CI evidence.
- Windows 11 x64 desktop, production GUI/installer/service, and real ASUS compatibility/physical cutoff have not passed acceptance. Milestone 6 is still a plan.
- Ubuntu/macOS public folders remain placeholders.

## Safety invariant

The original limit must be observed and durably preserved before a firmware write; recovery obligation must be durable before applying control; failure must preserve retry obligations; altered BIOS/device identity or unverified readback must block unsafe requests. Shutdown/alternate-OS recovery is best-effort and cannot be guaranteed. Acknowledgment is not actual charging cessation.

## Publishing discipline

Use a fresh public-only Git history. Never copy internal project memories, original workflow approvals, temporary user information, runner logs, private files or executable artifacts automatically. Review the source snapshot and its license before exposing a public branch/PR. Record the public Git commit, public CI run and separately verified results in this memory after they occur. Public prereleases should remain source-only until a separate binary-safety decision.

## Next

Reproduce compile/source/simulation checks against the public commit; independently validate desktop synthetic preview D01–D09; then design a separately approved native/firmware verification path. See [Windows README](Windows/README.md) for detailed completed and planned milestones.

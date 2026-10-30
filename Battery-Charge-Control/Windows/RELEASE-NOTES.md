# Battery Charge Control — Windows v0.1.0-experimental.1

**Experimental, source-only prerelease. No executable installer or firmware-control binary is attached.**

This first public snapshot is for transparent, collaborative development. It contains the selected Windows .NET/C#/PowerShell/Python/Inno source and synthetic test harness, plus a SHA-256 source manifest. It does not import internal Git history or other applications.

Historically, the Windows implementation compiled in CI, a 145-case managed synthetic simulation suite passed, and a separate no-firmware installer lifecycle fixture completed 10/10 checks on each of Server 2025 x64 and Windows 11 ARM64. Two earlier fixture attempts failed during profile initialization and were corrected. The production GUI, installer, service, Windows 11 x64 desktop preview and physical ASUS charging/restoration **have not been validated**. These past test outcomes are not fresh validation of the public commit.

**Do not use the production program to set a physical charging limit.** Hardware or firmware testing requires a separate, model-specific, explicitly approved safety protocol. Read the included `Battery-Charge-Control/Windows/README.md` and `LICENSE` before using or adapting the source.

The attached Windows-only source ZIP and its SHA-256 checksum are the intended archive. GitHub's automatically generated source archives cover the **whole public monorepo** at the tag (never a private repository).

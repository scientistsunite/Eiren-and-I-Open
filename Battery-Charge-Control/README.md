# Battery Charge Control

**Intended use:** Give laptop owners visibility into battery state and, on specifically supported models, a way to select charging thresholds. The design emphasizes original-setting readback, restoration safeguards and warnings about charge limits that can persist after rebooting into another operating system.

**Public implementation:** [Windows v0.1 experimental source preview](Windows/README.md), covered by the matching [Windows-specific MIT license](Windows/LICENSE) and [application MIT license](LICENSE). Synthetic tests and CI compilation do **not** establish that its installer, service or ASUS firmware charging control is safe for real hardware. **Do not execute unverified firmware-changing functionality.**

**Other distributions:** An Ubuntu 5-point-offset source-based installer archive is available through [Releases](https://github.com/scientistsunite/Eiren-and-I-Open/releases). That Ubuntu implementation has not been imported into this Git tree. The Ubuntu/macOS folders remain placeholders; past downloaded packages retain their original included notices.

See [public project memory](PROJECT_MEMORY.md), the [test matrix](../testing/TEST_MATRIX.md) and the [testing policy](../testing/TEST_POLICY.md). Firmware writes require independent device-specific review and explicit approval.

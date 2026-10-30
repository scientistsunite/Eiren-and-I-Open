# Agent Instructions — Eiren & I Open

Applies to this entire **public** repository.

## Public-only boundary

- Work within `scientistsunite/Eiren-and-I-Open` using newly authored material or explicitly approved public sources.
- Do not open, clone, connect to, fetch, copy, adapt, or import private repository files, Git history, project memory, logs, or assets without separate approval for the exact material and a publication review.
- Do not publish tokens, credentials, personal data, private URLs, recordings, internal build logs, or secrets in commits, PRs, issues, workflows, test fixtures, or artifacts.
- **Public branches and pull requests are public immediately.** Deleting sensitive content later cannot guarantee its removal from copies.

## Engineering practices

1. Read `README.md`, `SHARED_MEMORY.md`, the relevant application's `PROJECT_MEMORY.md`, and `testing/TEST_POLICY.md` before changes.
2. Inspect Git status. Preserve user work; avoid destructive resets or overwrites. Use branches and Git worktrees when useful.
3. Prefer minimal dependencies, reproducible checks, least privilege, and no unexplained network access.
4. For each substantive change, document scope, platform, build/test evidence, limitations, and relevant decisions.
5. Review content and history for publication suitability **before pushing**. Never claim a placeholder is working software.
6. Do not release packages, deploy, merge, or transfer private materials without authorization.
7. Keep public memory updated using `MEMORY_SYNC.md`, but do not synchronize private memory wholesale.

## Testing

- Run `python3 testing/scripts/verify_structure.py` for scaffold verification.
- Consult `testing/TEST_MATRIX.md` for verified per-platform status.
- The scaffold check is **not** a functional test, security scan, secret scan, or assurance of privacy.
- Introduce app-specific tests and dependencies only when reviewed source has been added.

## Initial state

Initial scaffolding had only placeholders. BCC Windows now has a reviewed experimental **source-only** implementation with an application-scoped license; no executable, hardware acceptance, or production support is implied. Cylon Raider Windows also has public prerelease source and build tooling. Some other app families have historical releases even though their Git trees remain placeholders. Each of the six application directories now has an app-scoped MIT license for project-owned material, subject to third-party and older-release rights.

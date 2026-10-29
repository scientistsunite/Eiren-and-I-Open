# Shared Testing Infrastructure

**Current scope:** public-only testing guidance and a lightweight, dependency-free scaffold check. No app test suite, build job, installer test, or production fixture exists yet.

- [TEST_POLICY.md](TEST_POLICY.md) — shared requirements, isolation, reliability, and privacy.
- [TEST_MATRIX.md](TEST_MATRIX.md) — public test and platform status.
- [scripts/](scripts/) — reusable scripts.
- [fixtures/](fixtures/) — guidance for synthetic fixtures.
- [../.github/workflows/](../.github/workflows/) — minimal CI workflow.

Run from the repository root:

```sh
python3 testing/scripts/verify_structure.py
```

The script only checks that the expected placeholder files exist and are nonempty. It does **not** check application behavior, secrets, Git history, security, or safe publication. Add platform-specific tests when reviewed applications are introduced.

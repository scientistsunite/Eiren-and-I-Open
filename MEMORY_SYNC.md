# Public Project Memory — Sync Procedure

All project-memory files in Eiren & I Open are **public documentation**. They do not synchronize with private repositories or personal memory stores.

## Ownership

- `SHARED_MEMORY.md`: cross-application decisions, shared tools, public release status, unresolved questions.
- `<App>/PROJECT_MEMORY.md`: public scope, decisions, platform status, build/test evidence, and follow-ups for one app.
- `AGENTS.md`: standing guidance for agents and contributors.
- `testing/TEST_MATRIX.md`: evidence-based public test status.

## Workflow

1. Read relevant public memory and current repository state before implementing.
2. Include memory changes with the same reviewed PR or commit as significant design/configuration changes.
3. Record **date, decision, rationale, affected scope, verification, and follow-up**.
4. Label test outcomes precisely: not run, passed, failed, or not applicable, with platform and commands/run links.
5. Resolve stale or conflicting public notes against current verified repository state.
6. Before committing, review memory text as public disclosure: exclude private materials, credentials, internal history, identifiable logs, unpublished details, and private links.
7. Never copy or automatically synchronize private repository memory into these files.

## Public decision entry template

```md
### YYYY-MM-DD — Title
- Decision:
- Reason:
- Scope:
- Verification:
- Follow-up:
```

An empty app template or platform folder does not constitute a tested or supported release.

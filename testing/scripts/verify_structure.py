#!/usr/bin/env python3
"""Dependency-free public scaffold layout check (not a security/privacy test)."""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
APPS = (
    "Archive-Reader",
    "Battery-Charge-Control",
    "Silent-Meteor",
    "System-Chronicle",
    "Cylon-Raider",
    "Cylon-Detector",
)
PLATFORMS = ("Windows", "Ubuntu", "macOS")
REQUIRED = [
    "README.md", "AGENTS.md", "SHARED_MEMORY.md", "MEMORY_SYNC.md",
    "testing/README.md", "testing/TEST_POLICY.md", "testing/TEST_MATRIX.md",
    "testing/fixtures/README.md", "testing/scripts/README.md",
    "testing/scripts/verify_structure.py", ".github/workflows/public-scaffold.yml",
]
for app in APPS:
    REQUIRED.extend((f"{app}/README.md", f"{app}/PROJECT_MEMORY.md"))
    REQUIRED.extend(f"{app}/{os_name}/README.md" for os_name in PLATFORMS)

missing = [p for p in REQUIRED if not (ROOT / p).is_file()]
empty = [p for p in REQUIRED if (ROOT / p).is_file() and (ROOT / p).stat().st_size == 0]
if missing or empty:
    if missing:
        print("Missing files:", *missing, sep="\n- ", file=sys.stderr)
    if empty:
        print("Empty files:", *empty, sep="\n- ", file=sys.stderr)
    sys.exit(1)
print(f"PASS: {len(REQUIRED)} expected public scaffold files exist and are nonempty.")
print("Layout only: not a functional, security, secret, or privacy test.")

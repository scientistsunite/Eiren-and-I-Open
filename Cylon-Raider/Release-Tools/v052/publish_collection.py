#!/usr/bin/env python3
"""Append the reviewed v0.5.2 build using the existing verified publisher guards."""
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("raider_collection_engine", ROOT.parent / "v05" / "publish_collection.py")
engine = importlib.util.module_from_spec(spec)
spec.loader.exec_module(engine)


def configure(config):
    for key in ("SOURCE_SHA", "RUN_ID", "ARTIFACT_ID", "ARTIFACT_NAME", "ARTIFACT_SHA", "ARTIFACT_SIZE", "PINNED_PAYLOADS"):
        setattr(engine, key, config[key])
    engine.RUN_URL = f"https://github.com/{engine.REPOSITORY}/actions/runs/{engine.RUN_ID}"
    engine.VERSION = "0.5.2-prerelease"
    engine.BUILD_WORKFLOW = ".github/workflows/cylon-raider-v052-prerelease.yml"
    engine.PUBLISH_REF = "refs/heads/cylon-raider-v052-release-addition"
    engine.PROVENANCE = "CylonRaider-0.5.2-prerelease-BUILD-PROVENANCE.json"
    engine.README = "CylonRaider-0.5.2-prerelease-READ-ME-FIRST.txt"
    engine.CHECKSUMS = "cylon-raider-windows-v0.5.2-prerelease-SHA256SUMS.txt"
    engine.RENAMES = {"BUILD-PROVENANCE.json": engine.PROVENANCE, "READ-ME-FIRST.txt": engine.README}
    engine.ORIGINAL_FILES = set(engine.PINNED_PAYLOADS) | set(engine.RENAMES) | {"SHA256SUMS.txt"}
    engine.NEW_FILES = set(engine.PINNED_PAYLOADS) | {engine.PROVENANCE, engine.README, engine.CHECKSUMS}
    baseline = json.loads((ROOT / "baseline.json").read_text())
    engine.ORIGINAL_BODY = (ROOT / "previous-publication-notes.md").read_text(encoding="utf-8")
    engine.BASELINE = {asset["name"]: (asset["id"], asset["size"], asset["digest"].removeprefix("sha256:")) for asset in baseline["assets"]}
    desired = (ROOT / "release-notes.md").read_text()
    engine.build_body = lambda: desired


if __name__ == "__main__":
    configure(json.loads((ROOT / "verified-build.json").read_text()))
    raise SystemExit(engine.main())

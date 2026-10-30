"""Preserve restored public dependency license/notice files in the shipped payload.

NuGet license expressions do not cause license texts to be copied by publish.
Committed docs/licenses supplies those expressions' full texts. This adds exact
package/runtime notices and records resolved versions without local machine paths.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZipFile


def notice_files(directory: Path) -> list[Path]:
    files = {
        path for path in directory.iterdir()
        if path.is_file() and any(part in path.name.lower() for part in ("license", "notice", "copyright"))
    }
    for nuspec in directory.glob("*.nuspec"):
        for license_element in ET.parse(nuspec).iter():
            if license_element.tag.rsplit("}", 1)[-1] == "license" and license_element.get("type") == "file":
                path = (directory / (license_element.text or "")).resolve()
                if not path.is_relative_to(directory.resolve()) or not path.is_file():
                    raise RuntimeError("A restored package's declared license file is unavailable or escapes the package")
                files.add(path)
    return sorted(files)


def metadata(directory: Path) -> dict:
    result = {}
    for nuspec in directory.glob("*.nuspec"):
        for item in ET.parse(nuspec).iter():
            name = item.tag.rsplit("}", 1)[-1]
            if name in ("license", "copyright"):
                result[name] = (item.text or "").strip()
                if name == "license":
                    result["license_type"] = item.get("type")
    return result


def collect(app_root: Path, payload: Path) -> dict:
    expected_license = (app_root.parent / "LICENSE").read_bytes()
    if (payload / "LICENSE").read_bytes() != expected_license:
        raise RuntimeError("Payload project LICENSE differs from the app's exact MIT grant")
    output = payload / "docs" / "licenses" / "restored"
    output.mkdir(parents=True, exist_ok=True)
    packages = {}
    roots = set()
    requested_runtime = set()
    for assets_file in sorted((app_root / "src").glob("*/obj/project.assets.json")):
        assets = json.loads(assets_file.read_text(encoding="utf-8-sig"))
        roots.update(Path(root) for root in assets.get("packageFolders", {}))
        for name, value in assets.get("libraries", {}).items():
            if value.get("type") == "package":
                identity, version = name.rsplit("/", 1)
                packages[(identity, version)] = value.get("path", f"{identity.lower()}/{version}")
        for framework in assets.get("project", {}).get("frameworks", {}).values():
            for dependency in framework.get("downloadDependencies", []):
                identity = dependency["name"]
                if identity.lower().startswith(("microsoft.netcore.app.runtime.", "microsoft.windowsdesktop.app.runtime.")):
                    bounds = [bound.strip() for bound in dependency["version"].strip("[]()").split(",")]
                    if not bounds[0] or len(set(bounds)) != 1:
                        raise RuntimeError("Runtime pack notice collection requires an exact restored version")
                    version = bounds[0]
                    requested_runtime.add((identity, version))
                    packages[(identity, version)] = f"{identity.lower()}/{version}"
    if not packages:
        raise RuntimeError("No restored package inventory found; publish all three applications before collecting notices")
    manifest = []
    for (identity, version), relative in sorted(packages.items()):
        directory = next((root / relative for root in sorted(roots) if (root / relative).is_dir()), None)
        if directory is None:
            raise RuntimeError(f"Resolved package is unavailable for notice collection: {identity}/{version}")
        target = output / re.sub(r"[^A-Za-z0-9._-]", "_", identity) / version
        target.mkdir(parents=True, exist_ok=True)
        copied = []
        for source in notice_files(directory):
            # Package directories are separate, and nested license paths remain nested.
            destination = target / source.relative_to(directory)
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, destination)
            copied.append({"file": destination.relative_to(payload).as_posix(), "sha256": hashlib.sha256(source.read_bytes()).hexdigest()})
        if (identity, version) in requested_runtime:
            if not any("license" in entry["file"].lower() for entry in copied):
                raise RuntimeError(f"Self-contained runtime pack has no license text: {identity}/{version}")
            if identity.lower().startswith("microsoft.netcore.app.runtime.") and not any("notice" in entry["file"].lower() for entry in copied):
                raise RuntimeError(f"Self-contained .NET runtime pack has no third-party notice text: {identity}/{version}")
        manifest.append({"package": identity, "version": version, **metadata(directory), "included_notice_files": copied})
    # Preserve runtime root notices too; do not overwrite their original filenames.
    runtime_root = output / "published-runtime-root"
    for source in notice_files(payload):
        if source.name == "LICENSE":
            continue
        runtime_root.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, runtime_root / source.name)
    result = {"scope": "Resolved public runtime dependencies; build/test-only packages excluded", "packages": manifest}
    (output.parent / "DEPENDENCIES.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return result


def verify_source_zip(app_root: Path, archive: Path) -> None:
    expected = {"Cylon-Detector/LICENSE": app_root.parent / "LICENSE"}
    expected.update({f"Cylon-Detector/Windows/docs/licenses/{path.name}": path for path in (app_root / "docs/licenses").glob("*.txt")})
    if len(expected) < 7:
        raise RuntimeError("Expected full upstream license texts are missing from source docs")
    with ZipFile(archive) as package:
        for name, source in expected.items():
            if package.read(name) != source.read_bytes():
                raise RuntimeError(f"Scoped source ZIP license differs from reviewed source: {name}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--payload", type=Path)
    mode.add_argument("--source-zip", type=Path)
    args = parser.parse_args()
    app_root = Path(__file__).resolve().parents[2]
    if args.payload:
        inventory = collect(app_root, args.payload.resolve())
        print(f"PASS exact project MIT grant and resolved dependency notices included ({len(inventory['packages'])} packages)")
    else:
        verify_source_zip(app_root, args.source_zip)
        print("PASS scoped source ZIP includes exact project MIT grant and full reviewed upstream license texts")

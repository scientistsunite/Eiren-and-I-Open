#!/usr/bin/env python3
"""Append the pinned v0.5 build to one owner-authorized release collection.

No application code or downloaded program is executed. Existing assets and tags
are never overwritten, deleted or moved. --publish is required for mutations.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import sys
import urllib.error
import urllib.parse
import urllib.request
import zipfile

REPOSITORY = "scientistsunite/Eiren-and-I-Open"
REPOSITORY_ID = 1438380905
RELEASE_ID = 427955131
TAG = "cylon-raider-windows-v0.4.0"
TAG_SHA = "3f15a615e9cc1a09692abbdb8d4a183eadccf43e"
TITLE = "Cylon Raider — Windows"
SOURCE_SHA = "b82a8d0426a78bbff04608b2a26d9d26273a9765"
RUN_ID = 40903515615
RUN_URL = f"https://github.com/{REPOSITORY}/actions/runs/{RUN_ID}"
VERSION = "0.5.0-preliminary"
BUILD_WORKFLOW = ".github/workflows/cylon-raider-v05-preliminary.yml"
PUBLISH_REF = "refs/heads/cylon-raider-v05-release-addition"
ARTIFACT_ID = 12910193270
ARTIFACT_NAME = "CylonRaider-0.5.0-preliminary-Windows-x64"
ARTIFACT_SHA = "0b34789e4669ba9a01304e31825529c6829df0aa70677c50c075b616a74fedea"
ARTIFACT_SIZE = 99541708
BASELINE = {
    "cylon-raider-windows-v0.4.0-SHA256SUMS.txt": (688782966, 201, "0be3dd380cab8f74f21a2ceff67da0f5cac56b23c26ffb855dfb81315d3cf4cd"),
    "CylonRaider-Setup-0.4.0.exe": (688782967, 42118460, "5c1ab0cd34089ac5a16c51e1be1550b95c3ca8e6013fff198e23e3bdc6098c99"),
    "CylonRaider-Windows-0.4.0-Portable.zip": (688782968, 56894200, "276706218a74de547daf7c727c4cb00f34c152da3020fd36d223e418cc099ddd"),
}
PINNED_PAYLOADS = {
    "CylonRaider-Setup-0.5.0-preliminary.exe": "03d28df0802e904d8b336d43fafdb3e6d0d83c27d942c895cbd1ab1649ab1fe9",
    "CylonRaider-Portable-0.5.0-preliminary-Windows-x64.zip": "8da0c870f50b97dcf6e31cc21ef7f8e1b16b07be8fe3c3b74ba1bebb7dc5f41f",
    "CylonRaider-Source-0.5.0-preliminary.zip": "2be93ed484b3ba678aaebc51f6f0ef5f74b38dd37843782d5cf580b74ed85a18",
}
PROVENANCE = "CylonRaider-0.5.0-preliminary-BUILD-PROVENANCE.json"
README = "CylonRaider-0.5.0-preliminary-READ-ME-FIRST.txt"
CHECKSUMS = "cylon-raider-windows-v0.5.0-preliminary-SHA256SUMS.txt"
RENAMES = {"BUILD-PROVENANCE.json": PROVENANCE, "READ-ME-FIRST.txt": README}
ORIGINAL_FILES = set(PINNED_PAYLOADS) | set(RENAMES) | {"SHA256SUMS.txt"}
NEW_FILES = set(PINNED_PAYLOADS) | {PROVENANCE, README, CHECKSUMS}
ORIGINAL_BODY = "# Cylon Raider — Windows 0.4.0\n\nPRERELEASE: sequential Windows VM validation passed, but physical-machine acceptance testing was still outstanding when this package was selected.\n\nPackage source: individually selected release assets, checked locally before public upload.\nSHA-256 checksums are attached. No project-wide open-source license has yet been selected.\nGitHub's automatically generated Source code archives currently include public repository scaffolding, not this application's implementation.\n"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def asset_url(name: str) -> str:
    return f"https://github.com/{REPOSITORY}/releases/download/{TAG}/{urllib.parse.quote(name)}"


def build_body(original: str = ORIGINAL_BODY) -> str:
    return f"""# {TITLE}

Windows versions are collected on this page. Each version has its own downloads and checksums; earlier files remain available. The existing release URL and historical v0.4.0 tag are retained.

## v0.5.0 preliminary — latest addition

A UI and user-experience update: one application window with the requested ten sections, embedded History and settings, a complete session application list with stable scrolling, rounded dark controls and one-second explanations. Measurement, logging, reports, storage and service logic are preserved apart from version provenance.

- **[Windows x64 installer]({asset_url('CylonRaider-Setup-0.5.0-preliminary.exe')})**
- [Portable Windows x64 archive]({asset_url('CylonRaider-Portable-0.5.0-preliminary-Windows-x64.zip')})
- [Complete version-specific source and build recipe]({asset_url('CylonRaider-Source-0.5.0-preliminary.zip')})
- [SHA256 checksums]({asset_url(CHECKSUMS)}) · [Build provenance]({asset_url(PROVENANCE)}) · [Testing notes]({asset_url(README)})

Verified source: `{SOURCE_SHA}`. [Passing Windows build]({RUN_URL}): measurement 19, history 60, quality/provenance 76, native settings 78 and unified UX 3,823 assertions, plus source network guard, self-contained publishing and installer compilation.

Unsigned preliminary build for user testing. Physical DPI/multi-monitor, real ETW, boot-service and installer lifecycle/upgrade acceptance remain outstanding. This addition publishes the already-verified build without rebuilding it. Build metadata and notes retain their original bytes and use versioned filenames here.

## v0.4.0 — preserved downloads

- [Windows installer]({asset_url('CylonRaider-Setup-0.4.0.exe')})
- [Portable archive]({asset_url('CylonRaider-Windows-0.4.0-Portable.zip')})
- [SHA256 checksums]({asset_url('cylon-raider-windows-v0.4.0-SHA256SUMS.txt')})

<details>
<summary>Original v0.4.0 release notes, preserved verbatim</summary>

{original}
</details>

GitHub's automatically generated Source code archives follow the historical tag and are not the v0.5 implementation. Use the explicit v0.5 source archive above. No new project-wide open-source reuse license is granted.
"""


def validate_release(release: dict, new_hashes: dict[str, str] | None = None) -> dict[str, dict]:
    require(release.get("id") == RELEASE_ID, "Unexpected release ID")
    require(release.get("tag_name") == TAG, "Historical tag changed")
    require(release.get("immutable") is False, "Release immutability must explicitly be false")
    require(release.get("draft") is False and release.get("prerelease") is True, "Published prerelease state changed")
    require(release.get("name") in {"Cylon Raider — Windows 0.4.0", TITLE}, "Unexpected release title")
    require(release.get("body") in {ORIGINAL_BODY, build_body()}, "Release notes changed; stop for review")
    assets = release.get("assets")
    require(isinstance(assets, list), "Missing asset inventory")
    by_name: dict[str, dict] = {}
    for asset in assets:
        name = asset.get("name")
        require(isinstance(name, str) and name not in by_name, "Duplicate or invalid asset name")
        require(name in set(BASELINE) | NEW_FILES, f"Unexpected asset present: {name}")
        require(asset.get("state") == "uploaded", f"Asset not fully uploaded: {name}")
        by_name[name] = asset
    for name, (identifier, size, digest) in BASELINE.items():
        require(name in by_name, f"Historical asset missing: {name}")
        asset = by_name[name]
        require((asset.get("id"), asset.get("size"), asset.get("digest")) == (identifier, size, "sha256:" + digest), f"Historical asset changed: {name}")
    for name, asset in by_name.items():
        if name in BASELINE:
            continue
        require(new_hashes is not None and name in new_hashes, f"Unreviewed existing addition: {name}")
        require(asset.get("digest") == "sha256:" + new_hashes[name], f"Existing-name collision: {name}")
        require(isinstance(asset.get("id"), int) and asset["id"] > 0, f"Invalid new asset ID: {name}")
    return by_name


def validate_origin(repository: dict, run: dict, artifact: dict, tag: dict) -> None:
    require(repository.get("id") == REPOSITORY_ID and repository.get("full_name") == REPOSITORY, "Unexpected repository identity")
    require(run.get("id") == RUN_ID and run.get("status") == "completed" and run.get("conclusion") == "success", "Pinned build run did not pass")
    require(run.get("head_sha") == SOURCE_SHA and run.get("repository", {}).get("id") == REPOSITORY_ID, "Pinned build source/repository mismatch")
    require(run.get("event") == "push" and run.get("path") == BUILD_WORKFLOW, "Unexpected build workflow/event")
    require(artifact.get("id") == ARTIFACT_ID and artifact.get("name") == ARTIFACT_NAME, "Unexpected artifact identity")
    require(artifact.get("expired") is False and artifact.get("digest") == "sha256:" + ARTIFACT_SHA and artifact.get("size_in_bytes") == ARTIFACT_SIZE, "Artifact unavailable or digest/size changed")
    require(artifact.get("workflow_run", {}).get("id") == RUN_ID and artifact.get("workflow_run", {}).get("head_sha") == SOURCE_SHA, "Artifact/run mismatch")
    require(tag.get("object", {}).get("type") == "commit" and tag.get("object", {}).get("sha") == TAG_SHA, "Historical tag target changed")


def parse_manifest(text: str) -> dict[str, str]:
    result: dict[str, str] = {}
    for line in text.lstrip("\ufeff").splitlines():
        match = re.fullmatch(r"([0-9a-fA-F]{64})  ([^/\\]+)", line)
        require(match is not None, "Malformed checksum manifest")
        digest, name = match.groups()
        require(name not in result, "Duplicate checksum entry")
        result[name] = digest.lower()
    require(set(result) == ORIGINAL_FILES - {"SHA256SUMS.txt"}, "Manifest does not name exactly the five original files")
    return result


def prepare_payload(archive: Path, directory: Path, expected_archive_sha: str | None = None) -> dict[str, str]:
    require(sha256(archive) == (expected_archive_sha or ARTIFACT_SHA), "Artifact ZIP checksum mismatch")
    directory.mkdir(parents=True, exist_ok=True)
    require(not any(directory.iterdir()), "Prepared directory must be empty")
    with zipfile.ZipFile(archive) as bundle:
        infos = bundle.infolist()
        require(len(infos) == len(ORIGINAL_FILES) and {info.filename for info in infos} == ORIGINAL_FILES, "Unexpected, duplicate or unsafe ZIP members")
        require(sum(info.file_size for info in infos) <= 200 * 1024 * 1024, "Artifact expansion exceeds expected budget")
        for info in infos:
            require(not info.is_dir() and not stat.S_ISLNK(info.external_attr >> 16) and not (info.flag_bits & 1), "ZIP directories, symlinks or encrypted members are forbidden")
            require(info.file_size <= 120 * 1024 * 1024, "Artifact member exceeds expected budget")
            with bundle.open(info) as source, (directory / info.filename).open("xb") as target:
                shutil.copyfileobj(source, target)
    manifest = parse_manifest((directory / "SHA256SUMS.txt").read_text(encoding="utf-8-sig"))
    for name, digest in manifest.items():
        require(sha256(directory / name) == digest, f"Original manifest mismatch: {name}")
    for name, digest in PINNED_PAYLOADS.items():
        require(manifest[name] == digest, f"Approved payload mismatch: {name}")
    provenance = json.loads((directory / "BUILD-PROVENANCE.json").read_text(encoding="utf-8-sig"))
    require(provenance.get("source_sha") == SOURCE_SHA and provenance.get("version") == VERSION and provenance.get("workflow_run") == RUN_URL, "Build provenance mismatch")
    for old, new in RENAMES.items():
        (directory / old).rename(directory / new)
    (directory / "SHA256SUMS.txt").unlink()
    hashes = {name: sha256(directory / name) for name in sorted(NEW_FILES - {CHECKSUMS})}
    (directory / CHECKSUMS).write_text("".join(f"{digest}  {name}\n" for name, digest in hashes.items()), encoding="ascii")
    hashes[CHECKSUMS] = sha256(directory / CHECKSUMS)
    return hashes


class SafeRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        parsed = urllib.parse.urlparse(newurl)
        host = parsed.hostname or ""
        allowed = host in {"github.com", "api.github.com", "uploads.github.com", "release-assets.githubusercontent.com", "github-cloud.s3.amazonaws.com"} or host.endswith(".blob.core.windows.net") or host.endswith(".actions.githubusercontent.com")
        require(parsed.scheme == "https" and not parsed.username and not parsed.password and allowed, "Unapproved download redirect")
        redirected = super().redirect_request(req, fp, code, msg, headers, newurl)
        if redirected is not None and host not in {"api.github.com", "uploads.github.com"}:
            redirected.remove_header("Authorization")
            redirected.remove_header("Cookie")
            redirected.remove_header("Accept")
            redirected.add_header("Accept", "*/*")
        return redirected


def api_url(path: str) -> str:
    if path.startswith("https://uploads.github.com/"):
        return path
    return f"https://api.github.com/repos/{REPOSITORY}" + ("/" + path if path else "")


def download_accept(path: str) -> str:
    return "application/octet-stream" if path.startswith("releases/assets/") else "application/vnd.github+json"


class GitHub:
    def __init__(self, token: str):
        require(bool(token), "GH_TOKEN is required")
        self.token = token
        self.opener = urllib.request.build_opener(SafeRedirect())

    def request(self, method: str, path: str, payload=None, upload: Path | None = None):
        url = api_url(path)
        print(f"GitHub {method}: {path or REPOSITORY}", flush=True)
        headers = {"Authorization": "Bearer " + self.token, "Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28", "User-Agent": "cylon-raider-scoped-collection/0.5"}
        if upload is not None:
            data = upload.read_bytes()
            headers["Content-Type"] = "application/octet-stream"
        else:
            data = None if payload is None else json.dumps(payload).encode("utf-8")
            if data is not None:
                headers["Content-Type"] = "application/json"
        with self.opener.open(urllib.request.Request(url, data=data, headers=headers, method=method), timeout=180) as response:
            return json.load(response)

    def download(self, path: str, destination: Path) -> None:
        require(not destination.exists(), "Download destination already exists")
        headers = {"Authorization": "Bearer " + self.token, "Accept": download_accept(path), "X-GitHub-Api-Version": "2022-11-28", "User-Agent": "cylon-raider-scoped-collection/0.5"}
        url = api_url(path)
        print(f"Verify download: {path}", flush=True)
        with self.opener.open(urllib.request.Request(url, headers=headers), timeout=180) as source, destination.open("xb") as target:
            shutil.copyfileobj(source, target)


def verify_download(api: GitHub, asset: dict, digest: str, destination: Path) -> None:
    api.download(f"releases/assets/{asset['id']}", destination)
    require(destination.stat().st_size == asset.get("size") and sha256(destination) == digest, f"Downloaded asset checksum mismatch: {asset['name']}")


def validate_actor(environment) -> None:
    expected = {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted", "GITHUB_REPOSITORY": REPOSITORY, "GITHUB_ACTOR": "scientistsunite", "GITHUB_EVENT_NAME": "push", "GITHUB_REF": PUBLISH_REF}
    require(all(environment.get(key) == value for key, value in expected.items()), "Publication requires the owner push on the scoped branch in a disposable hosted runner")


def publish(work: Path, mutate: bool) -> dict:
    if mutate:
        validate_actor(os.environ)
    require(not work.exists(), "Use a fresh work directory")
    work.mkdir(parents=True)
    api = GitHub(os.environ.get("GH_TOKEN", ""))
    repository = api.request("GET", "")
    run = api.request("GET", f"actions/runs/{RUN_ID}")
    artifact = api.request("GET", f"actions/artifacts/{ARTIFACT_ID}")
    tag_path = "git/ref/tags/" + TAG
    tag = api.request("GET", tag_path)
    validate_origin(repository, run, artifact, tag)
    archive = work / "verified-artifact.zip"
    api.download(f"actions/artifacts/{ARTIFACT_ID}/zip", archive)
    hashes = prepare_payload(archive, work / "prepared")
    release = api.request("GET", f"releases/{RELEASE_ID}")
    inventory = validate_release(release, hashes)
    desired_body = build_body()
    plan = {"repository": REPOSITORY, "release_id": RELEASE_ID, "tag": TAG, "tag_sha": TAG_SHA, "name": TITLE, "source_sha": SOURCE_SHA, "run_id": RUN_ID, "artifact_id": ARTIFACT_ID, "preserved_assets": {name: identifier for name, (identifier, _, _) in BASELINE.items()}, "new_assets": hashes, "body_sha256": hashlib.sha256(desired_body.encode()).hexdigest(), "mutations": f"Append missing matching {VERSION} assets; PATCH name/body only"}
    (work / "plan.json").write_text(json.dumps(plan, indent=2) + "\n", encoding="utf-8")
    (work / "release-notes.md").write_text(desired_body, encoding="utf-8")
    print(json.dumps(plan, indent=2), flush=True)
    for name, (_, _, digest) in BASELINE.items():
        verify_download(api, inventory[name], digest, work / ("before-" + name))
    if not mutate:
        return plan
    original_body = release["body"]
    for name in sorted(hashes):
        fresh = api.request("GET", f"releases/{RELEASE_ID}")
        inventory = validate_release(fresh, hashes)
        require(fresh["body"] == original_body, "Release notes changed concurrently; stop before mutation")
        if name not in inventory:
            upload_url = f"https://uploads.github.com/repos/{REPOSITORY}/releases/{RELEASE_ID}/assets?" + urllib.parse.urlencode({"name": name})
            asset = api.request("POST", upload_url, upload=work / "prepared" / name)
            require(asset.get("name") == name and asset.get("digest") == "sha256:" + hashes[name] and asset.get("state") == "uploaded", "Uploaded asset receipt mismatch")
        else:
            asset = inventory[name]
        verify_download(api, asset, hashes[name], work / ("new-" + name))
    fresh = api.request("GET", f"releases/{RELEASE_ID}")
    inventory = validate_release(fresh, hashes)
    require(set(inventory) == set(BASELINE) | NEW_FILES, "Final addition inventory incomplete")
    require(fresh["body"] == original_body, "Release notes changed concurrently; no metadata update attempted")
    if fresh["name"] != TITLE or fresh["body"] != desired_body:
        api.request("PATCH", f"releases/{RELEASE_ID}", {"name": TITLE, "body": desired_body})
    final = api.request("GET", f"releases/{RELEASE_ID}")
    inventory = validate_release(final, hashes)
    require(final["name"] == TITLE and final["body"] == desired_body, "Final release metadata mismatch")
    final_tag = api.request("GET", tag_path)
    require(final_tag.get("object") == tag.get("object"), "Historical tag changed during publication")
    for name, (_, _, digest) in BASELINE.items():
        verify_download(api, inventory[name], digest, work / ("after-" + name))
    receipt = {**plan, "release_url": final["html_url"], "immutable": final["immutable"], "prerelease": final["prerelease"], "verified_assets": {name: {"id": asset["id"], "size": asset["size"], "digest": asset["digest"], "download_url": asset["browser_download_url"]} for name, asset in sorted(inventory.items())}}
    (work / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print("Collection publication and all historical/new download checks passed.", flush=True)
    return receipt


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--work-dir", required=True, type=Path)
    parser.add_argument("--publish", action="store_true", help="Perform the explicitly authorized append and title/body update")
    arguments = parser.parse_args()
    try:
        publish(arguments.work_dir, arguments.publish)
        return 0
    except Exception as error:
        print(f"Publication stopped: {type(error).__name__}: {error}. No automatic overwrite, delete, rollback or retry is performed.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

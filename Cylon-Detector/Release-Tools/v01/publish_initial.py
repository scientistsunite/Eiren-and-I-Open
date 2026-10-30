#!/usr/bin/env python3
"""Publish only the pinned initial Detector build; never alter another release.

Uses the standard library and GH_TOKEN on a disposable GitHub-hosted runner.
Without --publish, download verification and a reviewable plan are read-only.
No application, installer or other downloaded code is executed.
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
TAG = "cylon-detector-windows-v0.1.0"
TITLE = "Cylon Detector — Windows"
VERSION = "0.1.0-initial"
BUILD_WORKFLOW = ".github/workflows/cylon-detector-v01.yml"
PUBLISH_REF = "refs/heads/cylon-detector-v01-release"
ARTIFACT_NAME = "CylonDetector-0.1.0-initial-Windows-x64"
PAYLOAD_FILES = {
    "CylonDetector-Setup-0.1.0-initial.exe",
    "CylonDetector-Portable-0.1.0-initial-Windows-x64.zip",
    "CylonDetector-Source-0.1.0-initial.zip",
}
PROVENANCE = "CylonDetector-0.1.0-initial-BUILD-PROVENANCE.json"
README = "CylonDetector-0.1.0-initial-READ-ME-FIRST.txt"
CHECKSUMS = "cylon-detector-windows-v0.1.0-initial-SHA256SUMS.txt"
RENAMES = {"BUILD-PROVENANCE.json": PROVENANCE, "READ-ME-FIRST.txt": README}
ORIGINAL_FILES = PAYLOAD_FILES | set(RENAMES) | {"SHA256SUMS.txt"}
NEW_FILES = PAYLOAD_FILES | set(RENAMES.values()) | {CHECKSUMS}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def positive_int(value) -> bool:
    return type(value) is int and value > 0


def validate_config(config: dict) -> dict:
    require(isinstance(config, dict), "Configuration must be an object")
    require(set(config) == {"source_sha", "run_id", "artifact", "payload_sha256", "validation"}, "Unexpected or missing configuration fields")
    require(isinstance(config["source_sha"], str) and re.fullmatch(r"[0-9a-f]{40}", config["source_sha"]) is not None, "Invalid pinned source SHA")
    require(positive_int(config["run_id"]), "Invalid pinned run ID")
    artifact = config["artifact"]
    require(isinstance(artifact, dict) and set(artifact) == {"id", "name", "size", "sha256"}, "Invalid artifact configuration")
    require(positive_int(artifact["id"]) and positive_int(artifact["size"]) and artifact["size"] <= 200 * 1024 * 1024, "Invalid artifact ID/size")
    require(artifact["name"] == ARTIFACT_NAME, "Unexpected artifact name")
    hashes = config["payload_sha256"]
    require(isinstance(hashes, dict) and set(hashes) == PAYLOAD_FILES, "Configuration must pin exactly three Detector payloads")
    require(all(isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) for value in [artifact["sha256"], *hashes.values()]), "Invalid pinned SHA256")
    validation = config["validation"]
    require(isinstance(validation, dict) and all(isinstance(key, str) and re.fullmatch(r"[A-Za-z][A-Za-z0-9 /_-]{0,79}", key) and type(count) is int and count >= 0 for key, count in validation.items()), "Invalid validation counts")
    return config


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def asset_url(name: str) -> str:
    return f"https://github.com/{REPOSITORY}/releases/download/{TAG}/{urllib.parse.quote(name)}"


def build_body(config: dict) -> str:
    validation = "; ".join(f"{label}: {count}" for label, count in sorted(config["validation"].items()))
    evidence = f"Recorded passing checks: {validation}.\n\n" if validation else ""
    return f"""# {TITLE}

## v0.1.0 initial — Windows x64 prerelease

Initial independent Cylon Detector release for user testing. It combines per-application network measurements with available microphone/camera access metadata, local event-triggered network history, a five-minute RAM prebuffer and postroll, optional continuous detail, interval summaries and observations for investigation. An observation does not declare an application malicious.

- **[Windows x64 installer]({asset_url('CylonDetector-Setup-0.1.0-initial.exe')})**
- [Portable Windows x64 archive]({asset_url('CylonDetector-Portable-0.1.0-initial-Windows-x64.zip')})
- [Version-specific source and build recipe]({asset_url('CylonDetector-Source-0.1.0-initial.zip')})
- [SHA256 checksums]({asset_url(CHECKSUMS)}) · [Build provenance]({asset_url(PROVENANCE)}) · [Testing notes]({asset_url(README)})

Verified source: `{config['source_sha']}`. [Successful Windows build](https://github.com/{REPOSITORY}/actions/runs/{config['run_id']}). The installer, portable and source downloads are the exact verified build bytes, with no rebuild during publication.

{evidence}Microphone/camera coverage uses Windows CapabilityAccessManager metadata for the selected Windows owner. Coverage is limited by Windows records, permissions and available application identity; missing metadata does not prove inactivity. This release does not capture microphone audio, camera video or network payloads. The application has no telemetry, update loop or application-initiated network requests; ordinary observation uses local Windows facilities.

Unsigned initial prerelease. Hosted synthetic/native checks do not establish real-hardware ETW or sensor accuracy, physical Windows desktop behavior, boot-service reliability or physical-machine installer/upgrade acceptance. Review the attached testing notes before testing. Ubuntu and macOS are not included.

Detector is independent of Cylon Raider. This publication creates only Detector's version tag, release and six downloads; other application releases and files are preserved. GitHub's automatically generated Source code archives identify the tagged repository tree; the explicit source ZIP above contains the version-specific Detector source and build recipe. Metadata files retain their original build bytes under versioned names; the attached checksum manifest uses those public filenames. Refer to accompanying licenses and third-party notices for reuse rights.
"""


def validate_origin(config: dict, repository: dict, run: dict, artifact: dict) -> None:
    pin = config["artifact"]
    require(repository.get("id") == REPOSITORY_ID and repository.get("full_name") == REPOSITORY, "Unexpected repository identity")
    require(run.get("id") == config["run_id"] and run.get("status") == "completed" and run.get("conclusion") == "success", "Pinned build did not pass")
    require(run.get("head_sha") == config["source_sha"] and run.get("repository", {}).get("id") == REPOSITORY_ID, "Build source/repository mismatch")
    require(run.get("event") == "push" and run.get("path") == BUILD_WORKFLOW, "Unexpected build workflow/event")
    require(artifact.get("id") == pin["id"] and artifact.get("name") == pin["name"], "Artifact identity mismatch")
    require(artifact.get("expired") is False and artifact.get("digest") == "sha256:" + pin["sha256"] and artifact.get("size_in_bytes") == pin["size"], "Artifact digest/size/availability mismatch")
    require(artifact.get("workflow_run", {}).get("id") == config["run_id"] and artifact.get("workflow_run", {}).get("head_sha") == config["source_sha"], "Artifact run/source mismatch")


def validate_tag(config: dict, tag: dict) -> None:
    require(tag.get("ref") == "refs/tags/" + TAG and tag.get("object", {}).get("type") == "commit" and tag.get("object", {}).get("sha") == config["source_sha"], "Existing Detector tag is not the exact pinned source")


def validate_release(config: dict, release: dict, hashes: dict[str, str], allow_draft: bool = True) -> dict[str, dict]:
    require(positive_int(release.get("id")), "Invalid release ID")
    require(release.get("tag_name") == TAG and release.get("target_commitish") == config["source_sha"], "Existing Detector release source/tag mismatch")
    require(release.get("name") == TITLE and release.get("body") == build_body(config), "Existing Detector release notes/title mismatch")
    require(release.get("immutable") is False and release.get("prerelease") is True and type(release.get("draft")) is bool, "Unexpected release immutability/prerelease/draft state")
    require(allow_draft or release["draft"] is False, "Release is not published")
    assets = release.get("assets")
    require(isinstance(assets, list), "Missing Detector asset inventory")
    inventory: dict[str, dict] = {}
    for asset in assets:
        name = asset.get("name")
        require(name in NEW_FILES and name not in inventory, "Unexpected/duplicate Detector asset")
        require(asset.get("state") == "uploaded" and positive_int(asset.get("id")) and positive_int(asset.get("size")), "Incomplete Detector asset")
        require(asset.get("digest") == "sha256:" + hashes[name], f"Existing Detector asset collision: {name}")
        inventory[name] = asset
    require(release["draft"] or set(inventory) == NEW_FILES, "Published Detector release is incomplete")
    return inventory


def parse_manifest(text: str) -> dict[str, str]:
    result: dict[str, str] = {}
    for line in text.lstrip("\ufeff").splitlines():
        match = re.fullmatch(r"([0-9a-fA-F]{64})  ([^/\\]+)", line)
        require(match is not None, "Malformed checksum manifest")
        digest, name = match.groups()
        require(name not in result, "Duplicate checksum entry")
        result[name] = digest.lower()
    require(set(result) == ORIGINAL_FILES - {"SHA256SUMS.txt"}, "Manifest must name exactly five original Detector files")
    return result


def prepare_payload(config: dict, archive: Path, directory: Path) -> dict[str, str]:
    require(archive.stat().st_size == config["artifact"]["size"] and sha256(archive) == config["artifact"]["sha256"], "Artifact ZIP checksum/size mismatch")
    directory.mkdir(parents=True, exist_ok=True)
    require(not any(directory.iterdir()), "Prepared directory must be empty")
    with zipfile.ZipFile(archive) as bundle:
        infos = bundle.infolist()
        require(len(infos) == len(ORIGINAL_FILES) and {info.filename for info in infos} == ORIGINAL_FILES, "Unexpected, duplicate or unsafe ZIP members")
        require(sum(info.file_size for info in infos) <= 220 * 1024 * 1024, "Artifact expansion exceeds expected budget")
        for info in infos:
            require(not info.is_dir() and not stat.S_ISLNK(info.external_attr >> 16) and not (info.flag_bits & 1), "ZIP directories, symlinks and encrypted members are forbidden")
            require(info.file_size <= 120 * 1024 * 1024, "Artifact member exceeds expected budget")
            with bundle.open(info) as source, (directory / info.filename).open("xb") as target:
                shutil.copyfileobj(source, target)
    manifest = parse_manifest((directory / "SHA256SUMS.txt").read_text(encoding="utf-8-sig"))
    for name, digest in manifest.items():
        require(sha256(directory / name) == digest, f"Original manifest mismatch: {name}")
    require(all(manifest[name] == digest for name, digest in config["payload_sha256"].items()), "Approved Detector payload mismatch")
    provenance = json.loads((directory / "BUILD-PROVENANCE.json").read_text(encoding="utf-8-sig"))
    require(provenance.get("source_sha") == config["source_sha"] and provenance.get("version") == VERSION and provenance.get("workflow_run") == f"https://github.com/{REPOSITORY}/actions/runs/{config['run_id']}", "Build provenance mismatch")
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
        parsed = urllib.parse.urlparse(path)
        require(parsed.netloc == "uploads.github.com" and parsed.path.startswith(f"/repos/{REPOSITORY}/releases/"), "Unscoped upload URL")
        return path
    require(not path.startswith(("/", "http")) and ".." not in path, "Unscoped API path")
    return f"https://api.github.com/repos/{REPOSITORY}" + ("/" + path if path else "")


class GitHub:
    def __init__(self, token: str):
        require(bool(token), "GH_TOKEN is required")
        self.token = token
        self.opener = urllib.request.build_opener(SafeRedirect())

    def headers(self, accept="application/vnd.github+json") -> dict:
        return {"Authorization": "Bearer " + self.token, "Accept": accept, "X-GitHub-Api-Version": "2022-11-28", "User-Agent": "cylon-detector-scoped-publication/0.1"}

    def request(self, method: str, path: str, payload=None, upload: Path | None = None):
        print(f"GitHub {method}: {path or REPOSITORY}", flush=True)
        headers = self.headers()
        if upload is not None:
            data = upload.read_bytes()
            headers["Content-Type"] = "application/octet-stream"
        else:
            data = None if payload is None else json.dumps(payload).encode("utf-8")
            if data is not None:
                headers["Content-Type"] = "application/json"
        with self.opener.open(urllib.request.Request(api_url(path), data=data, headers=headers, method=method), timeout=180) as response:
            return json.load(response)

    def optional(self, path: str):
        try:
            return self.request("GET", path)
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return None
            raise

    def pages(self, path: str) -> list:
        result = []
        for page in range(1, 101):
            batch = self.request("GET", f"{path}{'&' if '?' in path else '?'}per_page=100&page={page}")
            require(isinstance(batch, list), "Invalid paginated API result")
            result.extend(batch)
            if len(batch) < 100:
                return result
        raise ValueError("Unexpected pagination budget exceeded")

    def download(self, path: str, destination: Path) -> None:
        require(not destination.exists(), "Download destination already exists")
        accept = "application/octet-stream" if path.startswith("releases/assets/") else "application/vnd.github+json"
        print(f"Verify download: {path}", flush=True)
        with self.opener.open(urllib.request.Request(api_url(path), headers=self.headers(accept)), timeout=180) as source, destination.open("xb") as target:
            shutil.copyfileobj(source, target)


def snapshot_metadata(release: dict, assets: list) -> dict:
    fields = ("id", "tag_name", "target_commitish", "name", "body", "draft", "prerelease", "immutable", "created_at", "published_at", "html_url")
    asset_fields = ("id", "name", "label", "size", "digest", "state", "content_type", "created_at", "updated_at", "browser_download_url")
    return {**{key: release.get(key) for key in fields}, "assets": sorted(({key: asset.get(key) for key in asset_fields} for asset in assets), key=lambda asset: asset["id"])}


def snapshot_others(api: GitHub) -> dict:
    releases = {}
    for release in api.pages("releases"):
        if release.get("tag_name") != TAG:
            assets = api.pages(f"releases/{release['id']}/assets")
            releases[str(release["id"])] = snapshot_metadata(release, assets)
    # Unlike download counters, ref/object identities must never change here.
    tags = {}
    for tag in api.pages("tags"):
        if tag.get("name") != TAG:
            name = tag["name"]
            ref = api.request("GET", "git/ref/tags/" + urllib.parse.quote(name, safe=""))
            tags[name] = {"ref": ref.get("ref"), "object": ref.get("object")}
    return {"releases": releases, "tags": tags}


def assert_preserved(before: dict, after: dict) -> None:
    require(before == after, "Another application release, asset or tag changed; stop publication")


def validate_actor(environment) -> None:
    expected = {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted", "GITHUB_REPOSITORY": REPOSITORY, "GITHUB_ACTOR": "scientistsunite", "GITHUB_EVENT_NAME": "push", "GITHUB_REF": PUBLISH_REF}
    require(all(environment.get(key) == value for key, value in expected.items()), "Publication requires owner push on the Detector-only release branch in a hosted runner")


def verify_download(api: GitHub, asset: dict, digest: str, destination: Path) -> None:
    api.download(f"releases/assets/{asset['id']}", destination)
    require(destination.stat().st_size == asset.get("size") and sha256(destination) == digest, f"Downloaded Detector asset checksum mismatch: {asset['name']}")


def get_release(api: GitHub):
    # The list includes drafts visible to this token, unlike release-by-tag.
    matches = [release for release in api.pages("releases") if release.get("tag_name") == TAG]
    require(len(matches) <= 1, "Multiple Detector releases use the requested tag")
    if not matches:
        return None
    release = api.request("GET", f"releases/{matches[0]['id']}")
    release["assets"] = api.pages(f"releases/{release['id']}/assets")
    return release


def publish(config: dict, work: Path, mutate: bool) -> dict:
    validate_config(config)
    if mutate:
        validate_actor(os.environ)
    require(not work.exists(), "Use a fresh work directory")
    work.mkdir(parents=True)
    api = GitHub(os.environ.get("GH_TOKEN", ""))
    pin = config["artifact"]
    repository = api.request("GET", "")
    run = api.request("GET", f"actions/runs/{config['run_id']}")
    artifact = api.request("GET", f"actions/artifacts/{pin['id']}")
    validate_origin(config, repository, run, artifact)
    archive = work / "verified-artifact.zip"
    api.download(f"actions/artifacts/{pin['id']}/zip", archive)
    hashes = prepare_payload(config, archive, work / "prepared")
    before = snapshot_others(api)
    tag_path = "git/ref/tags/" + TAG
    tag = api.optional(tag_path)
    release = get_release(api)
    if tag is not None:
        validate_tag(config, tag)
    if release is not None:
        require(tag is not None, "Existing release is missing its approved tag")
        validate_release(config, release, hashes)
    plan = {"repository": REPOSITORY, "tag": TAG, "source_sha": config["source_sha"], "name": TITLE, "run_id": config["run_id"], "artifact_id": pin["id"], "artifact_sha256": pin["sha256"], "new_assets": hashes, "body_sha256": hashlib.sha256(build_body(config).encode()).hexdigest(), "mutations": "Create only Detector tag/draft release and missing exact assets; publish after byte and preservation checks", "preserved_releases": sorted(before["releases"]), "preserved_tags": sorted(before["tags"])}
    for filename, content in {"plan.json": plan, "other-releases-before.json": before}.items():
        (work / filename).write_text(json.dumps(content, indent=2) + "\n", encoding="utf-8")
    (work / "release-notes.md").write_text(build_body(config), encoding="utf-8")
    print(json.dumps(plan, indent=2), flush=True)
    if not mutate:
        return plan
    # Last reviewable preflight before any mutation.
    assert_preserved(before, snapshot_others(api))
    if tag is None:
        require(api.optional(tag_path) is None, "Detector tag appeared concurrently")
        tag = api.request("POST", "git/refs", {"ref": "refs/tags/" + TAG, "sha": config["source_sha"]})
        validate_tag(config, tag)
    if release is None:
        require(get_release(api) is None, "Detector release appeared concurrently")
        release = api.request("POST", "releases", {"tag_name": TAG, "target_commitish": config["source_sha"], "name": TITLE, "body": build_body(config), "draft": True, "prerelease": True, "generate_release_notes": False, "make_latest": "false"})
        validate_release(config, release, hashes)
    release_id = release["id"]
    for name in sorted(hashes):
        fresh = get_release(api)
        require(fresh is not None and fresh["id"] == release_id, "Detector release identity changed concurrently")
        inventory = validate_release(config, fresh, hashes)
        validate_tag(config, api.request("GET", tag_path))
        if name not in inventory:
            require(fresh["draft"] is True, "Refuse to append to an unexpected published release")
            url = f"https://uploads.github.com/repos/{REPOSITORY}/releases/{release_id}/assets?" + urllib.parse.urlencode({"name": name})
            asset = api.request("POST", url, upload=work / "prepared" / name)
            require(asset.get("name") == name and asset.get("digest") == "sha256:" + hashes[name] and asset.get("state") == "uploaded", "Upload receipt mismatch")
        else:
            asset = inventory[name]
        verify_download(api, asset, hashes[name], work / ("verified-" + name))
    fresh = get_release(api)
    require(fresh is not None and fresh["id"] == release_id, "Detector release changed before publication")
    inventory = validate_release(config, fresh, hashes)
    require(set(inventory) == NEW_FILES, "Detector asset inventory incomplete")
    assert_preserved(before, snapshot_others(api))
    validate_tag(config, api.request("GET", tag_path))
    if fresh["draft"]:
        api.request("PATCH", f"releases/{release_id}", {"draft": False, "make_latest": "false"})
    final = get_release(api)
    require(final is not None and final["id"] == release_id, "Final Detector release identity changed")
    inventory = validate_release(config, final, hashes, allow_draft=False)
    validate_tag(config, api.request("GET", tag_path))
    after = snapshot_others(api)
    assert_preserved(before, after)
    (work / "other-releases-after.json").write_text(json.dumps(after, indent=2) + "\n", encoding="utf-8")
    # Re-download final public assets after draft publication, including resumes.
    for name, asset in inventory.items():
        verify_download(api, asset, hashes[name], work / ("public-" + name))
    receipt = {**plan, "release_id": release_id, "release_url": final["html_url"], "draft": final["draft"], "prerelease": final["prerelease"], "preserved_other_release_metadata": True, "preserved_other_tag_objects": True, "verified_assets": {name: {"id": asset["id"], "size": asset["size"], "digest": asset["digest"], "download_url": asset["browser_download_url"]} for name, asset in sorted(inventory.items())}}
    (work / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print("Detector publication, final download hashes and other-release preservation passed.", flush=True)
    return receipt


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", required=True, type=Path)
    parser.add_argument("--work-dir", required=True, type=Path)
    parser.add_argument("--publish", action="store_true")
    arguments = parser.parse_args()
    try:
        config = json.loads(arguments.config.read_text(encoding="utf-8"))
        publish(config, arguments.work_dir, arguments.publish)
        return 0
    except Exception as error:
        print(f"Publication stopped: {type(error).__name__}: {error}. No overwrite, deletion, rollback or automatic retry is performed.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

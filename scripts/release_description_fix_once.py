#!/usr/bin/env python3
"""One-time, idempotent release-description edit for eight reviewed Eiren & I Releases.

Updates ONLY the release body text. Never changes release tag, assets, names,
download URLs, prerelease/draft status, source archives, or release attachments.
"""
import json
import os
import sys
import urllib.error
import urllib.request

REPO = "scientistsunite/Eiren-and-I-Open"
LEGACY = "No project-wide open-source license has yet been selected."
LEGACY_OTHER = "No new project-wide open-source reuse license is granted."
TARGETS = [
    (427952986, "archive-reader-windows-v0.11", "Archive-Reader", "Browse and search locally exported ChatGPT conversations, including large archives, using the portable Python-based Windows viewer.", 2),
    (427953070, "archive-reader-ubuntu-v0.11", "Archive-Reader", "Browse and search locally exported ChatGPT conversations using an Ubuntu installable package or portable archive.", 3),
    (427953166, "battery-charge-control-ubuntu-5-point-offset", "Battery-Charge-Control", "Manage model-supported laptop battery charging thresholds in five-percentage-point steps on Ubuntu. Confirm device compatibility and restoration behavior before changing firmware settings.", 2),
    (427953241, "silent-meteor-windows-v0.4.0", "Silent-Meteor", "Display configurable ambient meteor animations and idle/screensaver visuals on Windows.", 3),
    (427954499, "silent-meteor-ubuntu-v0.5.16", "Silent-Meteor", "Display configurable meteor animations and supported media/screensaver visuals on Ubuntu.", 3),
    (427954573, "system-chronicle-windows-v0.4.4", "System-Chronicle", "Track locally available computer performance and thermal readings over time to investigate load and overheating on Windows.", 2),
    (427955050, "system-chronicle-ubuntu-v0.4", "System-Chronicle", "Track locally available computer performance and thermal readings over time to investigate load and overheating on Ubuntu.", 2),
    (427955131, "cylon-raider-windows-v0.4.0", "Cylon-Raider", "Observe local network activity by application, inspect live measurements, and review traffic history and reports while keeping Raider itself network-isolated. It is not a firewall.", 21),
]

def api(path, method="GET", data=None):
    payload = None if data is None else json.dumps(data, ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(
        "https://api.github.com/" + path,
        data=payload,
        method=method,
        headers={
            "Authorization": "Bearer " + os.environ["GH_TOKEN"],
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "Eiren-I-Release-Descriptions-Once",
            "Content-Type": "application/json",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=45) as resp:
            return json.load(resp)
    except urllib.error.HTTPError as exc:
        raise RuntimeError(f"GitHub API {method} {path}: HTTP {exc.code}") from exc

def meta(r):
    return (r["id"], r["tag_name"], r["name"], r["draft"], r["prerelease"],
            tuple((a["id"], a["name"], a["size"]) for a in r["assets"]))

def make_body(old, app, purpose):
    link = f"https://github.com/{REPO}/blob/main/{app}/LICENSE"
    notice = (
        f"Project-owned material in the public repository is available under the "
        f"[MIT License]({link}), subject to third-party rights. "
        f"Previously published downloadable archives and binaries retain their "
        f"applicable accompanying notices; this description change does not alter "
        f"their contents or automatically relicense them."
    )
    if LEGACY not in old:
        if "## Intended use" in old and link in old and LEGACY_OTHER not in old:
            return old   # idempotent rerun after a partial success
        raise RuntimeError(f"Expected original licensing statement missing for {app}")
    if old.count(LEGACY) != 1:
        raise RuntimeError(f"Unexpected obsolete-statement count for {app}")
    if "## Intended use" in old:
        raise RuntimeError(f"Unexpected existing intended-use section for {app}")
    new = old.replace(LEGACY, notice)
    if LEGACY_OTHER in new:
        if new.count(LEGACY_OTHER) != 1:
            raise RuntimeError("Unexpected additional legacy licensing statements")
        new = new.replace(LEGACY_OTHER, notice)
    if app == "Cylon-Raider":
        marker = "Original v0.4.0 release notes, preserved verbatim"
        if marker not in new:
            raise RuntimeError("Historical Raider note marker changed")
        new = new.replace(marker, "Historical v0.4.0 release notes (licensing statement updated)")
    line_end = new.find("\n")
    if line_end < 0 or not new.startswith("# "):
        raise RuntimeError("Release title missing")
    new = new[:line_end + 1] + "\n## Intended use\n" + purpose + "\n" + new[line_end + 1:]
    if LEGACY in new or LEGACY_OTHER in new or "## Intended use" not in new or link not in new:
        raise RuntimeError("Final release note transformation failed validation")
    return new

def main():
    if os.environ.get("GITHUB_REPOSITORY") != REPO:
        raise RuntimeError("Unexpected repository; nothing will be changed")
    if os.environ.get("GITHUB_REF") != "refs/heads/maintenance/release-description-fix-20261030":
        raise RuntimeError("Unexpected branch; nothing will be changed")
    if os.environ.get("GITHUB_EVENT_NAME") != "push":
        raise RuntimeError("Unexpected event; nothing will be changed")
    if not os.environ.get("GH_TOKEN"):
        raise RuntimeError("Missing ephemeral GitHub Actions token")
    if len(TARGETS) != 8 or len({x[0] for x in TARGETS}) != 8:
        raise RuntimeError("Incorrect release inventory")
    public_repo = api(f"repos/{REPO}")
    if public_repo["full_name"] != REPO or public_repo["default_branch"] != "main":
        raise RuntimeError("Repository identity check failed")
    # All six app LICENSE files must already exist on main.
    for app in sorted({t[2] for t in TARGETS} | {"Cylon-Detector"}):
        licensed = api(f"repos/{REPO}/contents/{app}/LICENSE?ref=main")
        if licensed.get("type") != "file" or licensed.get("size", 0) < 1000:
            raise RuntimeError(f"Missing or unexpectedly small MIT license: {app}")
    plan = []
    for release_id, tag, app, purpose, asset_count in TARGETS:
        r = api(f"repos/{REPO}/releases/{release_id}")
        if r["id"] != release_id or r["tag_name"] != tag or r["draft"]:
            raise RuntimeError(f"Release identity check failed for {tag}")
        if len(r["assets"]) != asset_count:
            raise RuntimeError(f"Asset count changed for {tag}")
        old = r.get("body")
        if not isinstance(old, str) or not old:
            raise RuntimeError(f"Missing release description for {tag}")
        new = make_body(old, app, purpose)
        plan.append((r, new))
    print(f"PRECHECK PASSED: {len(plan)} known releases, all licenses, assets and tags verified.", flush=True)
    changed = 0
    for original, new in plan:
        rid = original["id"]
        tag = original["tag_name"]
        if new == original["body"]:
            print("UNCHANGED (already completed): " + tag, flush=True)
            continue
        live = api(f"repos/{REPO}/releases/{rid}")
        if meta(live) != meta(original) or live["body"] != original["body"]:
            raise RuntimeError(f"Release changed since preflight: {tag}")
        saved = api(f"repos/{REPO}/releases/{rid}", method="PATCH", data={"body": new})
        if saved["body"] != new or meta(saved) != meta(original):
            raise RuntimeError(f"Post-update response verification failed: {tag}")
        confirm = api(f"repos/{REPO}/releases/{rid}")
        if confirm["body"] != new or meta(confirm) != meta(original):
            raise RuntimeError(f"Persisted release verification failed: {tag}")
        print("UPDATED AND VERIFIED: " + tag, flush=True)
        changed += 1
    print(f"SUCCESS: {changed} release descriptions updated; all eight now have intended uses and MIT clarification.", flush=True)
    print("No release assets, tags, prerelease statuses or downloadable files were altered.", flush=True)

if __name__ == "__main__":
    main()

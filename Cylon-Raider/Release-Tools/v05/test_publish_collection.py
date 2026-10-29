import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import stat
import tempfile
import unittest
import urllib.request
import zipfile

MODULE = Path(__file__).with_name("publish_collection.py")
SPEC = importlib.util.spec_from_file_location("collection", MODULE)
p = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(p)


def release():
    return {"id": p.RELEASE_ID, "tag_name": p.TAG, "name": "Cylon Raider — Windows 0.4.0", "body": p.ORIGINAL_BODY, "draft": False, "prerelease": True, "immutable": False, "assets": [{"id": identifier, "name": name, "size": size, "digest": "sha256:" + digest, "state": "uploaded"} for name, (identifier, size, digest) in p.BASELINE.items()]}


def origin():
    return ({"id": p.REPOSITORY_ID, "full_name": p.REPOSITORY}, {"id": p.RUN_ID, "status": "completed", "conclusion": "success", "head_sha": p.SOURCE_SHA, "repository": {"id": p.REPOSITORY_ID}, "event": "push", "path": ".github/workflows/cylon-raider-v05-preliminary.yml"}, {"id": p.ARTIFACT_ID, "name": p.ARTIFACT_NAME, "expired": False, "digest": "sha256:" + p.ARTIFACT_SHA, "size_in_bytes": p.ARTIFACT_SIZE, "workflow_run": {"id": p.RUN_ID, "head_sha": p.SOURCE_SHA}}, {"object": {"type": "commit", "sha": p.TAG_SHA}})


class ReleaseGuards(unittest.TestCase):
    def test_download_accept_matches_github_endpoint(self):
        self.assertEqual(p.download_accept("actions/artifacts/12910193270/zip"), "application/vnd.github+json")
        self.assertEqual(p.download_accept("releases/assets/688782967"), "application/octet-stream")

    def test_repository_root_and_subresource_urls(self):
        root = "https://api.github.com/repos/" + p.REPOSITORY
        self.assertEqual(p.api_url(""), root)
        self.assertEqual(p.api_url("releases/427955131"), root + "/releases/427955131")
        upload = "https://uploads.github.com/repos/" + p.REPOSITORY + "/releases/427955131/assets?name=versioned.txt"
        self.assertEqual(p.api_url(upload), upload)

    def test_mutation_actor_is_owner_on_scoped_hosted_branch(self):
        environment = {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted", "GITHUB_REPOSITORY": p.REPOSITORY, "GITHUB_ACTOR": "scientistsunite", "GITHUB_EVENT_NAME": "push", "GITHUB_REF": "refs/heads/cylon-raider-v05-release-addition"}
        p.validate_actor(environment)
        for key in environment:
            with self.subTest(key=key), self.assertRaises(ValueError):
                changed = dict(environment); changed[key] = "other"; p.validate_actor(changed)

    def test_identity_and_all_original_assets(self):
        inventory = p.validate_release(release())
        self.assertEqual(set(inventory), set(p.BASELINE))
        for field, wrong in (("id", 1), ("tag_name", "other"), ("immutable", True), ("immutable", None), ("immutable", 0), ("draft", True), ("prerelease", False), ("body", "concurrent edit"), ("name", "other")):
            with self.subTest(field=field, wrong=wrong), self.assertRaises(ValueError):
                value = release(); value[field] = wrong; p.validate_release(value)

    def test_original_assets_cannot_disappear_or_change(self):
        value = release(); value["assets"].pop()
        with self.assertRaises(ValueError): p.validate_release(value)
        for field, wrong in (("id", 1), ("name", "renamed.exe"), ("size", 1), ("digest", "sha256:" + "0" * 64), ("state", "new")):
            with self.subTest(field=field), self.assertRaises(ValueError):
                value = release(); value["assets"][0][field] = wrong; p.validate_release(value)

    def test_collision_resume_requires_exact_digest(self):
        name = next(iter(p.PINNED_PAYLOADS)); digest = p.PINNED_PAYLOADS[name]
        value = release(); value["assets"].append({"id": 900000001, "name": name, "digest": "sha256:" + digest, "state": "uploaded"})
        self.assertIn(name, p.validate_release(value, {name: digest}))
        for hashes in (None, {name: "0" * 64}):
            with self.assertRaises(ValueError): p.validate_release(value, hashes)
        value["assets"].append(copy.deepcopy(value["assets"][-1]))
        with self.assertRaises(ValueError): p.validate_release(value, {name: digest})

    def test_unknown_asset_is_not_touched(self):
        value = release(); value["assets"].append({"id": 900000001, "name": "unreviewed.zip", "state": "uploaded"})
        with self.assertRaises(ValueError): p.validate_release(value)

    def test_approved_source_and_tag_are_pinned(self):
        values = origin(); p.validate_origin(*values)
        for index, field, wrong in ((0, "id", 0), (1, "conclusion", "failure"), (1, "head_sha", "0" * 40), (1, "event", "pull_request"), (2, "expired", True), (2, "digest", "sha256:" + "0" * 64)):
            with self.subTest(index=index, field=field), self.assertRaises(ValueError):
                modified = copy.deepcopy(values); modified[index][field] = wrong; p.validate_origin(*modified)
        modified = copy.deepcopy(values); modified[3]["object"]["sha"] = "0" * 40
        with self.assertRaises(ValueError): p.validate_origin(*modified)

    def test_release_body_retains_original_notes_and_version_links(self):
        body = p.build_body()
        self.assertIn(p.ORIGINAL_BODY, body)
        self.assertTrue(body.startswith("# Cylon Raider — Windows\n"))
        for name in set(p.BASELINE) | p.NEW_FILES:
            self.assertIn(p.asset_url(name), body)
        self.assertIn(p.SOURCE_SHA, body)
        value = release(); value.update(name=p.TITLE, body=body)
        p.validate_release(value)


class ManifestAndArchive(unittest.TestCase):
    def fixture(self, root, change=None):
        payload = {name: ("synthetic " + name).encode() for name in p.ORIGINAL_FILES - {"SHA256SUMS.txt"}}
        payload["BUILD-PROVENANCE.json"] = json.dumps({"source_sha": p.SOURCE_SHA, "version": "0.5.0-preliminary", "workflow_run": p.RUN_URL}).encode()
        hashes = {name: hashlib.sha256(data).hexdigest() for name, data in payload.items()}
        payload["SHA256SUMS.txt"] = "".join(f"{digest}  {name}\n" for name, digest in sorted(hashes.items())).encode()
        if change: change(payload)
        archive = root / "fixture.zip"
        with zipfile.ZipFile(archive, "w") as bundle:
            for name, data in payload.items(): bundle.writestr(name, data)
        return archive, hashes, payload

    def test_checksum_manifest_is_exact_not_partial(self):
        valid = "".join(f"{'a' * 64}  {name}\n" for name in sorted(p.ORIGINAL_FILES - {"SHA256SUMS.txt"}))
        self.assertEqual(len(p.parse_manifest(valid)), 5)
        for bad in (valid + valid.splitlines()[0] + "\n", valid.replace("  ", " ", 1), valid.replace("BUILD-PROVENANCE.json", "../BUILD-PROVENANCE.json"), valid.replace(valid.splitlines()[0] + "\n", "")):
            with self.assertRaises(ValueError): p.parse_manifest(bad)

    def test_verified_files_are_preserved_and_metadata_names_versioned(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); archive, hashes, payload = self.fixture(root)
            original_pins = p.PINNED_PAYLOADS
            try:
                p.PINNED_PAYLOADS = {name: hashes[name] for name in original_pins}
                out = root / "out"
                published = p.prepare_payload(archive, out, p.sha256(archive))
            finally: p.PINNED_PAYLOADS = original_pins
            self.assertEqual(set(published), p.NEW_FILES)
            for old, data in payload.items():
                if old == "SHA256SUMS.txt": continue
                self.assertEqual((out / p.RENAMES.get(old, old)).read_bytes(), data)
            manifest = (out / p.CHECKSUMS).read_text()
            for name in p.NEW_FILES - {p.CHECKSUMS}:
                self.assertIn(f"{p.sha256(out / name)}  {name}\n", manifest)
            self.assertFalse((out / "SHA256SUMS.txt").exists())

    def test_archive_checksum_and_payload_changes_fail(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); archive, hashes, payload = self.fixture(root)
            with self.assertRaises(ValueError): p.prepare_payload(archive, root / "wrong-hash", "0" * 64)
            with self.assertRaises(ValueError): p.prepare_payload(archive, root / "unapproved-payload", p.sha256(archive))

    def test_unsafe_archive_members_fail_before_extract(self):
        for name in ("../outside", "/absolute", "folder/file", "unexpected.txt"):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as temp:
                root = Path(temp); archive, _, _ = self.fixture(root, lambda payload: payload.update({name: b"invalid"}))
                with self.assertRaises(ValueError): p.prepare_payload(archive, root / "out", p.sha256(archive))
                self.assertFalse((root / "outside").exists())

    def test_symlink_member_is_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); archive, _, payload = self.fixture(root)
            with zipfile.ZipFile(archive, "w") as bundle:
                for name, data in payload.items():
                    info = zipfile.ZipInfo(name)
                    if name == "READ-ME-FIRST.txt": info.external_attr = (stat.S_IFLNK | 0o777) << 16
                    bundle.writestr(info, data)
            with self.assertRaises(ValueError): p.prepare_payload(archive, root / "out", p.sha256(archive))


class Redirects(unittest.TestCase):
    def test_download_redirect_drops_authorization(self):
        request = urllib.request.Request("https://api.github.com/repos/example/artifact", headers={"Authorization": "Bearer synthetic-only", "Cookie": "synthetic-cookie"})
        redirected = p.SafeRedirect().redirect_request(request, None, 302, "Found", {}, "https://release-assets.githubusercontent.com/synthetic")
        self.assertIsNone(redirected.get_header("Authorization"))
        self.assertIsNone(redirected.get_header("Cookie"))

    def test_unapproved_redirect_rejected(self):
        request = urllib.request.Request("https://api.github.com/repos/example/artifact")
        for target in ("http://release-assets.githubusercontent.com/file", "https://example.com/file", "https://api.github.com.evil.example/file"):
            with self.assertRaises(ValueError): p.SafeRedirect().redirect_request(request, None, 302, "Found", {}, target)


if __name__ == "__main__":
    unittest.main()

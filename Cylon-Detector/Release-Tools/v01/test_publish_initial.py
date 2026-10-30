"""Synthetic, offline tests for the Detector-only release publication guards."""
import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import stat
import tempfile
import unittest
import urllib.request
import urllib.error
import urllib.parse
from unittest.mock import Mock, patch
import zipfile

SPEC = importlib.util.spec_from_file_location("detector_initial_publisher", Path(__file__).with_name("publish_initial.py"))
p = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(p)


def config():
    return {"source_sha": "a" * 40, "run_id": 123456, "artifact": {"id": 654321, "name": "CylonDetector-0.1.0-initial-Windows-x64", "size": 1234, "sha256": "b" * 64}, "payload_sha256": {name: "c" * 64 for name in p.PAYLOAD_FILES}, "validation": {"measurement": 19}}


def origin(cfg):
    return ({"id": p.REPOSITORY_ID, "full_name": p.REPOSITORY}, {"id": cfg["run_id"], "status": "completed", "conclusion": "success", "head_sha": cfg["source_sha"], "repository": {"id": p.REPOSITORY_ID}, "event": "push", "path": ".github/workflows/cylon-detector-v01.yml"}, {"id": cfg["artifact"]["id"], "name": cfg["artifact"]["name"], "expired": False, "digest": "sha256:" + cfg["artifact"]["sha256"], "size_in_bytes": cfg["artifact"]["size"], "workflow_run": {"id": cfg["run_id"], "head_sha": cfg["source_sha"]}})


def release(cfg, hashes=None, draft=True):
    hashes = hashes or {}
    return {"id": 987654, "tag_name": p.TAG, "target_commitish": cfg["source_sha"], "name": p.TITLE, "body": p.build_body(cfg), "draft": draft, "prerelease": True, "immutable": False, "assets": [{"id": index + 1, "name": name, "size": 42, "digest": "sha256:" + digest, "state": "uploaded"} for index, (name, digest) in enumerate(sorted(hashes.items()))]}


class PublicationGuards(unittest.TestCase):
    def test_api_urls_stay_in_the_authorized_repository(self):
        root = "https://api.github.com/repos/" + p.REPOSITORY
        self.assertEqual(p.api_url(""), root)
        self.assertEqual(p.api_url("releases/123"), root + "/releases/123")
        upload = "https://uploads.github.com/repos/" + p.REPOSITORY + "/releases/123/assets?name=Detector.zip"
        self.assertEqual(p.api_url(upload), upload)
        for invalid in ("/other/repository", "../other/repository", "https://example.com/release", "https://uploads.github.com/repos/other/repository/releases/123/assets"):
            with self.subTest(path=invalid), self.assertRaises(ValueError): p.api_url(invalid)

    def test_config_requires_all_exact_payload_pins(self):
        p.validate_config(config())
        for field, wrong in (("source_sha", "main"), ("run_id", 0), ("run_id", True), ("payload_sha256", {})):
            with self.subTest(field=field, wrong=wrong), self.assertRaises(ValueError):
                changed = config(); changed[field] = wrong; p.validate_config(changed)
        for field, wrong in (("id", 0), ("name", "CylonRaider-artifact"), ("sha256", "unpinned"), ("size", -1)):
            with self.subTest(field=field), self.assertRaises(ValueError):
                changed = config(); changed["artifact"][field] = wrong; p.validate_config(changed)

    def test_origin_is_exact_successful_detector_push(self):
        cfg = config(); values = origin(cfg); p.validate_origin(cfg, *values)
        for index, field, wrong in ((0, "id", 0), (0, "full_name", "other/repository"), (1, "conclusion", "failure"), (1, "head_sha", "d" * 40), (1, "event", "pull_request"), (1, "path", ".github/workflows/cylon-raider-v05-preliminary.yml"), (2, "expired", True), (2, "digest", "sha256:" + "0" * 64), (2, "size_in_bytes", 1)):
            with self.subTest(index=index, field=field), self.assertRaises(ValueError):
                changed = copy.deepcopy(values); changed[index][field] = wrong; p.validate_origin(cfg, *changed)
        changed = copy.deepcopy(values); changed[2]["workflow_run"]["id"] += 1
        with self.assertRaises(ValueError): p.validate_origin(cfg, *changed)

    def test_tag_must_point_to_approved_source_commit(self):
        cfg = config(); tag = {"ref": "refs/tags/" + p.TAG, "object": {"type": "commit", "sha": cfg["source_sha"]}}
        p.validate_tag(cfg, tag)
        for field, wrong in (("type", "tag"), ("sha", "0" * 40)):
            with self.subTest(field=field), self.assertRaises(ValueError):
                changed = copy.deepcopy(tag); changed["object"][field] = wrong; p.validate_tag(cfg, changed)

    def test_only_matching_detector_release_can_resume(self):
        cfg = config(); hashes = {name: "d" * 64 for name in p.NEW_FILES}
        self.assertEqual(p.validate_release(cfg, release(cfg), hashes), {})
        partial = release(cfg, hashes); partial["assets"].pop()
        self.assertEqual(len(p.validate_release(cfg, partial, hashes)), 5)
        self.assertEqual(set(p.validate_release(cfg, release(cfg, hashes), hashes)), p.NEW_FILES)
        p.validate_release(cfg, release(cfg, hashes, draft=False), hashes, allow_draft=False)
        for field, wrong in (("tag_name", "cylon-raider-windows-v0.4.0"), ("target_commitish", "main"), ("name", "other"), ("body", "concurrent edit"), ("prerelease", False), ("immutable", True), ("immutable", None), ("immutable", 0)):
            with self.subTest(field=field), self.assertRaises(ValueError):
                changed = release(cfg, hashes); changed[field] = wrong; p.validate_release(cfg, changed, hashes)
        with self.assertRaises(ValueError): p.validate_release(cfg, release(cfg, hashes), hashes, allow_draft=False)

    def test_asset_collision_or_partial_published_release_fails(self):
        cfg = config(); hashes = {name: "d" * 64 for name in p.NEW_FILES}
        for field, wrong in (("name", "unreviewed.zip"), ("digest", "sha256:" + "0" * 64), ("state", "new"), ("id", 0)):
            with self.subTest(field=field), self.assertRaises(ValueError):
                changed = release(cfg, hashes); changed["assets"][0][field] = wrong; p.validate_release(cfg, changed, hashes)
        changed = release(cfg, hashes); changed["assets"].append(copy.deepcopy(changed["assets"][0]))
        with self.assertRaises(ValueError): p.validate_release(cfg, changed, hashes)
        changed = release(cfg, hashes, draft=False); changed["assets"].pop()
        with self.assertRaises(ValueError): p.validate_release(cfg, changed, hashes)

    def test_owner_branch_and_disposable_runner_are_required(self):
        env = {"GITHUB_ACTIONS": "true", "RUNNER_ENVIRONMENT": "github-hosted", "GITHUB_REPOSITORY": p.REPOSITORY, "GITHUB_ACTOR": "scientistsunite", "GITHUB_EVENT_NAME": "push", "GITHUB_REF": "refs/heads/cylon-detector-v01-release"}
        p.validate_actor(env)
        for key in env:
            with self.subTest(key=key), self.assertRaises(ValueError):
                changed = dict(env); changed[key] = "other"; p.validate_actor(changed)

    def test_other_release_metadata_and_assets_must_be_preserved(self):
        cfg = config(); other = release(cfg); other.update(id=555, tag_name="other-app-v1", name="Other app")
        assets = [{"id": 123, "name": "other.exe", "size": 42, "digest": "sha256:" + "e" * 64, "state": "uploaded", "download_count": 4}]
        before = p.snapshot_metadata(other, assets)
        p.assert_preserved(before, copy.deepcopy(before))
        counted_assets = copy.deepcopy(assets); counted_assets[0]["download_count"] += 1
        p.assert_preserved(before, p.snapshot_metadata(other, counted_assets))
        for field, wrong in (("name", "renamed other app"), ("body", "edited other notes"), ("tag_name", "retagged")):
            with self.subTest(field=field), self.assertRaises(ValueError):
                changed = dict(other); changed[field] = wrong; p.assert_preserved(before, p.snapshot_metadata(changed, assets))
        changed_assets = copy.deepcopy(assets); changed_assets[0]["digest"] = "sha256:" + "f" * 64
        with self.assertRaises(ValueError): p.assert_preserved(before, p.snapshot_metadata(other, changed_assets))
        with self.assertRaises(ValueError): p.assert_preserved(before, p.snapshot_metadata(other, []))

    def test_notes_link_all_files_and_state_sensor_acceptance_limits(self):
        cfg = config(); body = p.build_body(cfg)
        self.assertTrue(body.startswith("# Cylon Detector — Windows\n"))
        self.assertIn(cfg["source_sha"], body)
        for name in p.NEW_FILES: self.assertIn(p.asset_url(name), body)
        for phrase in ("CapabilityAccessManager", "physical", "audio", "video", "payload"):
            self.assertIn(phrase.lower(), body.lower())


class ReleaseReadbackGuards(unittest.TestCase):
    def fake_api(self, cfg, responses, asset_lists):
        api = Mock()
        api.request.side_effect = [copy.deepcopy(value) for value in responses]
        api.pages.side_effect = [copy.deepcopy(value) for value in asset_lists]
        return api

    def test_known_id_reads_do_not_use_a_stale_release_collection(self):
        cfg = config(); hashes = {name: "d" * 64 for name in p.NEW_FILES}
        api = self.fake_api(cfg, [release(cfg)], [[]])
        found = p.get_release(api, 987654, config=cfg, hashes=hashes)
        self.assertEqual(found["id"], 987654)
        api.request.assert_called_once_with("GET", "releases/987654")
        api.pages.assert_called_once_with("releases/987654/assets")

    def test_resume_discovery_retries_only_direct_visibility_after_an_id_is_known(self):
        cfg = config(); hashes = {name: "d" * 64 for name in p.NEW_FILES}
        api = Mock()
        api.pages.side_effect = [[release(cfg)], []]
        api.request.side_effect = [urllib.error.HTTPError("synthetic", 404, "Not visible", {}, None), release(cfg)]
        with patch.object(p.time, "sleep") as wait:
            found = p.get_release(api, config=cfg, hashes=hashes)
        self.assertEqual(found["id"], 987654)
        self.assertEqual(api.pages.call_args_list[0].args, ("releases",))
        self.assertEqual(api.pages.call_args_list[1].args, ("releases/987654/assets",))
        self.assertTrue(all(call.args == ("GET", "releases/987654") for call in api.request.call_args_list))
        wait.assert_called_once_with(2)

    def test_known_uploaded_receipt_can_be_temporarily_absent_without_upload_retry(self):
        cfg = config(); name = sorted(p.NEW_FILES)[0]; hashes = {item: "d" * 64 for item in p.NEW_FILES}
        asset = release(cfg, {name: hashes[name]})["assets"][0]
        api = self.fake_api(cfg, [release(cfg), release(cfg)], [[], [asset]])
        with patch.object(p.time, "sleep") as wait:
            found = p.get_release(api, 987654, config=cfg, hashes=hashes, expected_assets={name: asset})
        self.assertEqual(found["assets"], [asset])
        self.assertTrue(all(call.args[0] == "GET" for call in api.request.call_args_list))
        self.assertEqual(api.request.call_count, 2)
        wait.assert_called_once_with(2)

    def test_mismatched_source_body_asset_or_known_identity_never_retries(self):
        cfg = config(); hashes = {name: "d" * 64 for name in p.NEW_FILES}
        original = release(cfg, hashes); known = {asset["name"]: asset for asset in original["assets"]}
        for mutation in ("source", "body", "digest", "asset_id"):
            changed = copy.deepcopy(original)
            if mutation == "source": changed["target_commitish"] = "0" * 40
            if mutation == "body": changed["body"] = "concurrent notes edit"
            if mutation == "digest": changed["assets"][0]["digest"] = "sha256:" + "e" * 64
            if mutation == "asset_id": changed["assets"][0]["id"] += 1000
            api = self.fake_api(cfg, [changed], [changed["assets"]])
            with self.subTest(mutation=mutation), patch.object(p.time, "sleep") as wait:
                with self.assertRaises(ValueError):
                    p.get_release(api, 987654, config=cfg, hashes=hashes, expected_assets=known)
                wait.assert_not_called()
                self.assertEqual(api.request.call_count, 1)

    def test_visibility_retries_are_bounded_and_permission_errors_are_not_retried(self):
        cfg = config(); hashes = {name: "d" * 64 for name in p.NEW_FILES}
        for status, attempts in ((404, 6), (403, 1)):
            api = Mock(); api.request.side_effect = urllib.error.HTTPError("synthetic", status, "Unavailable", {}, None)
            with self.subTest(status=status), patch.object(p.time, "sleep") as wait:
                with self.assertRaises(urllib.error.HTTPError):
                    p.get_release(api, 987654, config=cfg, hashes=hashes)
                self.assertEqual(api.request.call_count, attempts)
                self.assertEqual(wait.call_count, attempts - 1)

    def test_published_state_visibility_can_lag_but_incomplete_unexpected_publication_fails(self):
        cfg = config(); hashes = {name: "d" * 64 for name in p.NEW_FILES}
        draft = release(cfg, hashes); public = release(cfg, hashes, draft=False)
        known = {asset["name"]: asset for asset in draft["assets"]}
        api = self.fake_api(cfg, [draft, public], [draft["assets"], public["assets"]])
        with patch.object(p.time, "sleep") as wait:
            found = p.get_release(api, 987654, config=cfg, hashes=hashes, expected_assets=known, published=True)
        self.assertFalse(found["draft"])
        wait.assert_called_once_with(2)
        incomplete = release(cfg, draft=False)
        api = self.fake_api(cfg, [incomplete], [[]])
        with patch.object(p.time, "sleep") as wait:
            with self.assertRaises(ValueError): p.get_release(api, 987654, config=cfg, hashes=hashes)
            wait.assert_not_called()

    def test_fresh_gets_use_official_host_cache_controls_and_unique_query_nonces(self):
        api = p.GitHub("synthetic-test-token")
        api.opener = Mock()
        api.opener.open.side_effect = lambda *args, **kwargs: io.BytesIO(b"{}")
        with patch.object(p.time, "time_ns", side_effect=[101, 102]):
            api.request("GET", "releases/987654")
            api.request("GET", "releases/987654")
        requests = [call.args[0] for call in api.opener.open.call_args_list]
        self.assertEqual(len({request.full_url for request in requests}), 2)
        for request in requests:
            self.assertEqual(urllib.parse.urlparse(request.full_url).netloc, "api.github.com")
            headers = {key.lower(): value for key, value in request.headers.items()}
            self.assertEqual(headers["cache-control"], "no-cache")
            self.assertEqual(headers["pragma"], "no-cache")
            self.assertIn("detector_readback=", request.full_url)

    def test_binary_read_visibility_retry_keeps_exact_hash_verification(self):
        with tempfile.TemporaryDirectory() as temp:
            destination = Path(temp) / "verified.bin"; data = b"synthetic exact bytes"
            asset = {"id": 123, "name": "Detector.bin", "size": len(data)}
            api = Mock(); calls = 0
            def download(path, output):
                nonlocal calls
                calls += 1
                if calls == 1: raise urllib.error.HTTPError("synthetic", 404, "Not visible", {}, None)
                output.write_bytes(data)
            api.download.side_effect = download
            with patch.object(p.time, "sleep") as wait:
                p.verify_download(api, asset, hashlib.sha256(data).hexdigest(), destination)
            self.assertEqual(calls, 2)
            wait.assert_called_once_with(2)
            with self.assertRaises(ValueError):
                p.verify_download(api, asset, "0" * 64, Path(temp) / "bad-hash.bin")


class ArchiveGuards(unittest.TestCase):
    def fixture(self, root, mutate=None, symlink=False):
        cfg = config()
        payload = {name: ("synthetic fixture " + name).encode() for name in p.ORIGINAL_FILES - {"SHA256SUMS.txt"}}
        payload["BUILD-PROVENANCE.json"] = json.dumps({"source_sha": cfg["source_sha"], "version": p.VERSION, "workflow_run": f"https://github.com/{p.REPOSITORY}/actions/runs/{cfg['run_id']}"}).encode()
        hashes = {name: hashlib.sha256(data).hexdigest() for name, data in payload.items()}
        payload["SHA256SUMS.txt"] = "".join(f"{digest}  {name}\n" for name, digest in sorted(hashes.items())).encode()
        cfg["payload_sha256"] = {name: hashes[name] for name in p.PAYLOAD_FILES}
        if mutate: mutate(payload)
        archive = root / "fixture.zip"
        with zipfile.ZipFile(archive, "w") as bundle:
            for name, data in payload.items():
                info = zipfile.ZipInfo(name)
                if symlink and name == "READ-ME-FIRST.txt": info.external_attr = (stat.S_IFLNK | 0o777) << 16
                bundle.writestr(info, data)
        cfg["artifact"]["sha256"] = p.sha256(archive)
        cfg["artifact"]["size"] = archive.stat().st_size
        return cfg, archive, payload

    def test_manifest_is_complete_unique_and_has_safe_names(self):
        valid = "".join(f"{'a' * 64}  {name}\n" for name in sorted(p.ORIGINAL_FILES - {"SHA256SUMS.txt"}))
        self.assertEqual(len(p.parse_manifest(valid)), 5)
        for invalid in (valid + valid.splitlines()[0] + "\n", valid.replace("  ", " ", 1), valid.replace("BUILD-PROVENANCE.json", "../BUILD-PROVENANCE.json"), valid.replace(valid.splitlines()[0] + "\n", "")):
            with self.assertRaises(ValueError): p.parse_manifest(invalid)

    def test_payload_bytes_survive_renames_and_manifest_is_regenerated(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); cfg, archive, payload = self.fixture(root); out = root / "prepared"
            hashes = p.prepare_payload(cfg, archive, out)
            self.assertEqual(set(hashes), p.NEW_FILES)
            for old, data in payload.items():
                if old != "SHA256SUMS.txt": self.assertEqual((out / p.RENAMES.get(old, old)).read_bytes(), data)
            manifest = (out / p.CHECKSUMS).read_text()
            for name in p.NEW_FILES - {p.CHECKSUMS}: self.assertIn(f"{p.sha256(out / name)}  {name}\n", manifest)
            self.assertFalse((out / "SHA256SUMS.txt").exists())

    def test_archive_manifest_and_approved_payload_hashes_fail_closed(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); cfg, archive, _ = self.fixture(root)
            changed = copy.deepcopy(cfg); changed["artifact"]["sha256"] = "0" * 64
            with self.assertRaises(ValueError): p.prepare_payload(changed, archive, root / "bad-zip")
            changed = copy.deepcopy(cfg); changed["payload_sha256"][next(iter(p.PAYLOAD_FILES))] = "0" * 64
            with self.assertRaises(ValueError): p.prepare_payload(changed, archive, root / "bad-pin")
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); cfg, archive, _ = self.fixture(root, lambda payload: payload.update({"READ-ME-FIRST.txt": b"changed after manifest"}))
            with self.assertRaises(ValueError): p.prepare_payload(cfg, archive, root / "bad-manifest")

    def test_traversal_unexpected_directories_and_symlinks_are_rejected(self):
        for name in ("../outside", "/absolute", "folder/file", "unexpected.txt"):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as temp:
                root = Path(temp); cfg, archive, _ = self.fixture(root, lambda payload: payload.update({name: b"unsafe"}))
                with self.assertRaises(ValueError): p.prepare_payload(cfg, archive, root / "out")
                self.assertFalse((root / "outside").exists())
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); cfg, archive, _ = self.fixture(root, symlink=True)
            with self.assertRaises(ValueError): p.prepare_payload(cfg, archive, root / "out")

    def test_provenance_source_must_match_even_when_manifest_is_valid(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); cfg, archive, _ = self.fixture(root)
            cfg["source_sha"] = "0" * 40
            with self.assertRaises(ValueError): p.prepare_payload(cfg, archive, root / "out")


class RedirectGuards(unittest.TestCase):
    def test_redirect_to_cdn_strips_credentials(self):
        request = urllib.request.Request("https://api.github.com/repos/example/artifact", headers={"Authorization": "Bearer synthetic-only", "Cookie": "synthetic-only"})
        redirected = p.SafeRedirect().redirect_request(request, None, 302, "Found", {}, "https://release-assets.githubusercontent.com/synthetic")
        self.assertIsNone(redirected.get_header("Authorization"))
        self.assertIsNone(redirected.get_header("Cookie"))

    def test_redirect_cannot_use_plain_http_or_untrusted_host(self):
        request = urllib.request.Request("https://api.github.com/repos/example/artifact")
        for url in ("http://release-assets.githubusercontent.com/file", "https://example.com/file", "https://api.github.com.evil.example/file"):
            with self.subTest(url=url), self.assertRaises(ValueError): p.SafeRedirect().redirect_request(request, None, 302, "Found", {}, url)


if __name__ == "__main__":
    unittest.main()

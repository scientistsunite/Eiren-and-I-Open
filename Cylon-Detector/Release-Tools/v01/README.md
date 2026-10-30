# Initial Windows release publisher

This standard-library Python publisher creates only the initial Cylon Detector tag and prerelease. It never runs downloaded application code or changes another application's release, tag or assets. Publication is restricted to the repository owner's push on `cylon-detector-v01-release` in a disposable GitHub-hosted runner.

Before publication, add a reviewed `pins.json` with these exact fields:

```json
{
  "source_sha": "40-character lowercase source commit SHA",
  "run_id": 123,
  "artifact": {
    "id": 456,
    "name": "CylonDetector-0.1.0-initial-Windows-x64",
    "size": 789,
    "sha256": "64-character lowercase artifact archive SHA256"
  },
  "payload_sha256": {
    "CylonDetector-Setup-0.1.0-initial.exe": "64-character lowercase SHA256",
    "CylonDetector-Portable-0.1.0-initial-Windows-x64.zip": "64-character lowercase SHA256",
    "CylonDetector-Source-0.1.0-initial.zip": "64-character lowercase SHA256"
  },
  "validation": {}
}
```

`validation` may contain reviewed passing assertion counts keyed by short check labels. The example strings are explanatory and deliberately fail validation. No unverified placeholder pins are supplied.

```sh
python -m unittest discover -s Cylon-Detector/Release-Tools/v01 -p 'test_publish_initial.py' -v
python Cylon-Detector/Release-Tools/v01/publish_initial.py --config Cylon-Detector/Release-Tools/v01/pins.json --work-dir /tmp/detector-review
```

Both preparation and publication require `GH_TOKEN`; preparation performs only API reads, verified downloads and local writes. Use a fresh work directory. Add `--publish` only in the owner-gated release workflow to create the pinned source tag and a draft prerelease. All six assets are checked against the artifact, manifest and provenance, uploaded without overwrites, downloaded and hashed, then the draft is published. Exact matching drafts may resume; an existing public release must already contain all six matching files. Other states fail closed without deletion, replacement or rollback.

The original installer, portable archive and source bytes are preserved. Provenance and testing notes keep their build bytes under versioned names, and the uploaded checksum manifest reflects those names. The publisher records plan/notes before its first mutation, snapshots the other releases' metadata/assets and all other tag objects, verifies preservation before and after publication, and verifies all six final public downloads. These snapshots do not claim to revalidate historical binary contents. Download counters are excluded because readers can change them independently.

After discovering or creating a release, read-back uses its exact release ID and fresh GET requests. Missing known post-write records may be retried up to six reads; source, body, asset identity and checksum mismatches still stop immediately. Mutations are never automatically retried. Receipt artifacts include the workflow attempt number so a resumed job preserves its earlier diagnostics.

Only verified artifact/build pins and public receipts belong here. Hardware ETW/sensor coverage, boot service and physical-machine installer acceptance remain separate from hosted build and synthetic test evidence.

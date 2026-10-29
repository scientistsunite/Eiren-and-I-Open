# Cylon Raider Windows collection addition

Owner-authorized operation: rename existing release 427955131 to `Cylon Raider — Windows` and append the verified v0.5 preliminary files while preserving the original v0.4 tag, assets and downloads.

The app-specific workflow `.github/workflows/cylon-raider-v05-release-addition.yml` runs only on the dedicated publication branch and owner/repository identity. It executes synthetic publication tests and this publisher; it never rebuilds, installs or runs a downloaded application.

The publisher pins repository/release/tag identity, the successful source/build/artifact and all payload hashes. It validates ZIP members, complete checksums and provenance, stages versioned metadata, and writes a concrete plan before mutations. Existing historical downloads are hashed before and after. New filenames may be uploaded only when absent; a matching existing addition can be verified and resumed, while a mismatch stops publication without deletion or replacement. Only the release title and body are patched; prerelease status and tag are retained.

Run local tests:

```sh
python3 -m unittest discover -s Cylon-Raider/Release-Tools/v05 -p 'test_publish_collection.py' -v
```

On the scoped runner, omit `--publish` to perform read/download verification and produce a plan only:

```sh
python3 Cylon-Raider/Release-Tools/v05/publish_collection.py --work-dir "$RUNNER_TEMP/raider-collection"
```

Explicit authorized publication:

```sh
python3 Cylon-Raider/Release-Tools/v05/publish_collection.py --work-dir "$RUNNER_TEMP/raider-collection" --publish
```

Use a fresh work directory. `GH_TOKEN` is supplied through the runner environment and never written to a receipt. Authorization headers are stripped from storage redirects. A verification receipt is produced only after all new and historical download checks pass.

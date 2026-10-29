# Reviewed v0.5.2 collection addition

This app-scoped publisher reuses the v0.5 publication engine with a separately pinned build, artifact, source and inventory. It adds six versioned files to the owner-requested Windows collection and updates its notes. It never changes the historical tag, deletes or replaces an asset, starts application code, or builds a new package.

`verified-build.json` pins the successful Windows run and package hashes. `baseline.json` is the reviewed public release inventory and notes before this addition; all fifteen existing assets are downloaded and hash-verified before and after publication. Existing assets with new names are accepted only if their hashes match exactly. Concurrent notes changes stop publication.

The dedicated workflow permits publication only from the owner-pushed `cylon-raider-v052-release-addition` branch on a disposable GitHub-hosted runner. The workflow has contents-write and actions-read permissions only for that job. Blob-download redirects remove authorization headers. No credentials are written to receipts.

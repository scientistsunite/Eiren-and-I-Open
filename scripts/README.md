# Public release-note maintenance

Run `Update-Public-Release-Notes.ps1` with Windows PowerShell and an authenticated GitHub CLI **after the app-scoped MIT licenses are merged into `main`**. The script checks the six license files and the identities of the eight existing GitHub Releases.

It defaults to a **read-only dry run**; add `-Apply` to remove the obsolete no-license statement and add intended use descriptions to each release. It updates GitHub Release **body text only**, not tags, historical assets, checksums, binary downloads or release visibility. A changed release description during preflight makes the operation stop.

```powershell
.\scripts\Update-Public-Release-Notes.ps1
.\scripts\Update-Public-Release-Notes.ps1 -Apply
```

Existing asset contents may have their own third-party notices; the new app-scoped MIT licenses do not automatically change those older packages.

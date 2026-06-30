# GPhotos Takeout Sync

<img width="2198" height="1543" alt="image" src="https://github.com/user-attachments/assets/d41ee88d-2912-4867-a9e5-35620b130046" />

Portable Windows app (WinUI 3) to sync **1 to N Google Takeout (Google Photos) folders** to a drive (typically a USB disk), copying only what's needed, incrementally.

Available in **11 languages** (English, Spanish, French, German, Italian, Portuguese, Dutch, Polish, Russian, Chinese, Japanese) — auto-detected from Windows and switchable in the app, instantly.

## What it does and why

When you export Google Photos, Google Takeout has a few quirks this app handles:

- **It splits a single album across several parts** (ZIPs). The app **merges the N parts** into one virtual tree keyed by relative path before comparing. A file and its `.json` can live in different parts.
- **Event/album folders usually contain only `.json`** (membership pointers); the real media lives in the year folders `Fotos del YYYY`. The app works at the file level, so this "just works".
- **`.json` sidecars** (`*.supplemental-metadata.json`, etc.): **skipped** by default (they add no value as a backup of the photos themselves).
- **`Papelera` / `Trash`**: **excluded** by default (these are deleted photos).

### Fast comparison
By default, it compares by **name + size** (without reading the content). Photos/videos don't change after export, so this is enough and very fast over hundreds of GB. There are also *name+size+date* and *SHA-256 hash* (slow) modes.

### Deletion safety (mirror mode)
The destination can accumulate files that are no longer in the Takeout. Options:

| Mode | Action |
|------|--------|
| **Add only** | Never deletes. Copies what's missing. (Safest) |
| **Report** | Lists orphans, touches nothing |
| **Quarantine** | Moves orphans to `_SyncTrash` on the destination |
| **Mirror** | Same as quarantine, but only if you confirm **all** parts are present |

Safeguards (v1):
- **Never deletes permanently**: orphans are **moved to `_SyncTrash_…`** on the destination itself (reversible; you empty it).
- Only files inside **folders the Takeout actually contributes media to** are considered orphans. Folders that exist only at the destination (e.g. the English `Photos from 2020` from older exports) are **protected**, unless you explicitly enable *"also delete destination-only folders"*.
- **Mirror** mode requires checking *"I've loaded all the parts"*; otherwise it degrades to *report* (so it won't delete media that actually lives in a part you didn't load).

## Structure

```
src/
  GPhotosSyncer.Core/   UI-less engine: scan, merge, filter, compare, copy, quarantine
  GPhotosSyncer.Cli/    Console front-end (handy for validation/automation)
  GPhotosSyncer.App/    WinUI 3 app (MVVM) — the portable UI
```

## Build requirements

- Windows 10 1809+ / 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download) (the Windows App SDK is pulled in via NuGet automatically; Visual Studio is **not** required)

```powershell
winget install Microsoft.DotNet.SDK.9
```

## Build the portable app

Double-click **`build-portable.bat`** (no PowerShell policy change needed), or from any terminal use one of these:

```powershell
# A) direct command (not a script, so the execution policy never blocks it):
dotnet publish src/GPhotosSyncer.App/GPhotosSyncer.App.csproj -c Release -r win-x64 -o publish/GPhotosTakeoutSync

# B) the script, bypassing the policy for this call only:
powershell -ExecutionPolicy Bypass -File .\build-portable.ps1
```

> If you see "running scripts is disabled on this system", that's Windows' execution policy (`Restricted` by default), not a project bug. Use the `.bat`, option A, or enable scripts for your user with `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`.

This produces **a single portable executable** `publish\GPhotosTakeoutSync\GPhotosTakeoutSync.exe` (~207 MB, *self-contained*: bundles .NET and the Windows App SDK). Copy it anywhere — including the USB drive — and run it by double-clicking, no install required. Config is saved in `gpsync.profile.json` next to the `.exe`, so it travels with it.

> First launch: it extracts its native components to a temp folder, so it takes 1-2 s longer; subsequent launches are fast.

## Distribution

| Option | When | Notes |
|---|---|---|
| **Single `.exe`** (this one) via *GitHub Releases* | Simplest for a free app | Download and run. Unsigned → SmartScreen warns the first time (*More info → Run anyway*). |
| **Installer** (Inno Setup → `setup.exe`) | If you want a Start-menu shortcut and clean uninstall | Free, no certificate. Same SmartScreen warning. |
| **MSIX + Microsoft Store** | Best experience (automatic install/update/trust) | Requires a developer account (~$19 one-time) and review; or sign the MSIX for manual sideloading. |

The repo includes `.github/workflows/release.yml`: pushing a `vX.Y.Z` tag makes GitHub Actions build the `.exe` and attach it to the Release automatically.

## Using the app

1. **Add folder…** for each Takeout part (accepts the `takeout-…-00N` folder; it auto-detects the `Takeout\Google Fotos` subpath). If you pick a folder that contains several parts, they're all added; you can also drag & drop multiple folders.
2. Choose the **destination** (the USB folder).
3. Adjust **Options** (the defaults are the recommended ones).
4. **Analyze** → shows what it would copy, what it skips, and the orphans, without touching anything.
5. **Sync** → runs with a progress bar, speed, and ETA; cancellable.

## CLI (optional)

```powershell
# Dry run (writes nothing):
dotnet run --project src/GPhotosSyncer.Cli -- analyze --dest "E:\Fotos\Google Fotos" `
  --src "C:\...\takeout-...-001" "C:\...\takeout-...-002" --deletion mirror

# Actually execute (requires --yes):
dotnet run --project src/GPhotosSyncer.Cli -- sync --dest "E:\Fotos\Google Fotos" `
  --src "C:\...\takeout-...-001" --yes
```

Options: `--deletion addonly|report|quarantine|mirror`, `--all-parts`, `--delete-dest-only`, `--keep-json`, `--include-trash`, `--comparison namesize|namesizedate|hash`, `--parallel <n>`.

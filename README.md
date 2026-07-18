# Immich Uploader

A lightweight Windows tray app that automates uploads to a self-hosted [Immich](https://immich.app/) server using [`immich-go`](https://github.com/simulot/immich-go).

It runs in the system tray, runs uploads on a schedule, deduplicates work via `immich-go`'s server-side SHA-1 checks, and supports pause/resume, monthly full rescans, and per-folder incremental scanning.

> Requires `immich-go.exe` placed next to this app (e.g. one folder up, or alongside `ImmichUploader.exe` after install). The app shells out to it for every upload.

## Project layout

```
ImmichUploader\
├── ImmichUploader.csproj      # .NET 8 WinForms app
├── Program.cs                 # tray icon + context menu
├── SettingsForm.cs            # settings dialog
├── UploadOrchestrator.cs      # parallel folder uploads
├── Scheduler.cs               # weekly + monthly rescan timers
├── ImmichGoRunner.cs          # invokes immich-go.exe
├── AppConfig.cs / ConfigStore.cs
├── StateStore.cs              # per-folder last-success + monthly rescan state
├── AutoStart.cs               # HKCU Run registry toggle
├── TrayIcons.cs               # color-coded tray icons
├── GlobalUsings.cs
├── Upload-Immich.ps1          # standalone PowerShell fallback (same logic)
├── setup.ps1                  # legacy install/uninstall helper
└── ImmichUploader.iss         # Inno Setup installer script
```

## Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| Windows | 10 1809+ / 11 | x64 |
| .NET SDK | 8.0 or newer | only needed to **build**; not required to run the published exe |
| Inno Setup | 6.x | only needed to **build the installer** |
| `immich-go.exe` | latest | place it next to `ImmichUploader.exe` (or alongside this project) so the app can find it |

Install the build tools:

```powershell
# .NET 8 SDK (if not already installed)
winget install --id Microsoft.DotNet.SDK.8 -e

# Inno Setup 6 (only if you want to build the installer)
winget install --id JRSoftware.InnoSetup -e
```

## Build the tray app

From this folder:

```powershell
# Publish a self-contained, single-file exe (no .NET runtime needed on target)
dotnet publish -c Release -r win-x64 --self-contained true `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:EnableCompressionInSingleFile=true
```

Output:

```
bin\Release\net8.0-windows\win-x64\publish\ImmichUploader.exe
```

This is a ~68 MB exe you can run directly or hand to other machines. Drop `immich-go.exe` into the same folder before first run.

## Build the installer

From this folder, with Inno Setup installed:

```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" ImmichUploader.iss
```

Output:

```
installer\Output\ImmichUploader-Setup-1.0.0.exe
```

A single ~64 MB self-contained installer `.exe` that bundles the tray app and both PowerShell scripts, with a standard uninstaller. The installer also places `immich-go.exe` from the parent folder if it is found there at build time.

To bump the version, edit `#define MyAppVersion` at the top of `ImmichUploader.iss` before compiling.

## Install on a target machine

### Option A — installer (recommended)

1. Copy `ImmichUploader-Setup-1.0.0.exe` to the target machine.
2. Double-click it.
3. In the wizard:
   - Pick the install folder (default: `C:\Program Files\ImmichUploader`).
   - Check **Create a desktop icon** if you want one.
   - Check **Launch Immich Uploader when Windows starts** to enable autostart on login.
4. Click **Install**, then **Finish** (the app launches automatically).
5. Make sure `immich-go.exe` is present next to `ImmichUploader.exe` in the install folder (the installer copies it from the build folder if available).

Uninstall via **Settings → Apps → Installed apps → Immich Uploader → Uninstall**, or via the Start Menu shortcut.

### Option B — portable

1. Copy `ImmichUploader.exe` (the publish output) anywhere.
2. Place `immich-go.exe` in the same folder.
3. Run `ImmichUploader.exe`. On first launch it creates `config.json` and `state.json` next to the exe.
4. Use **Settings... → Startup & Logs → Launch Immich Uploader when Windows starts** to enable autostart.

## First-run configuration

1. Right-click the tray icon → **Settings...**
2. **Server** tab — set the Immich URL and API key (or set the `IMMICH_API_KEY` environment variable before launching).
3. **Folders & Performance** tab — add the source folders (e.g. `G:\zirjo\Pictures`, `G:\zirjo\Videos`) and set the concurrency (1–32, default 4).
4. **Schedule** tab — pick the weekly day/time and the monthly rescan day/time.
5. **Startup & Logs** tab — choose the log folder (defaults to `<install dir>\Logs`), enable autostart and start-minimized as desired.
6. **Save**.

The app is now ready. Right-click the tray icon for **Run now**, **Run full rescan now**, **Pause / Resume**, **Cancel current run**, **Open log folder**, or **Quit**.

## Tray menu reference

| Item | Purpose |
|------|---------|
| Status | Live label: Idle / Running / Paused / Cancelling / Error |
| Run now | Incremental upload using the per-folder last-success timestamp |
| Run full rescan now | Ignores `state.json`, scans everything (also fires automatically every 30 days) |
| Pause / Resume | Toggles the current run without cancelling it |
| Cancel current run | Stops the running immich-go processes |
| Settings... | Opens the settings dialog |
| Open log folder | Opens `<install dir>\Logs` in Explorer |
| Open state file | Opens `state.json` in the default app |
| Quit | Exits the tray app |

## How the efficiency features work

- **Server-side dedup**: `immich-go` computes a SHA-1 of each file and skips anything already on the server. No local client-side hash cache is needed.
- **Per-folder incremental scan** (`state.json`): the orchestrator records `LastSuccessByFolder[folder]` and, on each incremental run, passes `--date-range=<last-success,today>` so only files modified since the last successful upload are even enumerated by `immich-go`. On Windows, `LastWriteTime` is what the `immich-go` date range reads.
- **Monthly full rescan**: every 30 days (`LastFullRescan`), the per-folder last-success timestamp is ignored so renamed or moved files are picked up.
- **Bounded folder parallelism**: up to 3 folders are processed concurrently (capped at half the CPU count). Concurrency within each folder is controlled by `--concurrent-tasks` in Settings.
- **Excluded system folders** during enumeration: `@eaDir`, `@__thumb`, `.Spotlight-V100`, `.photostructure`, `thumbnails`, `Lightroom Catalog`, `Recently Deleted`, `$RECYCLE.BIN`, `System Volume Information`, plus any reparse points.

## Files written at runtime

| Path | Purpose |
|------|---------|
| `config.json` | Settings (server, API key, folders, concurrency, schedule, autostart preference) |
| `state.json` | Per-folder last-success timestamps and last full rescan date |
| `Logs\upload-<date>-<trigger>.log` | Per-run log including immich-go stdout/stderr |

All three live next to the executable (or under the chosen install/log directory).

## Troubleshooting

- **"The filename, directory name, or volume label syntax is incorrect"** — a folder path contained a stray quote. Make sure paths in Settings don't have wrapping quotes; the app handles escaping automatically.
- **Tray icon doesn't appear** — check that the app actually launched (it shows a balloon tip on first run, unless launched with `--tray`). Look in Task Manager for `ImmichUploader.exe`.
- **Autostart not sticking** — check `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` for the `ImmichUploader` value. The installer's "Launch on Windows startup" task and the in-app toggle both write here.
- **Nothing seems to upload** — open the log folder from the tray menu and inspect the most recent `upload-*.log`. `immich-go` writes its own log file path to stdout; that path is also captured.

## Development

Build only (no publish):

```powershell
dotnet build -c Release
```

Run from source during development:

```powershell
dotnet run -c Release
```

The app creates `config.json` / `state.json` under `bin\Release\net8.0-windows\win-x64\` while developing.

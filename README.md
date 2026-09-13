# Immich Uploader

A lightweight Windows tray app that automates uploads to a self-hosted [Immich](https://immich.app/) server using [`immich-go`](https://github.com/simulot/immich-go).

It runs in the system tray, uploads on a schedule, watches folders in real time, deduplicates work via `immich-go`'s server-side SHA-1 checks, and supports pause/resume, optional monthly full rescans, and per-folder incremental scanning.

> Requires `immich-go.exe` placed next to this app (e.g. one folder up, or alongside `ImmichUploader.exe` after install). The app shells out to it for every upload.

## Project layout

```
ImmichUploader\
├── ImmichUploader.csproj        # .NET 8 WinForms app
├── Program.cs                   # tray icon + context menu + watcher wiring
├── MainForm.cs                  # main window: dashboard + settings tab
├── SettingsPanel.cs             # shared settings UI (immich-go flag surface)
├── SettingsForm.cs              # modal settings dialog (hosts SettingsPanel)
├── UploadOrchestrator.cs        # targeted/full runs, coalescing, concurrency
├── Scheduler.cs                 # optional weekly partial + monthly full scans
├── ImmichGoRunner.cs            # centralized immich-go argv/env construction
├── FolderWatcher.cs             # recursive FileSystemWatcher + readiness probe
├── DebounceFileQueue.cs         # fixed-window debounce + duplicate suppression
├── PathFilters.cs               # shared exclude/containment rules
├── MediaFileClassifier.cs       # image/video extension families
├── IUploadRunner.cs             # runner seam (testability)
├── AppConfig.cs / ConfigStore.cs
├── StateStore.cs                # per-folder last-success, rescan + schedule markers
├── AutoStart.cs                 # HKCU Run registry toggle
├── TrayIcons.cs                 # color-coded tray icons
├── GlobalUsings.cs
├── Upload-Immich.ps1            # standalone PowerShell fallback (batch only)
├── setup.ps1                    # legacy install/uninstall helper
├── ImmichUploader.iss           # Inno Setup installer script
└── ImmichUploader.Tests\        # xUnit test project (not shipped)
```

## Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| Windows | 10 1809+ / 11 | x64 |
| .NET SDK | 8.0 or newer | needed to build and to run the tests; not required to run the published exe |
| Inno Setup | 6.x | only needed to build the installer |
| `immich-go.exe` | latest | place it next to `ImmichUploader.exe` (or alongside this project) so the app can find it |

```powershell
# .NET 8 SDK (if not already installed)
winget install --id Microsoft.DotNet.SDK.8 -e

# Inno Setup 6 (only if you want to build the installer)
winget install --id JRSoftware.InnoSetup -e
```

## Build the tray app

Publish-only settings (single file, self-contained, RID) live on the command line so a plain `dotnet build` and the test project stay fast and RID-less:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:EnableCompressionInSingleFile=true
```

Output:

```
bin\Release\net8.0-windows\win-x64\publish\ImmichUploader.exe
```

Drop `immich-go.exe` into the same folder before first run.

## Run the tests

```powershell
dotnet test -c Release
```

From `ImmichUploader.Tests\` (or the repo root if a solution is used). The suite (59 tests) covers argument generation for the full flag surface, secret handling, config migration/clamping, fixed-window debounce behavior, scheduling math and toggles, process concurrency, trigger coalescing, cancellation, and pause safety.

## Build the installer

With Inno Setup installed:

```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" ImmichUploader.iss
```

Output: `installer\Output\ImmichUploader-Setup-<version>.exe`. The installer bundles the tray app and both PowerShell scripts. To bump the version, edit `#define MyAppVersion` at the top of `ImmichUploader.iss`. The `ImmichUploader.Tests` folder is intentionally **not** packaged.

## Install on a target machine

### Option A — installer (recommended)

1. Copy `ImmichUploader-Setup-<version>.exe` to the target machine and run it.
2. Pick the install folder (default: `C:\Program Files\ImmichUploader`).
3. Check **Launch Immich Uploader when Windows starts** if desired, then **Install** → **Finish**.
4. Ensure `immich-go.exe` is present next to `ImmichUploader.exe`.

### Option B — portable

1. Copy the publish output `ImmichUploader.exe` anywhere.
2. Place `immich-go.exe` in the same folder.
3. Run it. On first launch it creates `config.json` and `state.json` next to the exe.

## First-run configuration

Right-click the tray icon → **Settings...**

1. **Server** tab — Immich URL, API key, optional **Admin API key**, and the `immich-go` path.
   - The **Admin API key** is only needed to **pause Immich background jobs**. Both keys are passed to `immich-go` through environment variables and are never written to argv or logs.
   - You can seed the keys from `IMMICH_API_KEY` / `IMMICH_ADMIN_API_KEY` before first launch.
2. **Folders & Concurrency** tab — add source folders (validated on save; missing/duplicate paths warn but are allowed), and set the two independent concurrency knobs:
   - **Concurrent processes** — how many `immich-go` processes run at once (one per source folder).
   - **Tasks per process** — immich-go `--concurrent-tasks` inside a single process (1–20).
3. **immich-go Options** tab — the full curated, typed `immich-go` upload flag surface (see **Main window** above), including on-errors behavior, burst/RAW+JPEG/HEIC+JPEG handling, client timeout, recursive scanning, session tagging, SSL verification, overwrite, dry-run, and opt-in job pausing.
4. **Schedule & Watcher** tab — optional weekly incremental scan, optional monthly full rescan, and the real-time watcher (enable + debounce seconds).
5. **Startup & Logs** tab — autostart, start minimized, and the log folder.
6. **Save**.

## Tray menu reference

| Item | Purpose |
|------|---------|
| Status | Live label: Idle / Running / Running (queued) / Paused / Cancelling / Error |
| Watcher | Live watcher state (active / disabled / no existing folders) |
| Open dashboard | Opens the main window (Dashboard tab) |
| Settings... | Opens the main window (Settings tab) |
| Run now | Incremental upload using the per-folder last-success timestamp |
| Run full rescan now | Ignores `state.json`, scans everything |
| Pause / Resume | Blocks new folder processes without cancelling running ones |
| Cancel current run | Kills the running immich-go processes and drops queued work |
| Watch folders in real time | Toggle the recursive file watcher |
| Open log folder / Open state file | Convenience shortcuts |
| Quit | Exits the tray app |

## Main window

The tray app opens a main window (double-click the tray icon, or **Open dashboard**). It has two tabs:

- **Dashboard** — live status (state, trigger, watcher, last run), the same run controls as the tray (**Run now**, **Run full rescan**, **Pause/Resume**, **Cancel**), a real-time **immich-go command preview** (secrets omitted), and an activity log. Every tray action is available here, so the GUI and tray are equivalent.
- **Settings** — the full settings surface, including the **immich-go Options** tab.

The **immich-go Options** tab mirrors the immich-go-gui layout: a *Source & organization* section (album organization, put-all-into-album, burst, RAW+JPEG, HEIC+JPEG) and an *Advanced options* section (recursive, date-from-name, sidecars, Epson FastFoto, folder-as-tags, session tag, API trace, SSL, overwrite, dry-run, job pausing, device UUID, time zone, album path joiner, on-errors, client timeout, include/exclude extensions, media type, log level, skip-file patterns, custom tags). Options are emitted only when enabled; booleans that default to true upstream emit an explicit `=false` when turned off.

Closing the window hides it to the tray; use **Quit** to exit.

## How the efficiency features work

- **Server-side dedup**: `immich-go` computes a SHA-1 of each file and skips anything already on the server. No local hash cache is needed.
- **Per-folder incremental scan** (`state.json`): the orchestrator records `LastSuccessByFolder[folder]` and passes `--date-range=<last-success,today>` so only files modified since the last successful upload are enumerated. On Windows, `LastWriteTime` is what the `immich-go` date range reads.
- **Targeted partial scans**: watcher-triggered runs only scan the folders that actually changed, with a `since` timestamp derived from the changed files.
- **Full scans**: available manually ("Run full rescan now") and on the optional monthly schedule. When the monthly schedule is enabled, an incremental run also auto-upgrades to a full scan if 30+ days have elapsed.
- **Trigger coalescing**: a request that arrives while a run is in progress is merged into the next run instead of being dropped. Unbounded ("all folders") requests absorb targeted ones; multiple targeted requests union their folder lists.
- **Separate concurrency**: process concurrency (how many `immich-go` processes) is independent of per-process task concurrency (`--concurrent-tasks`).
- **Real-time watching**: a recursive `FileSystemWatcher` per folder detects created, modified, and renamed media files. Events are batched in a **fixed window** (the timer is not reset by later files, so a busy folder cannot starve uploads), duplicates are suppressed case-insensitively, and files are checked for read readiness before being handed to `immich-go`. Unready files are retried in the next window.
- **Watcher lifecycle**: changing watcher settings or folders restarts the watcher automatically; the enable toggle also saves immediately from the tray menu.
- **Pause safety**: remote job pausing is opt-in and only emitted when an admin API key is present; otherwise `--pause-immich-jobs=false` is forced and a warning is logged.
- **Secrets out of logs**: API keys are delivered via `IMMICH_GO_UPLOAD_API_KEY` / `IMMICH_GO_UPLOAD_ADMIN_API_KEY`; the logged command line and the GUI command preview never contain them.
- **Full flag surface**: every supported upload-from-folder flag is a typed setting emitted from one place ([`ImmichGoRunner.BuildInvocation`](ImmichGoRunner.cs)); the GUI shows a live command preview so you can see exactly what will run.
- **Excluded system folders** during enumeration and watching: `@eaDir`, `@__thumb`, `.Spotlight-V100`, `.photostructure`, `thumbnails`, `Lightroom Catalog`, `Recently Deleted`, `$RECYCLE.BIN`, `System Volume Information`, plus reparse points, hidden files, and OS noise (`Thumbs.db`, `desktop.ini`, `~$*`).

## Config, state, and schema migration

`config.json` carries a `ConfigVersion`. On load the app **normalizes** every value: out-of-range numbers are clamped (e.g. `--concurrent-tasks` to 1–20, monthly day to 1–28), unknown enum choices fall back to safe defaults, and folders are trimmed/deduplicated. Older config files without the newer fields are upgraded automatically.

`state.json` stores per-folder last-success timestamps plus the last-handled weekly and monthly schedule occurrences (so schedules fire exactly once, even across restarts) and the last run trigger/result.

## Files written at runtime

| Path | Purpose |
|------|---------|
| `config.json` | Settings (server, keys, folders, concurrency, advanced flags, schedule, watcher, autostart) |
| `state.json` | Per-folder last-success timestamps, last full rescan, schedule markers |
| `Logs\upload-<date>-<trigger>.log` | Per-run log including immich-go stdout/stderr |

All live next to the executable (or under the chosen log directory).

## PowerShell fallback

`Upload-Immich.ps1` mirrors the batch behavior (incremental/full, bounded folder parallelism, per-folder state, curated flags, secrets via environment, opt-in pausing). It runs a single batch and exits; **real-time watching is owned by the tray app**. Schedule it with Task Scheduler if you need it unattended:

```powershell
powershell -ExecutionPolicy Bypass -File Upload-Immich.ps1 -FullRescan -PauseJobs
```

## Troubleshooting

- **"The filename, directory name, or volume label syntax is incorrect"** — a folder path contained a stray quote. Remove wrapping quotes; the app escapes automatically.
- **Tray icon doesn't appear** — check Task Manager for `ImmichUploader.exe`. It shows a balloon tip on first run unless launched with `--tray`.
- **Autostart not sticking** — check `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` for the `ImmichUploader` value.
- **Watcher shows "inactive"** — enable it in **Settings → Schedule & Watcher**, and make sure the source folders exist. Missing folders are skipped (and reported in logs).
- **Pause Immich jobs is greyed out** — add an **Admin API key** on the Server tab; pausing is disabled without one.
- **Nothing seems to upload** — open the log folder and inspect the most recent `upload-*.log`.

## Development

```powershell
# Build only
dotnet build -c Release

# Run from source
dotnet run -c Release

# Run the tests
dotnet test -c Release
```

While developing, `config.json` / `state.json` are created under `bin\Release\net8.0-windows\`.

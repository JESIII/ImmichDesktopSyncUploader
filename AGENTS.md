# Immich Uploader — Agent Guide

Authoritative context for AI agents working on the **ImmichUploader** .NET 8 Windows tray app (a sibling of the `immich-go-gui` Python project). When user-provided docs or specs conflict with this file, prefer the user's latest instruction, then update this file if the change is permanent.

---

## 1. Workflow & Execution Philosophy

- **Step-by-step, source-first**: Read the authoritative files ([`ImmichGoRunner.cs`](ImmichGoRunner.cs), [`AppConfig.cs`](AppConfig.cs), [`UploadOrchestrator.cs`](UploadOrchestrator.cs), tests) before editing. Do not guess `immich-go` flag names or CLI behavior.
- **Verify the CLI contract**: `immich-go` 0.32.0 fixture help lives in the Python repo at `../immich-go-gui/core/fixtures/cli_help/0.32.0/`. Consult it before adding a flag; only emit flags that exist there.
- **Minimal scope**: Match existing patterns. Keep this app a thin tray orchestrator that shells out to `immich-go`; avoid drive-by refactors.
- **Keep the PowerShell fallback aligned**: [`Upload-Immich.ps1`](Upload-Immich.ps1) mirrors the batch behavior. Update it when you change upload flags, concurrency semantics, or secret delivery.
- **Always leave the tree green**: build and test before finishing (see §9).

---

## 2. Environment & Tooling

| Tool | Rule |
|------|------|
| **.NET SDK** | 8.0+ (`dotnet`). Target framework is `net8.0-windows`; `UseWindowsForms=true`, `Nullable=enable`, `ImplicitUsings=enable`, `LangVersion=latest`. |
| **Build** | `dotnet build -c Release` — must produce **0 warnings, 0 errors**. |
| **Test** | `dotnet test -c Release` — run from [`ImmichUploader.Tests/`](ImmichUploader.Tests/ImmichUploader.Tests.csproj) (or the app dir once the tests are referenced). |
| **Publish** | Publish-only properties are passed on the command line, never baked into the csproj (see §7). |
| **Inno Setup** | `ISCC.exe ImmichUploader.iss` — only needed to build the installer. |
| **Snyk / audit** | `dotnet list package --vulnerable --include-transitive` is the local dependency audit. |

---

## 3. Architecture & Key Modules

```text
TrayAppContext (WinForms)  →  UploadOrchestrator  →  IUploadRunner  →  immich-go.exe (external)
                                     ↑
                        FolderWatcher (debounced) / Scheduler
```

- **The runner is the only place that builds a command line.** [`ImmichGoRunner.BuildInvocation()`](ImmichGoRunner.cs:40) is the single source of truth for argv + environment. Never assemble `immich-go` arguments anywhere else.
- **Curated typed settings only.** Flags come from typed [`AppConfig`](AppConfig.cs:14) fields — never from arbitrary raw command text.
- **Two independent concurrency knobs.** `MaxParallelProcesses` (simultaneous `immich-go` processes, one per folder) is separate from `Concurrency` (`--concurrent-tasks` *within* a process, 1–20).
- **Secrets never go in argv.** API keys are injected as `IMMICH_GO_*` environment variables (see §5).
- **The tray app owns real-time watching.** The PowerShell script is a batch fallback only.

### Key Modules

| File | Role |
|------|------|
| [`Program.cs`](Program.cs:22) | `TrayAppContext`: tray icon/menu, watcher lifecycle, coalesced run requests, settings wiring; implements `IMainFormHost` |
| [`MainForm.cs`](MainForm.cs:40) | Main window: Dashboard (run controls, status, command preview, activity log) + Settings tab; mirrors every tray action |
| [`SettingsPanel.cs`](SettingsPanel.cs:11) | Shared settings UI (Server / Folders & Concurrency / immich-go Options / Schedule & Watcher / Startup) |
| [`UploadOrchestrator.cs`](UploadOrchestrator.cs:14) | Targeted/full runs, trigger coalescing, process concurrency, cancellation, durable state |
| [`ImmichGoRunner.cs`](ImmichGoRunner.cs:30) | Centralized argv/env construction + process execution |
| [`FolderWatcher.cs`](FolderWatcher.cs:36) | Recursive `FileSystemWatcher`, readiness probe, batch hand-off |
| [`DebounceFileQueue.cs`](DebounceFileQueue.cs:16) | Fixed-window debounce + case-insensitive duplicate suppression |
| [`PathFilters.cs`](PathFilters.cs:9) | Shared exclude/containment rules (used by runner + watcher) |
| [`MediaFileClassifier.cs`](MediaFileClassifier.cs:9) | Image/video extension families |
| [`Scheduler.cs`](Scheduler.cs:9) | Optional weekly partial + monthly full scans; pure `Next*/Previous*/IsDue` helpers |
| [`AppConfig.cs`](AppConfig.cs:14) / [`ConfigStore.cs`](ConfigStore.cs:5) | Settings model, schema version, `Normalize()` migration/clamp, JSON persistence |
| [`StateStore.cs`](StateStore.cs:5) | Per-folder last-success, last full rescan, weekly/monthly handled markers (atomic write) |
| [`SettingsForm.cs`](SettingsForm.cs:11) | Modal settings dialog; thin wrapper over `SettingsPanel` |
| [`IUploadRunner.cs`](IUploadRunner.cs:16) | Runner seam (`UploadRequest`/`UploadResult`) for testing |
| [`Upload-Immich.ps1`](Upload-Immich.ps1:1) | Standalone batch fallback (must stay aligned) |

### Runtime data files

Written next to the executable (or the configured log dir): `config.json`, `state.json`, `Logs\upload-<date>-<trigger>.log`.

---

## 4. Flag Emission Rules

> **A flag reaches the CLI if and only if a typed setting asks for it.** `immich-go` applies its own defaults for anything not passed.

- **Structural, always emit**: `upload from-folder`, `--no-ui`, `--server=`, `--date-range=`, `--concurrent-tasks=`, `--on-errors=`, `--client-timeout=`.
- **Simple options**: `--folder-as-album=`, `--into-album=`, `--manage-burst=`, `--manage-raw-jpeg=`, `--manage-heic-jpeg=`.
- **Advanced options (emit only when enabled/non-default)**: `--recursive`/`--recursive=false`, `--date-from-name=false`, `--ignore-sidecar-files`, `--manage-epson-fastfoto`, `--folder-as-tags`, `--session-tag`, `--api-trace`, `--skip-verify-ssl`, `--device-uuid=`, `--time-zone=`, `--album-path-joiner=`, `--include-type=`, `--include-extensions=`, `--exclude-extensions=`, `--ban-file=` (repeated), `--tag=` (repeated), `--overwrite`, `--dry-run`, `--log-level=`.
- **Pause is opt-in and admin-gated**: emit `--pause-immich-jobs=true` only when `PauseImmichJobs` **and** an admin key are present; otherwise force `--pause-immich-jobs=false` and add a warning.
- **Clamp before emitting**: `--concurrent-tasks` is 1–20; `--date-range` is `<from>,<to>` (`yyyy-MM-dd`). Full scans pass a far-past `from` (not `DateTime.MinValue`).
- **Known CI trap**: after the app-level config, each flag must be emitted exactly once. Do not duplicate `--date-range`/`--pause-immich-jobs` when refactoring.

---

## 5. Security & Secret Handling

| Concern | Mitigation |
|---------|------------|
| Keys in argv / logs | Env vars only: `IMMICH_GO_UPLOAD_API_KEY`, `IMMICH_GO_UPLOAD_ADMIN_API_KEY`. The logged command ([`DisplayCommand`](ImmichGoRunner.cs:21)) never contains them. |
| Keys in config | Stored in `config.json`; may be seeded from `IMMICH_API_KEY` / `IMMICH_ADMIN_API_KEY` at startup. |
| Job pausing | Requires an admin key; the UI disables the checkbox without one and the runner forces pausing off. |
| Watcher trust | Events are filtered to configured folders (boundary-safe), media extensions, and non-excluded paths before queueing. |

**Redaction rule:** logs show env var **names**, never values. Never log the raw API key.

---

## 6. Testing

- **Suite Metrics**: **59 tests across 6 test classes** (`ArgumentGenerationTests`, `ConfigMigrationTests`, `DebounceQueueTests`, `SchedulingTests`, `OrchestratorTests`, `PathAndWatcherTests`).
- **Conventions**:
  - The orchestrator is exercised through [`UploadRequest`](IUploadRunner.cs:16) and a `FakeRunner` (see [`TestSupport.cs`](ImmichUploader.Tests/TestSupport.cs:45)); never shell out to a real `immich-go` in tests.
  - Inject `loadState`/`saveState` into `UploadOrchestrator`/`Scheduler` to keep tests off the filesystem.
  - Prefer pure static helpers for time/debounce logic (`Scheduler.PreviousWeekly`, `DebounceFileQueue`) so tests are deterministic.
  - Use generous waits (`ManualResetEventSlim` + seconds, not milliseconds) for concurrency tests.
  - Use throwaway directories via `TempFolder`.
- **When adding a flag or setting**: extend `AppConfig`, emit it in `BuildInvocation`, surface it in `SettingsForm`, mirror it in `Upload-Immich.ps1`, and add an `ArgumentGenerationTests` case (including a secret/absence assertion).

---

## 7. CI/CD, Packaging & Versioning

- **Version lives in two places** — keep them in sync: `<Version>` in [`ImmichUploader.csproj`](ImmichUploader.csproj:15) and `#define MyAppVersion` in [`ImmichUploader.iss`](ImmichUploader.iss:9).
- **Publish settings are CLI-only.** `PublishSingleFile`, `SelfContained`, `RuntimeIdentifier`, and compression are **not** set in the csproj, so plain builds and the test project reference a RID-less, framework-dependent assembly. The publish command supplies them:

  ```powershell
  dotnet publish -c Release -r win-x64 --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true
  ```

- **Tests are not packaged.** The `ImmichUploader.Tests` folder is excluded from the app's compile glob and from the installer file list.
- **Installer**: bundles `ImmichUploader.exe`, `setup.ps1`, and `Upload-Immich.ps1`, plus `immich-go.exe` when present (ISPP `#if FileExists`). Builds are **unsigned**; SmartScreen warnings are expected.
- **Upgrade hardening**: the installer uses `CloseApplications=yes`, `RestartApplications=no`, and `AppMutex=ImmichUploader.SingleInstance`. That mutex name **must** match `Program.SingleInstanceMutexName`; changing one without the other breaks clean upgrades. A `[Code]` guard blocks accidental downgrades.
- **Upgrades preserve data**: `config.json`, `state.json`, and `Logs\` are not in `[Files]`, so they survive upgrades; `[UninstallDelete]` removes them on uninstall.

---

## 8. Common Pitfalls

1. **Building argv outside [`BuildInvocation`](ImmichGoRunner.cs:40)** — always extend the one centralized builder.
2. **Putting a secret in argv or a log line** — use env vars and the masked display command.
3. **Conflating the two concurrency knobs** — process concurrency ≠ `--concurrent-tasks`.
4. **Resetting the debounce timer on every file** — the window is **fixed** (started on the empty→non-empty transition); a sliding window can starve uploads. Preserve this in [`DebounceFileQueue`](DebounceFileQueue.cs:16).
5. **Touching UI/non-thread-safe state from the watcher thread** — watcher batches arrive on a worker thread; marshal via `SynchronizationContext`/`RequestRun` (which locks internally).
6. **Forgetting the pause gate release / semaphore release** — the orchestrator uses `try/finally`; keep it that way or runs hang.
7. **Adding publish/RID properties back into the csproj** — this breaks the test reference and plain builds.
8. **Skipping `Normalize()`** — new settings must be clamped/defaulted so migrated or hand-edited `config.json` files cannot crash a run.
9. **Editing `Upload-Immich.ps1` without re-checking syntax** — parse it before finishing (see §9).
10. **Duplicating settings controls** — all settings live in [`SettingsPanel`](SettingsPanel.cs:11); `MainForm` and `SettingsForm` only host it. Add a control once, in the panel.
11. **Adding a flag in only one place** — a new flag needs: `AppConfig` field + `Normalize`, emission in `BuildInvocation`, a control in `SettingsPanel`, a mirror in `Upload-Immich.ps1`, and an `ArgumentGenerationTests` case.

---

## 9. Useful Commands

```powershell
# Build (0 warnings / 0 errors expected)
dotnet build -c Release

# Run the app from source
dotnet run -c Release

# Tests (52 expected) — run from the test project dir
dotnet test -c Release

# Dependency vulnerability audit
dotnet list package --vulnerable --include-transitive

# Publish a self-contained single-file exe
dotnet publish -c Release -r win-x64 --self-contained true `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:EnableCompressionInSingleFile=true

# Build the installer
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" ImmichUploader.iss

# Validate the PowerShell fallback parses
pwsh -NoProfile -Command "`$t=`$null;`$e=`$null;[void][System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path 'Upload-Immich.ps1'),[ref]`$t,[ref]`$e); if(`$e.Count){`$e|%{Write-Host `$_.Message}}else{'PS SYNTAX OK'}"
```

---

## 10. Runtime Invariants

- **Config is normalized on every load/save** — [`AppConfig.Normalize()`](AppConfig.cs:139) clamps ranges, fixes enum choices, and dedupes folders. `ConfigVersion` tracks schema changes.
- **Run state is durable** — [`StateStore.Save()`](StateStore.cs:60) writes atomically; per-folder last-success and schedule markers survive restarts.
- **Schedules fire exactly once** — the scheduler compares the most recent past occurrence against a persisted handled marker; markers are baselined at startup so a fresh install or restart does not fire a catch-up run.
- **Requests coalesce, never drop** — a run request during an active run is merged; unbounded ("all folders") requests absorb targeted ones.
- **Pause blocks new processes only** — in-flight `immich-go` processes finish; cancel kills the process tree and clears any coalesced request.
- **Full scans are explicit** — manual trigger, monthly schedule, or an incremental run that auto-upgrades when the monthly schedule is enabled and 30+ days have elapsed.

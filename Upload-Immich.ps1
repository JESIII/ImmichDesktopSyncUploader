# Immich Uploader - standalone PowerShell fallback
#
# Mirrors the C# tray app's *batch* behavior:
#   - bounded parallel source folders (separate process concurrency)
#   - per-folder incremental scan (state file tracks last success per folder)
#   - optional full rescan to pick up renamed/moved files
#   - the same curated immich-go upload flags as the GUI (no raw command text)
#   - secrets passed via environment variables, never argv or logs
#   - job pausing is opt-in and requires an admin API key
#
# Real-time folder watching is owned by the tray app. This script intentionally
# runs a single batch and exits; schedule it with Task Scheduler if needed.
#
# Run directly:
#   powershell -ExecutionPolicy Bypass -File Upload-Immich.ps1
#
# Or with overrides:
#   powershell -ExecutionPolicy Bypass -File Upload-Immich.ps1 -FullRescan -PauseJobs

[CmdletBinding()]
param(
    [switch]$FullRescan,
    [int]$MaxParallelProcesses = 3,
    [int]$Concurrency = 4,
    [int]$DaysBack = 7,
    [string]$ImmichServer = "http://192.168.1.119:2283",
    [string]$ApiKey = "",
    [string]$AdminApiKey = "",
    [switch]$PauseJobs,

    # immich-go: simple options
    [ValidateSet("NONE", "FOLDER", "PATH")][string]$FolderAsAlbum = "NONE",
    [string]$IntoAlbum = "",
    [ValidateSet("NoStack", "Stack", "StackKeepRaw", "StackKeepJPEG")][string]$ManageBurst = "Stack",
    [ValidateSet("NoStack", "KeepRaw", "KeepJPG", "StackCoverRaw", "StackCoverJPG")][string]$ManageRawJpeg = "StackCoverRaw",
    [ValidateSet("NoStack", "KeepHeic", "KeepJPG", "StackCoverHeic", "StackCoverJPG")][string]$ManageHeicJpeg = "NoStack",

    # immich-go: advanced options
    [switch]$NoRecursive,
    [switch]$NoDateFromName,
    [switch]$IgnoreSidecarFiles,
    [switch]$ManageEpsonFastFoto,
    [switch]$FolderAsTags,
    [switch]$NoSessionTag,
    [switch]$ApiTrace,
    [switch]$SkipSslVerify,
    [switch]$Overwrite,
    [switch]$DryRun,
    [string]$DeviceUuid = "",
    [string]$TimeZone = "",
    [string]$AlbumPathJoiner = "",
    [string]$OnErrors = "continue",
    [int]$ClientTimeoutMinutes = 60,
    [string]$IncludeExtensions = "",
    [string]$ExcludeExtensions = "",
    [string]$BanFiles = "",
    [string]$Tags = "",
    [ValidateSet("all", "IMAGE", "VIDEO")][string]$IncludeType = "all",
    [ValidateSet("", "INFO", "DEBUG", "WARN", "ERROR")][string]$LogLevel = "",

    [string[]]$Folders = @(
        "G:\zirjo\Pictures",
        "G:\zirjo\Videos",
        "C:\Users\zirjo\Videos"
    ),
    [string]$LogDir = "",
    [string]$ImmichGoPath = "immich-go",
    [string]$StateFile = ""
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $LogDir)    { $LogDir    = Join-Path $ScriptDir 'Logs' }
if (-not $StateFile) { $StateFile = Join-Path $ScriptDir 'state.json' }

if (-not $ApiKey)      { $ApiKey      = $env:IMMICH_API_KEY }
if (-not $AdminApiKey) { $AdminApiKey = $env:IMMICH_ADMIN_API_KEY }
if (-not $ApiKey) { throw "API key not provided. Set -ApiKey or the IMMICH_API_KEY env var." }

# immich-go documents --concurrent-tasks as 1-20.
$Concurrency = [Math]::Max(1, [Math]::Min(20, $Concurrency))
$MaxParallelProcesses = [Math]::Max(1, $MaxParallelProcesses)

# Job pausing is opt-in and requires an admin key; otherwise it is forced off.
$pause = [bool]$PauseJobs -and [bool]$AdminApiKey
if ($PauseJobs -and -not $AdminApiKey) {
    Write-Warning "PauseJobs requires -AdminApiKey; pausing is disabled for this run."
}

# Secrets travel through the child process environment, never argv/logs.
$env:IMMICH_GO_UPLOAD_API_KEY = $ApiKey
if ($AdminApiKey) { $env:IMMICH_GO_UPLOAD_ADMIN_API_KEY = $AdminApiKey }

if (-not (Test-Path $LogDir)) { New-Item -ItemType Directory -Path $LogDir -Force | Out-Null }

function Load-State {
    if (Test-Path $StateFile) {
        try { return Get-Content -LiteralPath $StateFile -Raw | ConvertFrom-Json }
        catch { return [pscustomobject]@{ LastSuccessByFolder = @{}; LastFullRescan = $null } }
    }
    return [pscustomobject]@{ LastSuccessByFolder = @{}; LastFullRescan = $null }
}

function Save-State($state) {
    $state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $StateFile -Encoding UTF8
}

function Split-Csv([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return @() }
    return @($value -split '[,;]' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

function Split-Lines([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return @() }
    return @($value -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

# Single source of truth for the immich-go argv (mirrors ImmichGoRunner.BuildInvocation).
function New-ImmichGoArguments([string]$folder, [datetime]$sinceUtc, [bool]$isFull) {
    $to = (Get-Date).ToUniversalTime()
    $from = if ($isFull) { $to.AddYears(-50) } else { $sinceUtc }
    $dateRange = "{0:yyyy-MM-dd},{1:yyyy-MM-dd}" -f $from, $to

    $list = [System.Collections.Generic.List[string]]::new()
    foreach ($arg in @(
            'upload', 'from-folder', '--no-ui',
            "--server=$ImmichServer",
            $(if ($NoRecursive) { '--recursive=false' } else { '--recursive' }),
            "--date-range=$dateRange",
            "--concurrent-tasks=$Concurrency",
            "--on-errors=$OnErrors",
            "--client-timeout=${ClientTimeoutMinutes}m"
        )) {
        [void]$list.Add($arg)
    }

    if ($LogLevel) { [void]$list.Add("--log-level=$LogLevel") }

    # Simple options
    if ($FolderAsAlbum -ne 'NONE') { [void]$list.Add("--folder-as-album=$FolderAsAlbum") }
    if ($IntoAlbum)                { [void]$list.Add("--into-album=$IntoAlbum") }
    if ($ManageBurst)              { [void]$list.Add("--manage-burst=$ManageBurst") }
    if ($ManageRawJpeg)            { [void]$list.Add("--manage-raw-jpeg=$ManageRawJpeg") }
    if ($ManageHeicJpeg -ne 'NoStack') { [void]$list.Add("--manage-heic-jpeg=$ManageHeicJpeg") }

    # Advanced options
    if ($NoDateFromName)      { [void]$list.Add('--date-from-name=false') }
    if ($IgnoreSidecarFiles)  { [void]$list.Add('--ignore-sidecar-files') }
    if ($ManageEpsonFastFoto) { [void]$list.Add('--manage-epson-fastfoto') }
    if ($FolderAsTags)        { [void]$list.Add('--folder-as-tags') }
    if (-not $NoSessionTag)   { [void]$list.Add('--session-tag') }
    if ($ApiTrace)            { [void]$list.Add('--api-trace') }
    if ($SkipSslVerify)       { [void]$list.Add('--skip-verify-ssl') }
    if ($DeviceUuid)          { [void]$list.Add("--device-uuid=$DeviceUuid") }
    if ($TimeZone)            { [void]$list.Add("--time-zone=$TimeZone") }
    if ($AlbumPathJoiner)     { [void]$list.Add("--album-path-joiner=$AlbumPathJoiner") }
    if ($IncludeType -ne 'all') { [void]$list.Add("--include-type=$IncludeType") }
    if ($IncludeExtensions)   { [void]$list.Add("--include-extensions=$IncludeExtensions") }
    if ($ExcludeExtensions)   { [void]$list.Add("--exclude-extensions=$ExcludeExtensions") }
    foreach ($pattern in Split-Lines $BanFiles) { [void]$list.Add("--ban-file=$pattern") }
    foreach ($tag in Split-Csv $Tags)           { [void]$list.Add("--tag=$tag") }

    if ($Overwrite) { [void]$list.Add('--overwrite'); Write-Warning "Overwrite mode replaces existing files on the server." }
    if ($DryRun)    { [void]$list.Add('--dry-run');   Write-Warning "Dry-run mode is enabled: nothing will be uploaded." }

    [void]$list.Add("--pause-immich-jobs=$(if ($pause) { 'true' } else { 'false' })")
    [void]$list.Add($folder)
    return , $list.ToArray()
}

$state = Load-State
$today = (Get-Date).Date
$daysSinceFull = if ($state.LastFullRescan) { [int]($today - ([datetime]$state.LastFullRescan).Date).TotalDays } else { [int]::MaxValue }
$doFull = [bool]$FullRescan -or $daysSinceFull -ge 30
if ($doFull) { $state.LastFullRescan = (Get-Date).ToUniversalTime() }
Save-State $state

$date = Get-Date -Format "yyyy-MM-dd-HHmmss"
$trigger = if ($FullRescan) { "manual-full" } else { "manual" }
$logFile = Join-Path $LogDir "upload-$date-$trigger.log"
"Run started: $(Get-Date -Format o) trigger=$trigger fullRescan=$doFull processes=$MaxParallelProcesses tasks=$Concurrency" |
    Set-Content -LiteralPath $logFile

$folderParallelism = [Math]::Max(1, [Math]::Min($MaxParallelProcesses, $Folders.Count))

$runspacePool = [runspacefactory]::CreateRunspacePool(1, $folderParallelism)
$runspacePool.Open()

$powershellInstances = New-Object 'System.Collections.Generic.List[System.Management.Automation.PowerShell]'

foreach ($folder in $Folders) {
    $sinceUtc = if ($doFull) {
        [datetime]::MinValue
    }
    elseif ($state.LastSuccessByFolder.$folder) {
        [datetime]$state.LastSuccessByFolder.$folder
    }
    else {
        (Get-Date).ToUniversalTime().AddDays(-$DaysBack)
    }

    $argList = New-ImmichGoArguments -folder $folder -sinceUtc $sinceUtc -isFull $doFull

    $ps = [powershell]::Create()
    $ps.RunspacePool = $runspacePool
    $ps.AddScript({
            param($folder, $argList, $logFile, $immichGoPath)
            try {
                if (-not (Test-Path -LiteralPath $folder)) {
                    Write-Host "[warn] Source path does not exist: $folder"
                    return [pscustomobject]@{ Folder = $folder; Success = $false }
                }
                $p = Start-Process -FilePath $immichGoPath -ArgumentList $argList -NoNewWindow -PassThru `
                    -RedirectStandardOutput "$logFile.stdout" -RedirectStandardError "$logFile.stderr" -Wait
                Get-Content "$logFile.stdout", "$logFile.stderr" -ErrorAction SilentlyContinue | Add-Content -LiteralPath $logFile
                Remove-Item "$logFile.stdout", "$logFile.stderr" -ErrorAction SilentlyContinue
                return [pscustomobject]@{ Folder = $folder; Success = ($p.ExitCode -eq 0) }
            }
            catch {
                Write-Host "[error] $($_.Exception.Message)"
                return [pscustomobject]@{ Folder = $folder; Success = $false }
            }
        }).AddArgument($folder) | Out-Null
    $ps.AddArgument($argList) | Out-Null
    $ps.AddArgument($logFile) | Out-Null
    $ps.AddArgument($ImmichGoPath) | Out-Null

    $powershellInstances.Add($ps)
}

$asyncResults = @()
foreach ($ps in $powershellInstances) { $asyncResults += $ps.BeginInvoke() }

$completed = 0
for ($i = 0; $i -lt $powershellInstances.Count; $i++) {
    $ps = $powershellInstances[$i]
    $ar = $asyncResults[$i]
    $out = $ps.EndInvoke($ar)
    if ($out -is [pscustomobject]) {
        if ($out.Success -and -not $doFull) {
            $state.LastSuccessByFolder.$($out.Folder) = (Get-Date).ToUniversalTime().ToString('o')
        }
        if ($out.Success) { $completed++ }
    }
    $ps.Dispose()
}
Save-State $state
$runspacePool.Close()
$runspacePool.Dispose()

"`nRun finished: $(Get-Date -Format o) success=$completed/$($powershellInstances.Count)" |
    Add-Content -LiteralPath $logFile
Write-Host "Log: $logFile"

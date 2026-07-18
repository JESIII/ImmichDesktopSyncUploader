# Immich Uploader - standalone PowerShell version
# Mirrors the C# tray app's behavior:
#   - Parallel source folders (bounded)
#   - Per-folder incremental scan (state file tracks last successful run per folder)
#   - Monthly full rescan to pick up renamed/moved files
#   - Configurable concurrency (passed to immich-go)
#
# Run directly:
#   powershell -ExecutionPolicy Bypass -File Upload-Immich.ps1
#
# Or with overrides:
#   powershell -ExecutionPolicy Bypass -File Upload-Immich.ps1 -FullRescan

[CmdletBinding()]
param(
    [switch]$FullRescan,
    [int]$Concurrency = 4,
    [int]$DaysBack = 7,
    [string]$ImmichServer = "http://192.168.1.119:2283",
    [string]$ApiKey = "",
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
if (-not $LogDir)  { $LogDir  = Join-Path $ScriptDir 'Logs' }
if (-not $StateFile) { $StateFile = Join-Path $ScriptDir 'state.json' }

if (-not $ApiKey) { $ApiKey = $env:IMMICH_API_KEY }
if (-not $ApiKey) { throw "API key not provided. Set -ApiKey or the IMMICH_API_KEY env var." }

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

function Get-Files-Changed-Since($folder, $sinceUtc) {
    $exclude = @('@eaDir', '@__thumb', '.Spotlight-V100', '.photostructure', 'thumbnails', 'Lightroom Catalog', 'Recently Deleted', '$RECYCLE.BIN', 'System Volume Information')
    $results = [System.Collections.Generic.List[string]]::new()
    $stack = [System.Collections.Generic.Stack[System.IO.DirectoryInfo]]::new()
    $stack.Push((Get-Item -LiteralPath $folder))
    while ($stack.Count -gt 0) {
        $dir = $stack.Pop()
        try {
            foreach ($sub in $dir.GetDirectories()) {
                if ($exclude -contains $sub.Name) { continue }
                if (($sub.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq [System.IO.FileAttributes]::ReparsePoint) { continue }
                $stack.Push($sub)
            }
        } catch {}
        try {
            foreach ($f in $dir.GetFiles()) {
                try {
                    if ($f.LastWriteTimeUtc -ge $sinceUtc) { $results.Add($f.FullName) }
                } catch {}
            }
        } catch {}
    }
    return ,$results
}

function Invoke-Upload($folder, $sinceUtc, $logFile) {
    $dateRange = "{0:yyyy-MM-dd},{1:yyyy-MM-dd}" -f $sinceUtc, (Get-Date).ToUniversalTime()
    $argList = @(
        'upload', 'from-folder',
        '--no-ui',
        "--server=$ImmichServer",
        "--api-key=$ApiKey",
        '--recursive',
        '--manage-raw-jpeg=StackCoverRaw',
        "--date-range=$dateRange",
        "--concurrent-tasks=$Concurrency",
        '--on-errors', 'continue',
        '--pause-immich-jobs=true',
        '--manage-burst', 'Stack',
        '--client-timeout', '60m',
        '--session-tag',
        "$folder"
    )
    Write-Host "Uploading: $folder (since $sinceUtc)"
    $p = Start-Process -FilePath $ImmichGoPath -ArgumentList $argList -NoNewWindow -PassThru `
        -RedirectStandardOutput "$logFile.stdout" -RedirectStandardError "$logFile.stderr" `
        -Wait
    Get-Content "$logFile.stdout","$logFile.stderr" -ErrorAction SilentlyContinue | Add-Content -LiteralPath $logFile
    Remove-Item "$logFile.stdout","$logFile.stderr" -ErrorAction SilentlyContinue
    return $p.ExitCode -eq 0
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
"Run started: $(Get-Date -Format o) trigger=$trigger fullRescan=$doFull" | Set-Content -LiteralPath $logFile

$folderParallelism = [Math]::Max(1, [Math]::Min(3, [Environment]::ProcessorCount / 2))
$folderGate = [System.Threading.SemaphoreSlim]::new($folderParallelism, $folderParallelism)
$results = [System.Collections.Concurrent.ConcurrentBag[psobject]]::new()

$jobs = foreach ($folder in $Folders) {
    [pscustomobject]@{ Folder = $folder; Runspace = $null; Handle = $null }
}

$runspacePool = [runspacefactory]::CreateRunspacePool(1, $folderParallelism)
$runspacePool.Open()

$powershellInstances = New-Object 'System.Collections.Generic.List[System.Management.Automation.PowerShell]'

foreach ($job in $jobs) {
    $ps = [powershell]::Create()
    $ps.RunspacePool = $runspacePool
    $ps.AddScript({
        param($folder, $sinceUtc, $logFile, $Concurrency, $ImmichServer, $ApiKey, $ImmichGoPath, $doFull)
        try {
            if (-not (Test-Path -LiteralPath $folder)) {
                Write-Host "[warn] Source path does not exist: $folder"
                return [pscustomobject]@{ Folder = $folder; Success = $false }
            }
            $dateRange = "{0:yyyy-MM-dd},{1:yyyy-MM-dd}" -f $sinceUtc, (Get-Date).ToUniversalTime()
            $argList = @(
                'upload', 'from-folder', '--no-ui',
                "--server=$ImmichServer", "--api-key=$ApiKey",
                '--recursive', '--manage-raw-jpeg=StackCoverRaw',
                "--date-range=$dateRange", "--concurrent-tasks=$Concurrency",
                '--on-errors', 'continue', '--pause-immich-jobs=true',
                '--manage-burst', 'Stack', '--client-timeout', '60m',
                '--session-tag', "$folder"
            )
            $p = Start-Process -FilePath $ImmichGoPath -ArgumentList $argList -NoNewWindow -PassThru `
                -RedirectStandardOutput "$logFile.stdout" -RedirectStandardError "$logFile.stderr" -Wait
            Get-Content "$logFile.stdout","$logFile.stderr" -ErrorAction SilentlyContinue | Add-Content -LiteralPath $logFile
            Remove-Item "$logFile.stdout","$logFile.stderr" -ErrorAction SilentlyContinue
            return [pscustomobject]@{ Folder = $folder; Success = ($p.ExitCode -eq 0) }
        } catch {
            Write-Host "[error] $($_.Exception.Message)"
            return [pscustomobject]@{ Folder = $folder; Success = $false }
        }
    }).AddArgument($job.Folder) | Out-Null

    $sinceUtc = if ($doFull) {
        [datetime]::MinValue
    } elseif ($state.LastSuccessByFolder.$($job.Folder)) {
        [datetime]$state.LastSuccessByFolder.$($job.Folder)
    } else {
        (Get-Date).ToUniversalTime().AddDays(-$DaysBack)
    }
    $folderLog = $logFile
    $ps.AddArgument($sinceUtc) | Out-Null
    $ps.AddArgument($folderLog) | Out-Null
    $ps.AddArgument($Concurrency) | Out-Null
    $ps.AddArgument($ImmichServer) | Out-Null
    $ps.AddArgument($ApiKey) | Out-Null
    $ps.AddArgument($ImmichGoPath) | Out-Null
    $ps.AddArgument($doFull) | Out-Null

    $powershellInstances.Add($ps)
}

$asyncResults = @()
foreach ($ps in $powershellInstances) {
    $asyncResults += $ps.BeginInvoke()
}

$completed = 0
foreach ($i in 0..($powershellInstances.Count - 1)) {
    $ps = $powershellInstances[$i]
    $ar = $asyncResults[$i]
    $out = $ps.EndInvoke($ar)
    if ($out -is [pscustomobject]) {
        if ($out.Success -and -not $doFull) {
            $state.LastSuccessByFolder.$($out.Folder) = (Get-Date).ToUniversalTime().ToString('o')
        }
    }
    $ps.Dispose()
}
Save-State $state
$runspacePool.Close()
$runspacePool.Dispose()

"`nRun finished: $(Get-Date -Format o)" | Add-Content -LiteralPath $logFile
Write-Host "Log: $logFile"

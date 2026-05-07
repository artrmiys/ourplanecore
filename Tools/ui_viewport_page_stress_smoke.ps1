param(
    [string]$ProjectRoot = "",
    [Parameter(Mandatory)] [string]$JobPath,
    [switch]$CopyJob,
    [int]$TimeoutSeconds = 180,
    [int]$PageTimeoutMs = 8000,
    [int]$ReturnCount = 6,
    [int]$TabCount = 5,
    [switch]$KeepAppOpen
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Join-Path $PSScriptRoot ".."
}
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path

function Get-PageFolders {
    param([Parameter(Mandatory)] [string]$Root)

    Get-ChildItem -LiteralPath (Join-Path $Root "Pages") -Recurse -Filter source.json |
        ForEach-Object { Split-Path -Parent $_.FullName } |
        Sort-Object
}

function Copy-SmokeJob {
    param([Parameter(Mandatory)] [string]$SourceJob)

    $root = Join-Path $env:TEMP ("opc_viewport_page_stress_" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Copy-Item -LiteralPath $SourceJob -Destination $root -Recurse -Force
    $copied = Join-Path $root (Split-Path -Leaf $SourceJob)
    $lockPath = Join-Path $copied ".~lock"
    if (Test-Path -LiteralPath $lockPath) {
        Remove-Item -LiteralPath $lockPath -Force -ErrorAction SilentlyContinue
    }

    [pscustomobject]@{
        Root = $root
        Job = $copied
    }
}

function Set-SmokeSettings {
    param(
        [Parameter(Mandatory)] [string]$SmokeJobPath,
        [Parameter(Mandatory)] [string]$FirstPagePath
    )

    $settingsDir = Join-Path $env:APPDATA "OurPlaneCore"
    $settingsPath = Join-Path $settingsDir "settings.json"
    $backupPath = "$settingsPath.viewport-page-stress-smoke.bak"
    New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null
    if (Test-Path -LiteralPath $settingsPath) {
        Copy-Item -LiteralPath $settingsPath -Destination $backupPath -Force
    }

    $settings = [ordered]@{
        JobsRootPath = (Split-Path -Parent $SmokeJobPath)
        JobsRootPaths = @((Split-Path -Parent $SmokeJobPath))
        LastJobPath = $SmokeJobPath
        LastPageFolder = $FirstPagePath
        UnitMode = "Imperial"
        Theme = "Dark"
        ViewportBackground = "#FFFFFF"
        ShowMeasurementLabels = $true
        ShowLineLabels = $true
        ShowAreaLabels = $true
        ShowCountLabels = $false
        ShowSheetLegend = $true
        SimplifyViewportNavigation = $false
        RecentJobs = @()
    }
    $settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $settingsPath -Encoding UTF8

    [pscustomobject]@{
        Path = $settingsPath
        Backup = $backupPath
    }
}

function Restore-SmokeSettings {
    param($State)
    if ($null -eq $State) { return }
    if (Test-Path -LiteralPath $State.Backup) {
        Copy-Item -LiteralPath $State.Backup -Destination $State.Path -Force
        Remove-Item -LiteralPath $State.Backup -Force
    }
}

function Summarize-Results {
    param([Parameter(Mandatory)] $Report)

    $all = @()
    $all += @($Report.OpenResults)
    $all += @($Report.ReturnResults)
    $all += @($Report.TabResults)
    $all += @($Report.TabReturnResults)
    $slowest = @($all | Sort-Object -Property ElapsedMs -Descending | Select-Object -First 5)
    $max = 0
    $maxReady = 0
    if ($all.Count -gt 0) {
        $max = ($all | Measure-Object -Property ElapsedMs -Maximum).Maximum
        $maxReady = ($all | Measure-Object -Property RenderReadyMs -Maximum).Maximum
    }

    Write-Host "Viewport page stress smoke report:" -ForegroundColor Cyan
    Write-Host "  pages opened: $($Report.PageCount)"
    Write-Host "  return checks: $(@($Report.ReturnResults).Count)"
    Write-Host "  new tab checks: $(@($Report.TabResults).Count)"
    Write-Host "  tab return checks: $(@($Report.TabReturnResults).Count)"
    Write-Host "  max render ready: $maxReady ms"
    Write-Host "  max step: $max ms"
    foreach ($item in $slowest) {
        Write-Host ("  slow: {0} {1} ready {2} ms total {3} ms" -f $item.Stage, $item.PageName, $item.RenderReadyMs, $item.ElapsedMs)
    }
}

$resolvedJob = (Resolve-Path -LiteralPath $JobPath).Path
$jobState = $null
$settingsState = $null
$proc = $null
$reportPath = Join-Path $env:TEMP ("opc_viewport_page_stress_report_" + [guid]::NewGuid().ToString("N") + ".json")
$oldSmoke = $env:OURPLANECORE_VIEWPORT_PAGE_STRESS_SMOKE
$oldReport = $env:OURPLANECORE_VIEWPORT_PAGE_STRESS_REPORT
$oldTimeout = $env:OURPLANECORE_VIEWPORT_PAGE_STRESS_TIMEOUT_MS
$oldReturn = $env:OURPLANECORE_VIEWPORT_PAGE_STRESS_RETURN_COUNT
$oldTabs = $env:OURPLANECORE_VIEWPORT_PAGE_STRESS_TAB_COUNT
$stdoutPath = Join-Path $env:TEMP ("opc_viewport_page_stress_stdout_" + [guid]::NewGuid().ToString("N") + ".txt")
$stderrPath = Join-Path $env:TEMP ("opc_viewport_page_stress_stderr_" + [guid]::NewGuid().ToString("N") + ".txt")

try {
    if ($CopyJob) {
        $jobState = Copy-SmokeJob -SourceJob $resolvedJob
        $smokeJob = $jobState.Job
    } else {
        $smokeJob = $resolvedJob
    }

    $pages = @(Get-PageFolders -Root $smokeJob)
    if ($pages.Count -eq 0) {
        throw "No page folders with source.json were found under $smokeJob."
    }

    $settingsState = Set-SmokeSettings -SmokeJobPath $smokeJob -FirstPagePath $pages[0]
    $env:OURPLANECORE_VIEWPORT_PAGE_STRESS_SMOKE = "1"
    $env:OURPLANECORE_VIEWPORT_PAGE_STRESS_REPORT = $reportPath
    $env:OURPLANECORE_VIEWPORT_PAGE_STRESS_TIMEOUT_MS = [string]$PageTimeoutMs
    $env:OURPLANECORE_VIEWPORT_PAGE_STRESS_RETURN_COUNT = [string]$ReturnCount
    $env:OURPLANECORE_VIEWPORT_PAGE_STRESS_TAB_COUNT = [string]$TabCount

    $appDll = Join-Path $ProjectRoot "cache\verify_build\ourplanecore.dll"
    if (Test-Path -LiteralPath $appDll) {
        $proc = Start-Process -FilePath "dotnet" -ArgumentList @($appDll) -WorkingDirectory $ProjectRoot -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    } else {
        $projectPath = Join-Path $ProjectRoot "ourplanecore.csproj"
        $proc = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--no-restore", "--project", $projectPath) -WorkingDirectory $ProjectRoot -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not $proc.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 250
        $proc.Refresh()
    }
    if ($proc.HasExited) {
        try { $proc.WaitForExit() } catch {}
        $proc.Refresh()
    }

    if (-not $proc.HasExited) {
        if (-not $KeepAppOpen) {
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        }
        throw "Viewport page stress smoke timed out after $TimeoutSeconds seconds."
    }

    if (-not (Test-Path -LiteralPath $reportPath)) {
        if (Test-Path -LiteralPath $stdoutPath) {
            Get-Content -LiteralPath $stdoutPath -Tail 40 | ForEach-Object { Write-Host "  stdout: $_" }
        }
        if (Test-Path -LiteralPath $stderrPath) {
            Get-Content -LiteralPath $stderrPath -Tail 40 | ForEach-Object { Write-Host "  stderr: $_" -ForegroundColor Red }
        }
        throw "Viewport page stress smoke did not write a report. Exit code: $($proc.ExitCode)."
    }

    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    Summarize-Results -Report $report
    $exitCode = if ($null -eq $proc.ExitCode) { 0 } else { $proc.ExitCode }
    if ($exitCode -ne 0 -or -not $report.Passed) {
        foreach ($failure in @($report.Failures)) {
            Write-Host "  failure: $failure" -ForegroundColor Red
        }
        throw "Viewport page stress smoke failed with exit code $exitCode."
    }

    Write-Host "PASS viewport page stress smoke: opened $($report.PageCount) pages, returned to samples, opened new tabs, and all viewport opacity probes passed." -ForegroundColor Green
}
finally {
    $env:OURPLANECORE_VIEWPORT_PAGE_STRESS_SMOKE = $oldSmoke
    $env:OURPLANECORE_VIEWPORT_PAGE_STRESS_REPORT = $oldReport
    $env:OURPLANECORE_VIEWPORT_PAGE_STRESS_TIMEOUT_MS = $oldTimeout
    $env:OURPLANECORE_VIEWPORT_PAGE_STRESS_RETURN_COUNT = $oldReturn
    $env:OURPLANECORE_VIEWPORT_PAGE_STRESS_TAB_COUNT = $oldTabs
    Restore-SmokeSettings $settingsState
    if ($jobState -ne $null -and -not $KeepAppOpen) {
        Remove-Item -LiteralPath $jobState.Root -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $reportPath) {
        Remove-Item -LiteralPath $reportPath -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $stdoutPath) {
        Remove-Item -LiteralPath $stdoutPath -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $stderrPath) {
        Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue
    }
}

param(
    [string]$ProjectRoot = "",
    [string]$ExePath = "",
    [int]$TimeoutSeconds = 120,
    [string]$ReportPath = "",
    [switch]$KeepArtifacts
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Join-Path $PSScriptRoot ".."
}
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$resolvedExe = if ([string]::IsNullOrWhiteSpace($ExePath)) {
    (Resolve-Path -LiteralPath (Join-Path $ProjectRoot "bin\Debug\net9.0-windows\ourplancore.exe")).Path
} else {
    (Resolve-Path -LiteralPath $ExePath).Path
}

function Write-Ascii {
    param(
        [Parameter(Mandatory)] [System.IO.Stream]$Stream,
        [Parameter(Mandatory)] [string]$Text
    )

    $bytes = [System.Text.Encoding]::ASCII.GetBytes($Text)
    $Stream.Write($bytes, 0, $bytes.Length)
}

function Write-LargeVectorPdf {
    param([Parameter(Mandatory)] [string]$Path)

    # 42 x 30 inches. At the required 150 DPI this becomes 6300 x 4500 pixels.
    $contentLines = New-Object System.Collections.Generic.List[string]
    $contentLines.Add("q")
    $contentLines.Add("1 1 1 rg 0 0 3024 2160 re f")
    $contentLines.Add("0 0 0 RG 1.2 w 120 120 m 2904 120 l 2904 2040 l 120 2040 l h S")
    for ($x = 240; $x -le 2880; $x += 120) {
        $contentLines.Add("0.18 0.18 0.18 RG 0.7 w $x 120 m $x 2040 l S")
    }
    for ($y = 240; $y -le 1920; $y += 120) {
        $contentLines.Add("0.18 0.18 0.18 RG 0.7 w 120 $y m 2904 $y l S")
    }
    $contentLines.Add("0.05 0.05 0.05 RG 2 w 360 360 m 2640 1800 l S")
    $contentLines.Add("360 1800 m 2640 360 l S")
    $contentLines.Add("0 0 0 rg BT /F1 34 Tf 160 2080 Td (OurPlanCore Area 150 DPI Performance Smoke) Tj ET")
    $contentLines.Add("Q")
    $contents = ($contentLines -join "`n") + "`n"
    $contentBytes = [System.Text.Encoding]::ASCII.GetBytes($contents)
    $objects = @(
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 3024 2160] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        "<< /Length $($contentBytes.Length) >>`nstream`n$contents" + "endstream"
    )

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        Write-Ascii $stream "%PDF-1.4`n"
        $offsets = New-Object long[] ($objects.Count + 1)
        for ($i = 0; $i -lt $objects.Count; $i++) {
            $offsets[$i + 1] = $stream.Position
            Write-Ascii $stream "$($i + 1) 0 obj`n$($objects[$i])`nendobj`n"
        }

        $xrefOffset = $stream.Position
        Write-Ascii $stream "xref`n0 $($objects.Count + 1)`n"
        Write-Ascii $stream "0000000000 65535 f `n"
        for ($i = 1; $i -lt $offsets.Length; $i++) {
            Write-Ascii $stream ("{0:D10} 00000 n `n" -f $offsets[$i])
        }
        Write-Ascii $stream "trailer`n<< /Size $($objects.Count + 1) /Root 1 0 R >>`nstartxref`n$xrefOffset`n%%EOF`n"
    }
    finally {
        $stream.Dispose()
    }
}

function Write-ItemDataXml {
    param(
        [Parameter(Mandatory)] [string]$Folder,
        [Parameter(Mandatory)] [string]$Class,
        [Parameter(Mandatory)] [string]$Name,
        [int]$OrderIndex
    )

    New-Item -ItemType Directory -Force -Path $Folder | Out-Null
    $guid = [guid]::NewGuid().ToString().ToUpperInvariant()
    $escapedName = [System.Security.SecurityElement]::Escape($Name)
    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<Item Class="$Class" Name="$escapedName" GUID="$guid">
  <Properties>
    <Property Name="OrderIndex" Value="$OrderIndex" />
    <Property Name="Name" Value="$escapedName" />
    <Property Name="Type" Value="$Class" />
    <Property Name="GUID" Value="$guid" />
  </Properties>
</Item>
"@
    Set-Content -LiteralPath (Join-Path $Folder "Data.xml") -Value $xml -Encoding UTF8
}

function New-AreaPreviewSmokeWorkspace {
    $root = Join-Path $env:TEMP ("onc_area_preview_smoke_" + [guid]::NewGuid().ToString("N"))
    $job = Join-Path $root "AreaPreviewSmokeJob"
    $pages = Join-Path $job "Pages"
    $takeoffs = Join-Path $job "Takeoffs"
    $sources = Join-Path $job "sources"
    $page = Join-Path $pages "A101 Area Performance"
    $pdf = Join-Path $sources "area_performance.pdf"
    $settings = Join-Path $root "settings.json"

    Write-ItemDataXml -Folder $job -Class "Job" -Name "AreaPreviewSmokeJob" -OrderIndex 0
    Write-ItemDataXml -Folder $pages -Class "Folder" -Name "Pages" -OrderIndex 1
    Write-ItemDataXml -Folder $takeoffs -Class "Folder" -Name "Takeoffs" -OrderIndex 2
    Write-ItemDataXml -Folder $page -Class "Page" -Name "A101 Area Performance" -OrderIndex 1
    Write-LargeVectorPdf -Path $pdf

    [ordered]@{
        pdf = "..\..\sources\area_performance.pdf"
        page = 0
        scale_m_per_pt = 0.008466666666666667
        pdf_layers_cached = $false
        pdf_layers = @()
        legend_takeoff_order = @()
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $page "source.json") -Encoding UTF8

    [ordered]@{
        JobsRootPath = $root
        JobsRootPaths = @($root)
        LastJobPath = $job
        LastPageFolder = $page
        UnitMode = "Imperial"
        Theme = "Dark"
        ViewportBackground = "#FFFFFF"
        StaticPageRenderEnabled = $true
        StaticPageRenderDpi = 150
        BlackVectorOverlayEnabled = $true
        PdfLayersEnabled = $false
        RecentJobs = @()
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $settings -Encoding UTF8

    [pscustomobject]@{ Root = $root; Job = $job; Page = $page; Settings = $settings }
}

$workspace = New-AreaPreviewSmokeWorkspace
$reportPathWasProvided = -not [string]::IsNullOrWhiteSpace($ReportPath)
$resolvedReport = if ($reportPathWasProvided) {
    if ([System.IO.Path]::IsPathRooted($ReportPath)) {
        [System.IO.Path]::GetFullPath($ReportPath)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $ReportPath))
    }
} else {
    Join-Path $workspace.Root "area-preview-report.json"
}
$stdoutPath = Join-Path $workspace.Root "stdout.txt"
$stderrPath = Join-Path $workspace.Root "stderr.txt"
$oldSettings = $env:OURPLANCORE_SETTINGS_PATH
$oldSmoke = $env:OURPLANCORE_VIEWPORT_AREA_PREVIEW_SMOKE
$oldReport = $env:OURPLANCORE_VIEWPORT_AREA_PREVIEW_REPORT
$process = $null

try {
    $env:OURPLANCORE_SETTINGS_PATH = $workspace.Settings
    $env:OURPLANCORE_VIEWPORT_AREA_PREVIEW_SMOKE = "1"
    $env:OURPLANCORE_VIEWPORT_AREA_PREVIEW_REPORT = $resolvedReport
    $process = Start-Process -FilePath $resolvedExe -WorkingDirectory (Split-Path -Parent $resolvedExe) `
        -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
    }
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "Area preview smoke timed out after $TimeoutSeconds seconds."
    }
    $process.WaitForExit()
    $process.Refresh()
    $exitCode = if ($null -eq $process.ExitCode) { 0 } else { [int]$process.ExitCode }
    if (-not (Test-Path -LiteralPath $resolvedReport)) {
        throw "Area preview smoke did not write its report (exit $exitCode)."
    }

    $report = Get-Content -LiteralPath $resolvedReport -Raw | ConvertFrom-Json
    foreach ($zoom in @($report.Probe.Zooms)) {
        Write-Host ("zoom {0}: hit {1:P1}, miss/bypass {2}, p95 frame/page/in-progress {3}/{4}/{5} ms, raster {6} DPI, black segments {7}" -f `
            [double]$zoom.Zoom, [double]$zoom.PageFrameHitRate, $zoom.PageFrameMissOrBypassCount, `
            $zoom.P95ElapsedMs, $zoom.P95PageBitmapMs, $zoom.P95InProgressMs, $zoom.RasterDpi, $zoom.BlackVectorSegmentCount)
    }
    if ($exitCode -ne 0 -or -not $report.Passed) {
        foreach ($failure in @($report.Failures)) { Write-Host "failure: $failure" -ForegroundColor Red }
        foreach ($zoom in @($report.Probe.Zooms)) {
            foreach ($failure in @($zoom.Failures)) { Write-Host "failure: zoom $($zoom.Zoom): $failure" -ForegroundColor Red }
        }
        throw "Area preview smoke failed with exit code $exitCode."
    }

    Write-Host "PASS Area preview smoke: retained 150 DPI page and black vector overlay stayed within frame budgets." -ForegroundColor Green
    if ($reportPathWasProvided) { Write-Host "Report saved: $resolvedReport" }
}
finally {
    $env:OURPLANCORE_SETTINGS_PATH = $oldSettings
    $env:OURPLANCORE_VIEWPORT_AREA_PREVIEW_SMOKE = $oldSmoke
    $env:OURPLANCORE_VIEWPORT_AREA_PREVIEW_REPORT = $oldReport
    if (-not $KeepArtifacts) {
        $tempRoot = [System.IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\'
        $resolvedRoot = [System.IO.Path]::GetFullPath($workspace.Root).TrimEnd('\') + '\'
        if ($resolvedRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $workspace.Root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

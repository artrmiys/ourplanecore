param(
    [string]$ProjectRoot = "$PSScriptRoot\..",
    [switch]$KeepAppOpen,
    [int]$TimeoutSeconds = 30,
    [int]$WheelEvents = 42,
    [int]$Cycles = 5,
    [string]$JobPath = "",
    [string]$PagePath = "",
    [switch]$CopyJob
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class NativeViewportSmokeMouse
{
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, int data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@

$MouseMove = 0x0001
$MouseLeftDown = 0x0002
$MouseLeftUp = 0x0004
$MouseMiddleDown = 0x0020
$MouseMiddleUp = 0x0040
$MouseWheel = 0x0800

function Write-Ascii {
    param(
        [Parameter(Mandatory)] [System.IO.Stream]$Stream,
        [Parameter(Mandatory)] [string]$Text
    )

    $bytes = [System.Text.Encoding]::ASCII.GetBytes($Text)
    $Stream.Write($bytes, 0, $bytes.Length)
}

function Write-SmokePdf {
    param([Parameter(Mandatory)] [string]$Path)

    $contents = @"
q
1 1 1 rg 0 0 792 612 re f
0.12 0.14 0.16 RG 2 w
80 80 m 712 80 l 712 532 l 80 532 l h S
0.68 0.68 0.68 RG 0.6 w
80 155 m 712 155 l S
80 230 m 712 230 l S
80 305 m 712 305 l S
80 380 m 712 380 l S
80 455 m 712 455 l S
185 80 m 185 532 l S
290 80 m 290 532 l S
395 80 m 395 532 l S
500 80 m 500 532 l S
605 80 m 605 532 l S
0.10 0.35 0.70 RG 4 w
185 80 m 230 80 l S
395 532 m 440 532 l S
712 305 m 712 350 l S
0 0 0 rg
BT /F1 22 Tf 72 560 Td (Viewport Zoom Smoke) Tj ET
BT /F1 11 Tf 92 505 Td (Real PDF page loaded into PdfViewport for wheel zoom smoke.) Tj ET
BT /F1 10 Tf 92 60 Td (Generated temporary file. Do not estimate from this sheet.) Tj ET
Q
"@

    $normalized = $contents.Replace("`r`n", "`n")
    $contentBytes = [System.Text.Encoding]::ASCII.GetBytes($normalized)
    $objects = @(
        "<< /Type /Catalog /Pages 2 0 R >>",
        "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 792 612] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        "<< /Length $($contentBytes.Length) >>`nstream`n$normalized`nendstream"
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
        [int]$OrderIndex = 1
    )

    New-Item -ItemType Directory -Force -Path $Folder | Out-Null
    $guid = [guid]::NewGuid().ToString().ToUpperInvariant()
    $escapedName = [System.Security.SecurityElement]::Escape($Name)
    $escapedClass = [System.Security.SecurityElement]::Escape($Class)
    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<Item Class="$escapedClass" Name="$escapedName" GUID="$guid">
  <Properties>
    <Property Name="OrderIndex" Value="$OrderIndex" />
    <Property Name="Name" Value="$escapedName" />
    <Property Name="Type" Value="$escapedClass" />
    <Property Name="GUID" Value="$guid" />
  </Properties>
</Item>
"@
    Set-Content -LiteralPath (Join-Path $Folder "Data.xml") -Value $xml -Encoding UTF8
}

function New-ZoomSmokeJob {
    $root = Join-Path $env:TEMP ("opc_viewport_zoom_smoke_" + [guid]::NewGuid().ToString("N"))
    $job = Join-Path $root "ViewportZoomSmokeJob"
    $pages = Join-Path $job "Pages"
    $takeoffs = Join-Path $job "Takeoffs"
    $sources = Join-Path $job "sources"
    $page = Join-Path $pages "A101 Zoom Smoke"
    $pdf = Join-Path $sources "zoom_smoke.pdf"

    Write-ItemDataXml -Folder $job -Class "Job" -Name "ViewportZoomSmokeJob" -OrderIndex 0
    Write-ItemDataXml -Folder $pages -Class "Folder" -Name "Pages" -OrderIndex 1
    Write-ItemDataXml -Folder $takeoffs -Class "Folder" -Name "Takeoffs" -OrderIndex 2
    Write-ItemDataXml -Folder $page -Class "Page" -Name "A101 Zoom Smoke" -OrderIndex 1
    Write-SmokePdf -Path $pdf

    $source = [ordered]@{
        pdf = "..\..\sources\zoom_smoke.pdf"
        page = 0
        scale_m_per_pt = 0.008466666666666667
        pdf_layers_cached = $false
        pdf_layers = @()
        legend_takeoff_order = @()
        overlay_page_folder = ""
        overlay_color = "#E53935"
        overlay_opacity = 0.55
        overlay_offset_x_pt = 0
        overlay_offset_y_pt = 0
        overlay_scale = 1.0
    }
    $source | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $page "source.json") -Encoding UTF8

    return [pscustomobject]@{
        Root = $root
        Job = $job
        Page = $page
    }
}

function New-ZoomSmokeJobFromExisting {
    param(
        [Parameter(Mandatory)] [string]$ExistingJobPath,
        [Parameter(Mandatory)] [string]$ExistingPagePath,
        [switch]$Copy
    )

    $resolvedJob = (Resolve-Path -LiteralPath $ExistingJobPath).Path
    $resolvedPage = (Resolve-Path -LiteralPath $ExistingPagePath).Path
    $jobPrefix = [System.IO.Path]::GetFullPath($resolvedJob).TrimEnd('\')
    $pageFullPath = [System.IO.Path]::GetFullPath($resolvedPage)
    if (-not $pageFullPath.StartsWith($jobPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "PagePath must be inside JobPath."
    }

    if (-not $Copy) {
        return [pscustomobject]@{
            Root = $null
            Job = $resolvedJob
            Page = $resolvedPage
            DisplayName = (Split-Path -Leaf $resolvedPage)
            Copied = $false
        }
    }

    $root = Join-Path $env:TEMP ("opc_viewport_zoom_realjob_" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Copy-Item -LiteralPath $resolvedJob -Destination $root -Recurse -Force

    $copiedJob = Join-Path $root (Split-Path -Leaf $resolvedJob)
    $lockPath = Join-Path $copiedJob ".~lock"
    if (Test-Path -LiteralPath $lockPath) {
        Remove-Item -LiteralPath $lockPath -Force -ErrorAction SilentlyContinue
    }

    $relativePage = $pageFullPath.Substring($jobPrefix.Length).TrimStart('\')
    $copiedPage = Join-Path $copiedJob $relativePage
    if (-not (Test-Path -LiteralPath $copiedPage)) {
        throw "Copied page path was not found: $copiedPage"
    }

    return [pscustomobject]@{
        Root = $root
        Job = $copiedJob
        Page = $copiedPage
        DisplayName = (Split-Path -Leaf $copiedPage)
        Copied = $true
    }
}

function Set-ZoomSmokeSettings {
    param(
        [Parameter(Mandatory)] [string]$JobPath,
        [Parameter(Mandatory)] [string]$PagePath
    )

    $settingsDir = Join-Path $env:APPDATA "OurPlaneCore"
    $settingsPath = Join-Path $settingsDir "settings.json"
    $backupPath = "$settingsPath.viewport-zoom-smoke.bak"
    New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null
    if (Test-Path -LiteralPath $settingsPath) {
        Copy-Item -LiteralPath $settingsPath -Destination $backupPath -Force
    }

    $settings = [ordered]@{
        JobsRootPath = (Split-Path -Parent $JobPath)
        JobsRootPaths = @((Split-Path -Parent $JobPath))
        LastJobPath = $JobPath
        LastPageFolder = $PagePath
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
    return [pscustomobject]@{ Path = $settingsPath; Backup = $backupPath }
}

function Restore-ZoomSmokeSettings {
    param($State)
    if ($null -eq $State) { return }
    if (Test-Path -LiteralPath $State.Backup) {
        Copy-Item -LiteralPath $State.Backup -Destination $State.Path -Force
        Remove-Item -LiteralPath $State.Backup -Force
    }
}

function Wait-Until {
    param(
        [Parameter(Mandatory)] [scriptblock]$Condition,
        [string]$Message = "condition",
        [int]$TimeoutSeconds = 30
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for $Message."
}

function Wait-WindowForProcess {
    param([Parameter(Mandatory)] [int]$ProcessId)

    $condition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::ProcessIdProperty,
        $ProcessId)
    $root = [Windows.Automation.AutomationElement]::RootElement
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $window = $root.FindFirst([Windows.Automation.TreeScope]::Children, $condition)
        if ($null -ne $window) { return $window }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for main window."
}

function Focus-Window {
    param([Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Window)

    $handle = [IntPtr]$Window.Current.NativeWindowHandle
    if ($handle -ne [IntPtr]::Zero) {
        [NativeViewportSmokeMouse]::SetForegroundWindow($handle) | Out-Null
    }
    try { $Window.SetFocus() } catch {}
    Start-Sleep -Milliseconds 350
}

function Find-DescendantByName {
    param(
        [Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Root,
        [Parameter(Mandatory)] [string]$Text
    )

    $items = $Root.FindAll(
        [Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.Condition]::TrueCondition)
    foreach ($item in $items) {
        if ($item.Current.Name -like "*$Text*") { return $item }
    }
    return $null
}

function Get-ViewportProbePoint {
    param([Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Window)
    $rect = $Window.Current.BoundingRectangle
    if ($rect.IsEmpty) {
        throw "Main window has empty bounds."
    }

    return [pscustomobject]@{
        X = [int]($rect.Left + $rect.Width * 0.58)
        Y = [int]($rect.Top + $rect.Height * 0.52)
    }
}

function Send-ZoomWheelBurst {
    param([Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Window)

    $point = Get-ViewportProbePoint -Window $Window
    $x = $point.X
    $y = $point.Y
    [NativeViewportSmokeMouse]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 150
    [NativeViewportSmokeMouse]::mouse_event($MouseLeftDown, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [NativeViewportSmokeMouse]::mouse_event($MouseLeftUp, 0, 0, 0, [UIntPtr]::Zero)

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt $WheelEvents; $i++) {
        [NativeViewportSmokeMouse]::mouse_event($MouseWheel, 0, 0, 120, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 18
    }
    $watch.Stop()
    return $watch.ElapsedMilliseconds
}

function Drag-ViewportBy {
    param(
        [Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Window,
        [int]$DeltaX,
        [int]$DeltaY
    )

    $point = Get-ViewportProbePoint -Window $Window
    $startX = $point.X
    $startY = $point.Y
    [NativeViewportSmokeMouse]::SetCursorPos($startX, $startY) | Out-Null
    Start-Sleep -Milliseconds 50
    [NativeViewportSmokeMouse]::mouse_event($MouseMiddleDown, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    for ($i = 1; $i -le 14; $i++) {
        $x = [int]($startX + ($DeltaX * $i / 14))
        $y = [int]($startY + ($DeltaY * $i / 14))
        [NativeViewportSmokeMouse]::SetCursorPos($x, $y) | Out-Null
        [NativeViewportSmokeMouse]::mouse_event($MouseMove, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 14
    }
    [NativeViewportSmokeMouse]::mouse_event($MouseMiddleUp, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 80
}

function Send-ViewportZoomPanBurst {
    param([Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Window)

    $point = Get-ViewportProbePoint -Window $Window
    [NativeViewportSmokeMouse]::SetCursorPos($point.X, $point.Y) | Out-Null
    Start-Sleep -Milliseconds 150
    [NativeViewportSmokeMouse]::mouse_event($MouseLeftDown, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [NativeViewportSmokeMouse]::mouse_event($MouseLeftUp, 0, 0, 0, [UIntPtr]::Zero)

    $wheelPerCycle = [Math]::Max(4, [Math]::Max(1, [int]($WheelEvents / [Math]::Max(1, $Cycles))))
    $dragPattern = @(
        @{ X = -190; Y = 0 },
        @{ X = 160; Y = 95 },
        @{ X = 0; Y = -140 },
        @{ X = 210; Y = -70 },
        @{ X = -130; Y = 120 }
    )

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    for ($cycle = 0; $cycle -lt $Cycles; $cycle++) {
        for ($i = 0; $i -lt $wheelPerCycle; $i++) {
            [NativeViewportSmokeMouse]::mouse_event($MouseWheel, 0, 0, 120, [UIntPtr]::Zero)
            Start-Sleep -Milliseconds 14
        }

        $drag = $dragPattern[$cycle % $dragPattern.Count]
        Drag-ViewportBy -Window $Window -DeltaX $drag.X -DeltaY $drag.Y

        for ($i = 0; $i -lt [Math]::Max(2, [int]($wheelPerCycle / 2)); $i++) {
            [NativeViewportSmokeMouse]::mouse_event($MouseWheel, 0, 0, -120, [UIntPtr]::Zero)
            Start-Sleep -Milliseconds 14
        }

        $drag = $dragPattern[($cycle + 2) % $dragPattern.Count]
        Drag-ViewportBy -Window $Window -DeltaX $drag.X -DeltaY $drag.Y
    }
    $watch.Stop()
    return $watch.ElapsedMilliseconds
}

$job = $null
$settingsState = $null
$proc = $null
try {
    if ([string]::IsNullOrWhiteSpace($JobPath)) {
        $job = New-ZoomSmokeJob
        $job | Add-Member -NotePropertyName DisplayName -NotePropertyValue "A101 Zoom Smoke" -Force
        $job | Add-Member -NotePropertyName Copied -NotePropertyValue $false -Force
    } else {
        if ([string]::IsNullOrWhiteSpace($PagePath)) {
            throw "PagePath is required when JobPath is provided."
        }
        $job = New-ZoomSmokeJobFromExisting -ExistingJobPath $JobPath -ExistingPagePath $PagePath -Copy:$CopyJob
    }

    $settingsState = Set-ZoomSmokeSettings -JobPath $job.Job -PagePath $job.Page

    $appDll = Join-Path $ProjectRoot "cache\verify_build\ourplanecore.dll"
    if (Test-Path -LiteralPath $appDll) {
        $proc = Start-Process -FilePath "dotnet" -ArgumentList @($appDll) -WorkingDirectory $ProjectRoot -PassThru
    } else {
        $projectPath = Join-Path $ProjectRoot "ourplanecore.csproj"
        $proc = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--no-restore", "--project", $projectPath) -WorkingDirectory $ProjectRoot -PassThru
    }

    $window = Wait-WindowForProcess -ProcessId $proc.Id
    Focus-Window $window

    Wait-Until -TimeoutSeconds $TimeoutSeconds -Message "$($job.DisplayName) page loaded" -Condition {
        $null -ne (Find-DescendantByName -Root $window -Text $job.DisplayName)
    } | Out-Null

    $elapsedMs = Send-ViewportZoomPanBurst -Window $window

    Wait-Until -TimeoutSeconds $TimeoutSeconds -Message "main window responsive after viewport zoom burst" -Condition {
        if ($proc.HasExited) { return $false }
        try {
            $window.Current.IsEnabled -and $null -ne (Find-DescendantByName -Root $window -Text "Select")
        } catch {
            $false
        }
    } | Out-Null

    if ($job.Copied) {
        Write-Host "PASS viewport zoom smoke: copied real job page '$($job.DisplayName)' accepted zoom in/out plus middle-button pan cycles and UI stayed responsive ($elapsedMs ms dispatch)." -ForegroundColor Green
    } else {
        Write-Host "PASS viewport zoom smoke: PDF page '$($job.DisplayName)' accepted zoom in/out plus middle-button pan cycles and UI stayed responsive ($elapsedMs ms dispatch)." -ForegroundColor Green
    }
}
finally {
    if ($proc -ne $null -and -not $KeepAppOpen) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch {}
    }
    Restore-ZoomSmokeSettings $settingsState
    if ($job -ne $null -and -not $KeepAppOpen -and -not [string]::IsNullOrWhiteSpace($job.Root)) {
        try { Remove-Item -LiteralPath $job.Root -Recurse -Force -ErrorAction SilentlyContinue } catch {}
    }
}

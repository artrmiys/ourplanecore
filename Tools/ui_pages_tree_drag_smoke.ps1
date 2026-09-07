param(
    [string]$ProjectRoot = "$PSScriptRoot\..",
    [switch]$KeepAppOpen,
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class NativeMouse
{
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@

$MouseMove = 0x0001
$MouseLeftDown = 0x0002
$MouseLeftUp = 0x0004

function Write-ItemDataXml {
    param(
        [Parameter(Mandatory)] [string]$Folder,
        [Parameter(Mandatory)] [string]$Name,
        [int]$OrderIndex = 1
    )

    New-Item -ItemType Directory -Force -Path $Folder | Out-Null
    $guid = [guid]::NewGuid().ToString().ToUpperInvariant()
    $escapedName = [System.Security.SecurityElement]::Escape($Name)
    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<Item Class="Folder" Name="$escapedName" GUID="$guid">
  <Properties>
    <Property Name="OrderIndex" Value="$OrderIndex" />
    <Property Name="Name" Value="$escapedName" />
    <Property Name="Type" Value="Folder" />
    <Property Name="GUID" Value="$guid" />
  </Properties>
</Item>
"@
    Set-Content -LiteralPath (Join-Path $Folder "Data.xml") -Value $xml -Encoding UTF8
}

function New-SmokeJob {
    $root = Join-Path $env:TEMP ("onc_ui_smoke_" + [guid]::NewGuid().ToString("N"))
    $job = Join-Path $root "UiSmokeJob"
    $pages = Join-Path $job "Pages"
    $takeoffs = Join-Path $job "Takeoffs"

    Write-ItemDataXml -Folder $job -Name "UiSmokeJob" -OrderIndex 0
    Write-ItemDataXml -Folder $pages -Name "Pages" -OrderIndex 1
    Write-ItemDataXml -Folder $takeoffs -Name "Takeoffs" -OrderIndex 2
    Write-ItemDataXml -Folder (Join-Path $pages "RCP") -Name "RCP" -OrderIndex 1
    Write-ItemDataXml -Folder (Join-Path $pages "units") -Name "units" -OrderIndex 2
    Write-ItemDataXml -Folder (Join-Path $pages "Other") -Name "Other" -OrderIndex 3

    return [pscustomobject]@{
        Root = $root
        Job = $job
        Pages = $pages
        Rcp = Join-Path $pages "RCP"
        UnitsRoot = Join-Path $pages "units"
        UnitsInsideRcp = Join-Path (Join-Path $pages "RCP") "units"
    }
}

function Set-SmokeSettings {
    param([Parameter(Mandatory)] [string]$JobPath)

    $settingsDir = Join-Path $env:APPDATA "OurPlanCore"
    $settingsPath = Join-Path $settingsDir "settings.json"
    $backupPath = "$settingsPath.ui-smoke.bak"
    New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null
    if (Test-Path -LiteralPath $settingsPath) {
        Copy-Item -LiteralPath $settingsPath -Destination $backupPath -Force
    }

    $settings = [ordered]@{
        JobsRootPath = (Split-Path -Parent $JobPath)
        JobsRootPaths = @((Split-Path -Parent $JobPath))
        LastJobPath = $JobPath
        LastPageFolder = ""
        UnitMode = "Imperial"
        Theme = "Dark"
        ViewportBackground = "#FFFFFF"
        RecentJobs = @()
    }
    $settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
    return [pscustomobject]@{ Path = $settingsPath; Backup = $backupPath }
}

function Restore-SmokeSettings {
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
    param(
        [Parameter(Mandatory)] [int]$ProcessId,
        [string]$Title = "UiSmokeJob"
    )

    $uiaCondition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::ProcessIdProperty,
        $ProcessId)
    $root = [Windows.Automation.AutomationElement]::RootElement
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $window = $root.FindFirst([Windows.Automation.TreeScope]::Children, $uiaCondition)
        if ($null -ne $window) { return $window }

        $windows = $root.FindAll(
            [Windows.Automation.TreeScope]::Children,
            [Windows.Automation.Condition]::TrueCondition)
        foreach ($candidate in $windows) {
            if ($candidate.Current.Name -like "*$Title*") { return $candidate }
        }

        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for main window."
}

function Focus-Window {
    param([Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Window)

    $handle = [IntPtr]$Window.Current.NativeWindowHandle
    if ($handle -ne [IntPtr]::Zero) {
        [NativeMouse]::SetForegroundWindow($handle) | Out-Null
    }
    try { $Window.SetFocus() } catch {}
    Start-Sleep -Milliseconds 350
}

function Find-TreeItem {
    param(
        [Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Root,
        [Parameter(Mandatory)] [string]$Text
    )

    $condition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::ControlTypeProperty,
        [Windows.Automation.ControlType]::TreeItem)
    $items = $Root.FindAll([Windows.Automation.TreeScope]::Descendants, $condition)
    foreach ($item in $items) {
        if ($item.Current.Name -like "*$Text*") { return $item }
    }
    return $null
}

function Find-TreeItems {
    param(
        [Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Root,
        [Parameter(Mandatory)] [string]$Text
    )

    $result = New-Object System.Collections.Generic.List[Windows.Automation.AutomationElement]
    $condition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::ControlTypeProperty,
        [Windows.Automation.ControlType]::TreeItem)
    $items = $Root.FindAll([Windows.Automation.TreeScope]::Descendants, $condition)
    foreach ($item in $items) {
        if ($item.Current.Name -like "*$Text*") { $result.Add($item) }
    }
    return $result
}

function Expand-TreeItem {
    param([Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Item)
    try {
        $pattern = $Item.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
        if ($pattern.Current.ExpandCollapseState -eq [Windows.Automation.ExpandCollapseState]::Collapsed) {
            $pattern.Expand()
            Start-Sleep -Milliseconds 400
        }
    } catch {
        # Leaf nodes do not expose ExpandCollapsePattern.
    }
}

function Scroll-IntoView {
    param([Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Item)
    try {
        $pattern = $Item.GetCurrentPattern([Windows.Automation.ScrollItemPattern]::Pattern)
        $pattern.ScrollIntoView()
        Start-Sleep -Milliseconds 150
    } catch {
    }
}

function Drag-ElementToElement {
    param(
        [Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Source,
        [Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Target
    )

    Scroll-IntoView $Source
    Scroll-IntoView $Target
    $sourceRect = $Source.Current.BoundingRectangle
    $targetRect = $Target.Current.BoundingRectangle
    if ($sourceRect.IsEmpty -or $targetRect.IsEmpty) {
        throw "Cannot drag because source or target has an empty bounding rectangle."
    }

    $sourceOffset = [Math]::Min([Math]::Max($sourceRect.Width * 0.18, 26.0), [Math]::Max(26.0, $sourceRect.Width - 8.0))
    $targetOffset = [Math]::Min([Math]::Max($targetRect.Width * 0.18, 26.0), [Math]::Max(26.0, $targetRect.Width - 8.0))
    $sx = [int]($sourceRect.Left + $sourceOffset)
    $sy = [int]($sourceRect.Top + ([Math]::Min($sourceRect.Height / 2, 12)))
    $tx = [int]($targetRect.Left + $targetOffset)
    $ty = [int]($targetRect.Top + ([Math]::Min($targetRect.Height / 2, 12)))

    [NativeMouse]::SetCursorPos($sx, $sy) | Out-Null
    Start-Sleep -Milliseconds 120
    [NativeMouse]::mouse_event($MouseLeftDown, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 220
    for ($i = 1; $i -le 18; $i++) {
        $x = [int]($sx + (($tx - $sx) * $i / 18))
        $y = [int]($sy + (($ty - $sy) * $i / 18))
        [NativeMouse]::SetCursorPos($x, $y) | Out-Null
        [NativeMouse]::mouse_event($MouseMove, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 25
    }
    Start-Sleep -Milliseconds 180
    [NativeMouse]::mouse_event($MouseLeftUp, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 900
}

function Assert-UnitsNotUnderRcpInUi {
    param([Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Window)

    $walker = [Windows.Automation.TreeWalker]::ControlViewWalker
    $unitsItems = Find-TreeItems -Root $Window -Text "units"
    if ($unitsItems.Count -eq 0) {
        throw "No 'units' tree item found after drag-out."
    }

    foreach ($item in $unitsItems) {
        $parent = $walker.GetParent($item)
        if ($null -ne $parent -and $parent.Current.Name -like "*RCP*") {
            throw "'units' is still visually nested under RCP."
        }
    }
}

$job = $null
$settingsState = $null
$proc = $null
try {
    $job = New-SmokeJob
    $settingsState = Set-SmokeSettings -JobPath $job.Job

    $appDll = Join-Path $ProjectRoot "cache\verify_build\ourplancore.dll"
    if (Test-Path -LiteralPath $appDll) {
        $proc = Start-Process -FilePath "dotnet" -ArgumentList @($appDll) -WorkingDirectory $ProjectRoot -PassThru
    } else {
        $projectPath = Join-Path $ProjectRoot "ourplancore.csproj"
        $proc = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--no-restore", "--project", $projectPath) -WorkingDirectory $ProjectRoot -PassThru
    }
    $window = Wait-WindowForProcess -ProcessId $proc.Id -Title "UiSmokeJob"
    Focus-Window $window

    Wait-Until -TimeoutSeconds $TimeoutSeconds -Message "RCP folder in Pages tree" -Condition {
        $script:rcp = Find-TreeItem -Root $window -Text "RCP"
        $null -ne $script:rcp
    } | Out-Null

    Wait-Until -TimeoutSeconds $TimeoutSeconds -Message "units folder in root Pages tree" -Condition {
        $script:units = Find-TreeItem -Root $window -Text "units"
        $null -ne $script:units
    } | Out-Null

    Drag-ElementToElement -Source $script:units -Target $script:rcp
    Wait-Until -TimeoutSeconds $TimeoutSeconds -Message "units moved inside RCP" -Condition {
        (Test-Path -LiteralPath $job.UnitsInsideRcp) -and -not (Test-Path -LiteralPath $job.UnitsRoot)
    } | Out-Null

    $script:rcp = Find-TreeItem -Root $window -Text "RCP"
    if ($null -eq $script:rcp) { throw "RCP tree item disappeared after move-in." }
    Expand-TreeItem $script:rcp

    Wait-Until -TimeoutSeconds $TimeoutSeconds -Message "units folder visible inside RCP" -Condition {
        $script:units = Find-TreeItem -Root $window -Text "units"
        $null -ne $script:units
    } | Out-Null

    Drag-ElementToElement -Source $script:units -Target $script:rcp
    Wait-Until -TimeoutSeconds $TimeoutSeconds -Message "units moved back to root" -Condition {
        (Test-Path -LiteralPath $job.UnitsRoot) -and -not (Test-Path -LiteralPath $job.UnitsInsideRcp)
    } | Out-Null

    Start-Sleep -Milliseconds 1200
    Assert-UnitsNotUnderRcpInUi -Window $window

    Write-Host "PASS pages tree drag smoke: units moved into RCP and back out, filesystem and UI updated." -ForegroundColor Green
}
finally {
    if ($proc -ne $null -and -not $KeepAppOpen) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch {}
    }
    Restore-SmokeSettings $settingsState
    if ($job -ne $null -and -not $KeepAppOpen) {
        try { Remove-Item -LiteralPath $job.Root -Recurse -Force -ErrorAction SilentlyContinue } catch {}
    }
}

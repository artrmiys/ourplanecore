param(
    [string]$ProjectRoot = "$PSScriptRoot\..",
    [switch]$KeepAppOpen,
    [int]$TimeoutSeconds = 30,
    [int]$PageCount = 160,
    [int]$MeasuredTakeoffCount = 300,
    [int]$BulkCount = 120,
    [int]$MeasurementsPerTakeoff = 3
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

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
    public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@

$MouseMove = 0x0001
$MouseLeftDown = 0x0002
$MouseLeftUp = 0x0004
$KeyEventKeyUp = 0x0002
$VirtualKeyControl = 0x11
$VirtualKeyX = 0x58
$VirtualKeyV = 0x56
$SettingsPathEnvName = "OURPLANCORE_SETTINGS_PATH"

function Write-ItemDataXml {
    param(
        [Parameter(Mandatory)] [string]$Folder,
        [Parameter(Mandatory)] [string]$Class,
        [Parameter(Mandatory)] [string]$Name,
        [int]$OrderIndex = 1,
        [hashtable]$Properties = @{}
    )

    New-Item -ItemType Directory -Force -Path $Folder | Out-Null
    $guid = [guid]::NewGuid().ToString().ToUpperInvariant()
    $escapedName = [System.Security.SecurityElement]::Escape($Name)
    $escapedClass = [System.Security.SecurityElement]::Escape($Class)
    $propertyLines = New-Object System.Collections.Generic.List[string]
    $propertyLines.Add("    <Property Name=`"OrderIndex`" Value=`"$OrderIndex`" />")
    $propertyLines.Add("    <Property Name=`"Name`" Value=`"$escapedName`" />")
    $propertyLines.Add("    <Property Name=`"Type`" Value=`"$escapedClass`" />")
    $propertyLines.Add("    <Property Name=`"GUID`" Value=`"$guid`" />")
    foreach ($key in $Properties.Keys) {
        $escapedKey = [System.Security.SecurityElement]::Escape([string]$key)
        $escapedValue = [System.Security.SecurityElement]::Escape([string]$Properties[$key])
        $propertyLines.Add("    <Property Name=`"$escapedKey`" Value=`"$escapedValue`" />")
    }

    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<Item Class="$escapedClass" Name="$escapedName" GUID="$guid">
  <Properties>
$($propertyLines -join "`r`n")
  </Properties>
</Item>
"@
    Set-Content -LiteralPath (Join-Path $Folder "Data.xml") -Value $xml -Encoding UTF8
}

function New-TakeoffItemFolder {
    param(
        [Parameter(Mandatory)] [string]$Parent,
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$Color,
        [int]$OrderIndex
    )

    $path = Join-Path $Parent $Name
    Write-ItemDataXml -Folder $path -Class "Folder" -Name $Name -OrderIndex $OrderIndex -Properties @{
        SmartNodeKind = "item"
        Color = $Color
        MeasurementType = "line"
    }
    return $path
}

function Write-SmokePageSource {
    param(
        [Parameter(Mandatory)] [string]$Folder,
        [Parameter(Mandatory)] [string]$SourcePdf,
        [int]$PdfPage
    )

    $source = [ordered]@{
        pdf = $SourcePdf
        page = $PdfPage
        scale_m_per_pt = 0.01
        pdf_layers_cached = $false
        pdf_layers = @()
        legend_takeoff_order = @()
        legend_takeoff_order_mode = "auto"
        hidden_takeoffs = @()
    }
    $source | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Folder "source.json") -Encoding UTF8
}

function New-SmokePage {
    param(
        [Parameter(Mandatory)] [string]$Parent,
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$SourcePdf,
        [int]$OrderIndex
    )

    $path = Join-Path $Parent $Name
    Write-ItemDataXml -Folder $path -Class "Page" -Name $Name -OrderIndex $OrderIndex
    Write-SmokePageSource -Folder $path -SourcePdf $SourcePdf -PdfPage ($OrderIndex - 1)
    return $path
}

function Write-TakeoffMeasurements {
    param(
        [Parameter(Mandatory)] [string]$Folder,
        [Parameter(Mandatory)] [string]$Color,
        [Parameter(Mandatory)] [string[]]$PageFolders,
        [int]$Seed,
        [int]$Count
    )

    if ($PageFolders.Count -eq 0 -or $Count -le 0) { return }

    $measurements = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $Count; $i++) {
        $pageFolder = $PageFolders[($Seed + $i) % $PageFolders.Count]
        $x = 40 + (($Seed * 17 + $i * 31) % 1800)
        $y = 55 + (($Seed * 23 + $i * 19) % 1200)
        $measurements.Add([ordered]@{
            id = [guid]::NewGuid().ToString()
            mtype = "line"
            name = ""
            notes = ""
            points_pdf = @(
                [ordered]@{ X = [double]$x; Y = [double]$y },
                [ordered]@{ X = [double]($x + 80); Y = [double]($y + 20) }
            )
            holes_pdf = @()
            color = $Color
            count_symbol = "circle"
            page_folder = $pageFolder
            scale_m_per_pt = 0.01
            joist_direction_degrees = 0
            joist_direction_locked = $false
            joist_direction_follows_area_rotation = $true
            joist_add_end_joist = $true
        })
    }

    $measurements | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Folder "measurements.json") -Encoding UTF8
}

function New-TakeoffFolder {
    param(
        [Parameter(Mandatory)] [string]$Parent,
        [Parameter(Mandatory)] [string]$Name,
        [int]$OrderIndex
    )

    $path = Join-Path $Parent $Name
    Write-ItemDataXml -Folder $path -Class "Folder" -Name $Name -OrderIndex $OrderIndex -Properties @{
        SmartNodeKind = "folder"
    }
    return $path
}

function New-SmokeJob {
    $root = Join-Path $env:TEMP ("onc_takeoffs_ui_smoke_" + [guid]::NewGuid().ToString("N"))
    $job = Join-Path $root "TakeoffsSmokeJob"
    $pages = Join-Path $job "Pages"
    $takeoffs = Join-Path $job "Takeoffs"

    Write-ItemDataXml -Folder $job -Class "Folder" -Name "TakeoffsSmokeJob" -OrderIndex 0
    Write-ItemDataXml -Folder (Join-Path $job "sources") -Class "Folder" -Name "sources" -OrderIndex 1
    Write-ItemDataXml -Folder $pages -Class "Folder" -Name "Pages" -OrderIndex 2
    Write-ItemDataXml -Folder (Join-Path $pages "--------others") -Class "Folder" -Name "--------others" -OrderIndex 3
    Write-ItemDataXml -Folder $takeoffs -Class "Folder" -Name "Takeoffs" -OrderIndex 4
    $sourcePdf = Join-Path $job "sources\smoke.pdf"
    Set-Content -LiteralPath $sourcePdf -Encoding Ascii -Value "%PDF-1.4`n% OurPlanCore smoke placeholder`n"

    $pageFolders = New-Object System.Collections.Generic.List[string]
    for ($i = 1; $i -le $PageCount; $i++) {
        $pageName = "A{0:D3}" -f $i
        $pageFolders.Add((New-SmokePage -Parent $pages -Name $pageName -SourcePdf $sourcePdf -OrderIndex $i))
    }

    $wallA = New-TakeoffItemFolder -Parent $takeoffs -Name "Smoke Wall A" -Color "#D32F2F" -OrderIndex 1
    $wallB = New-TakeoffItemFolder -Parent $takeoffs -Name "Smoke Wall B" -Color "#1976D2" -OrderIndex 2
    $target = New-TakeoffFolder -Parent $takeoffs -Name "Smoke Target Folder" -OrderIndex 3
    $wallC = New-TakeoffItemFolder -Parent $takeoffs -Name "Smoke Wall C" -Color "#388E3C" -OrderIndex 4

    for ($i = 1; $i -le $MeasuredTakeoffCount; $i++) {
        $name = "Smoke Measured {0:D3}" -f $i
        $folder = New-TakeoffItemFolder -Parent $takeoffs -Name $name -Color "#00897B" -OrderIndex (1000 + $i)
        Write-TakeoffMeasurements -Folder $folder -Color "#00897B" -PageFolders $pageFolders.ToArray() -Seed $i -Count $MeasurementsPerTakeoff
    }

    for ($i = 1; $i -le $BulkCount; $i++) {
        $name = "Smoke Bulk {0:D3}" -f $i
        $folder = New-TakeoffItemFolder -Parent $takeoffs -Name $name -Color "#7B1FA2" -OrderIndex (2000 + $i)
        Write-TakeoffMeasurements -Folder $folder -Color "#7B1FA2" -PageFolders $pageFolders.ToArray() -Seed (5000 + $i) -Count $MeasurementsPerTakeoff
    }

    return [pscustomobject]@{
        Root = $root
        Job = $job
        SettingsPath = Join-Path $root "settings.json"
        ReportPath = Join-Path $root "takeoffs_move_smoke.report.json"
        StdOutLog = Join-Path $root "app.stdout.log"
        StdErrLog = Join-Path $root "app.stderr.log"
        Takeoffs = $takeoffs
        WallA = $wallA
        WallB = $wallB
        WallC = $wallC
        Target = $target
        WallBInsideTarget = Join-Path $target "Smoke Wall B"
        PageCount = $PageCount
        MeasuredTakeoffCount = $MeasuredTakeoffCount
        BulkCount = $BulkCount
        MeasurementsPerTakeoff = $MeasurementsPerTakeoff
    }
}

function Set-SmokeSettings {
    param([Parameter(Mandatory)] $Job)

    $previous = [pscustomobject]@{
        SettingsPath = [Environment]::GetEnvironmentVariable($SettingsPathEnvName, "Process")
        Smoke = [Environment]::GetEnvironmentVariable("OURPLANCORE_TAKEOFFS_MOVE_SMOKE", "Process")
        Report = [Environment]::GetEnvironmentVariable("OURPLANCORE_TAKEOFFS_MOVE_SMOKE_REPORT", "Process")
    }
    [Environment]::SetEnvironmentVariable($SettingsPathEnvName, $Job.SettingsPath, "Process")
    Set-Item -Path "Env:$SettingsPathEnvName" -Value $Job.SettingsPath
    [Environment]::SetEnvironmentVariable("OURPLANCORE_TAKEOFFS_MOVE_SMOKE", "1", "Process")
    Set-Item -Path "Env:OURPLANCORE_TAKEOFFS_MOVE_SMOKE" -Value "1"
    [Environment]::SetEnvironmentVariable("OURPLANCORE_TAKEOFFS_MOVE_SMOKE_REPORT", $Job.ReportPath, "Process")
    Set-Item -Path "Env:OURPLANCORE_TAKEOFFS_MOVE_SMOKE_REPORT" -Value $Job.ReportPath

    $settings = [ordered]@{
        JobsRootPath = (Split-Path -Parent $Job.Job)
        JobsRootPaths = @((Split-Path -Parent $Job.Job))
        LastJobPath = $Job.Job
        LastPageFolder = ""
        UnitMode = "Imperial"
        Theme = "Dark"
        ViewportBackground = "#FFFFFF"
        RecentJobs = @()
    }
    $settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Job.SettingsPath -Encoding UTF8
    return $previous
}

function Restore-SmokeSettings {
    param($PreviousState)

    Restore-ProcessEnvironment -Name $SettingsPathEnvName -Value $PreviousState.SettingsPath
    Restore-ProcessEnvironment -Name "OURPLANCORE_TAKEOFFS_MOVE_SMOKE" -Value $PreviousState.Smoke
    Restore-ProcessEnvironment -Name "OURPLANCORE_TAKEOFFS_MOVE_SMOKE_REPORT" -Value $PreviousState.Report
}

function Restore-ProcessEnvironment {
    param(
        [Parameter(Mandatory)] [string]$Name,
        $Value
    )

    [Environment]::SetEnvironmentVariable($Name, $Value, "Process")
    if ([string]::IsNullOrWhiteSpace($Value)) {
        Remove-Item -Path "Env:$Name" -ErrorAction SilentlyContinue
    } else {
        Set-Item -Path "Env:$Name" -Value $Value
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
        [string]$Title = "TakeoffsSmokeJob"
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
        if (Test-TreeItemText -Item $item -Text $Text) { return $item }
    }
    return $null
}

function Find-ElementByAutomationId {
    param(
        [Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Root,
        [Parameter(Mandatory)] [string]$AutomationId
    )

    $condition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    return $Root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
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
        if (Test-TreeItemText -Item $item -Text $Text) { $result.Add($item) }
    }
    return $result
}

function Get-ElementText {
    param([Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Element)

    $name = $Element.Current.Name
    if (-not [string]::IsNullOrWhiteSpace($name) -and
        -not $name.StartsWith("System.Windows.Controls.TreeViewItem", [StringComparison]::Ordinal)) {
        return $name
    }

    $textCondition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::ControlTypeProperty,
        [Windows.Automation.ControlType]::Text)
    $texts = $Element.FindAll([Windows.Automation.TreeScope]::Descendants, $textCondition)
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($text in $texts) {
        if (-not [string]::IsNullOrWhiteSpace($text.Current.Name)) {
            $parts.Add($text.Current.Name)
        }
    }
    return ($parts -join " ")
}

function Test-TreeItemText {
    param(
        [Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Item,
        [Parameter(Mandatory)] [string]$Text
    )

    $displayText = Get-ElementText -Element $Item
    return $displayText -like "*$Text*"
}

function Get-ElementHitRectangle {
    param([Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Element)

    $textCondition = New-Object Windows.Automation.PropertyCondition(
        [Windows.Automation.AutomationElement]::ControlTypeProperty,
        [Windows.Automation.ControlType]::Text)
    $texts = $Element.FindAll([Windows.Automation.TreeScope]::Descendants, $textCondition)
    $bestRect = $Element.Current.BoundingRectangle
    $bestArea = if ($bestRect.IsEmpty) { 0.0 } else { $bestRect.Width * $bestRect.Height }
    foreach ($text in $texts) {
        if ([string]::IsNullOrWhiteSpace($text.Current.Name)) {
            continue
        }

        $rect = $text.Current.BoundingRectangle
        if ($rect.IsEmpty -or $rect.Width -lt 8 -or $rect.Height -lt 8) {
            continue
        }

        $area = $rect.Width * $rect.Height
        if ($area -gt $bestArea) {
            $bestRect = $rect
            $bestArea = $area
        }
    }

    return $bestRect
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
    $sourceRect = Get-ElementHitRectangle -Element $Source
    $targetRect = Get-ElementHitRectangle -Element $Target
    if ($sourceRect.IsEmpty -or $targetRect.IsEmpty) {
        throw "Cannot drag because source or target has an empty bounding rectangle."
    }

    $sx = [int]($sourceRect.Left + ($sourceRect.Width / 2))
    $sy = [int]($sourceRect.Top + ($sourceRect.Height / 2))
    $tx = [int]($targetRect.Left + ($targetRect.Width / 2))
    $ty = [int]($targetRect.Top + ($targetRect.Height / 2))
    Write-Host "Drag from '$((Get-ElementText -Element $Source))' [$sx,$sy] to '$((Get-ElementText -Element $Target))' [$tx,$ty]." -ForegroundColor Cyan

    [NativeMouse]::SetCursorPos($sx, $sy) | Out-Null
    Start-Sleep -Milliseconds 120
    [NativeMouse]::mouse_event($MouseLeftDown, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [NativeMouse]::mouse_event($MouseLeftUp, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 350

    [NativeMouse]::SetCursorPos($sx, $sy) | Out-Null
    Start-Sleep -Milliseconds 120
    [NativeMouse]::mouse_event($MouseLeftDown, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 220

    $armX = [int]($sx + 42)
    $armY = [int]($sy + 8)
    [NativeMouse]::SetCursorPos($armX, $armY) | Out-Null
    [NativeMouse]::mouse_event($MouseMove, 1, 0, 0, [UIntPtr]::Zero)
    [NativeMouse]::SetCursorPos($armX, $armY) | Out-Null
    Start-Sleep -Milliseconds 180

    for ($i = 1; $i -le 24; $i++) {
        $x = [int]($armX + (($tx - $armX) * $i / 24))
        $y = [int]($armY + (($ty - $armY) * $i / 24))
        [NativeMouse]::SetCursorPos($x, $y) | Out-Null
        [NativeMouse]::mouse_event($MouseMove, 1, 0, 0, [UIntPtr]::Zero)
        [NativeMouse]::SetCursorPos($x, $y) | Out-Null
        Start-Sleep -Milliseconds 35
    }
    Start-Sleep -Milliseconds 180
    [NativeMouse]::mouse_event($MouseLeftUp, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 900
}

function Select-TreeElement {
    param([Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Item)

    Scroll-IntoView $Item
    try {
        $pattern = $Item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)
        $pattern.Select()
    } catch {
    }
    if ($script:takeoffsTreeElement -ne $null) {
        try { $script:takeoffsTreeElement.SetFocus() } catch {}
    } else {
        try { $Item.SetFocus() } catch {}
    }
    Start-Sleep -Milliseconds 350
}

function Send-KeyChord {
    param([Parameter(Mandatory)] [string]$Chord)

    $key = switch ($Chord) {
        "^x" { $VirtualKeyX }
        "^v" { $VirtualKeyV }
        default { throw "Unsupported key chord '$Chord'." }
    }

    [NativeMouse]::keybd_event([byte]$VirtualKeyControl, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 40
    [NativeMouse]::keybd_event([byte]$key, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 40
    [NativeMouse]::keybd_event([byte]$key, 0, $KeyEventKeyUp, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 40
    [NativeMouse]::keybd_event([byte]$VirtualKeyControl, 0, $KeyEventKeyUp, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 650
}

function Cut-PasteTreeElement {
    param(
        [Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Source,
        [Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Target
    )

    Select-TreeElement $Source
    Send-KeyChord "^x"
    Select-TreeElement $Target
    Send-KeyChord "^v"
}

function Assert-WallBNotUnderTargetInUi {
    param([Parameter(Mandatory)] [Windows.Automation.AutomationElement]$Window)

    $walker = [Windows.Automation.TreeWalker]::ControlViewWalker
    $wallItems = Find-TreeItems -Root $Window -Text "Smoke Wall B"
    if ($wallItems.Count -eq 0) {
        throw "No 'Smoke Wall B' tree item found after drag-out."
    }

    foreach ($item in $wallItems) {
        $parent = $walker.GetParent($item)
        if ($null -ne $parent -and (Get-ElementText -Element $parent) -like "*Smoke Target Folder*") {
            throw "'Smoke Wall B' is still visually nested under Smoke Target Folder."
        }
    }
}

function Write-SmokeDiagnostics {
    param(
        $Job,
        [System.Diagnostics.Process]$Process
    )

    Write-Host "Smoke diagnostics:" -ForegroundColor Yellow
    if ($Job -ne $null) {
        Write-Host "  Temp root: $($Job.Root)" -ForegroundColor Yellow
        Write-Host "  Settings: $($Job.SettingsPath)" -ForegroundColor Yellow
        if (Test-Path -LiteralPath $Job.SettingsPath) {
            Write-Host "  settings.json:" -ForegroundColor Yellow
            Get-Content -LiteralPath $Job.SettingsPath -ErrorAction SilentlyContinue | Select-Object -First 40 | ForEach-Object {
                Write-Host "    $_" -ForegroundColor DarkYellow
            }
        }
    }

    if ($Process -ne $null) {
        try { $Process.Refresh() } catch {}
        Write-Host "  Process: id=$($Process.Id) exited=$($Process.HasExited)" -ForegroundColor Yellow
        if ($Process.HasExited) {
            Write-Host "  ExitCode: $($Process.ExitCode)" -ForegroundColor Yellow
        }
    }

    if ($script:smokeWindow -ne $null) {
        Write-Host "  Window: name='$($script:smokeWindow.Current.Name)' pid=$($script:smokeWindow.Current.ProcessId) type=$($script:smokeWindow.Current.ControlType.ProgrammaticName)" -ForegroundColor Yellow
        $descendants = $script:smokeWindow.FindAll(
            [Windows.Automation.TreeScope]::Descendants,
            [Windows.Automation.Condition]::TrueCondition)
        Write-Host "  Descendant count: $($descendants.Count)" -ForegroundColor Yellow
        for ($i = 0; $i -lt [Math]::Min($descendants.Count, 120); $i++) {
            $element = $descendants.Item($i)
            $name = $element.Current.Name
            $automationId = $element.Current.AutomationId
            if (-not [string]::IsNullOrWhiteSpace($name) -or -not [string]::IsNullOrWhiteSpace($automationId)) {
                Write-Host "    Element[$i] type=$($element.Current.ControlType.ProgrammaticName) name='$name' automationId='$automationId' class='$($element.Current.ClassName)' offscreen=$($element.Current.IsOffscreen)" -ForegroundColor DarkYellow
            }
        }
        $treeItemCondition = New-Object Windows.Automation.PropertyCondition(
            [Windows.Automation.AutomationElement]::ControlTypeProperty,
            [Windows.Automation.ControlType]::TreeItem)
        $treeItems = $script:smokeWindow.FindAll(
            [Windows.Automation.TreeScope]::Descendants,
            $treeItemCondition)
        Write-Host "  TreeItem count: $($treeItems.Count)" -ForegroundColor Yellow
        for ($i = 0; $i -lt [Math]::Min($treeItems.Count, 80); $i++) {
            $item = $treeItems.Item($i)
            Write-Host "    TreeItem[$i] text='$(Get-ElementText -Element $item)' name='$($item.Current.Name)' automationId='$($item.Current.AutomationId)'" -ForegroundColor DarkYellow
        }
    }

    try {
        $uiaRoot = [Windows.Automation.AutomationElement]::RootElement
        $topWindows = $uiaRoot.FindAll(
            [Windows.Automation.TreeScope]::Children,
            [Windows.Automation.Condition]::TrueCondition)
        Write-Host "  Top-level windows matching smoke/app:" -ForegroundColor Yellow
        foreach ($candidate in $topWindows) {
            if ($candidate.Current.ProcessId -eq $Process.Id -or
                $candidate.Current.Name -like "*ourplancore*" -or
                $candidate.Current.Name -like "*TakeoffsSmokeJob*") {
                Write-Host "    pid=$($candidate.Current.ProcessId) name='$($candidate.Current.Name)' type=$($candidate.Current.ControlType.ProgrammaticName)" -ForegroundColor DarkYellow
            }
        }
    } catch {
    }

    foreach ($logPath in @($Job.StdOutLog, $Job.StdErrLog)) {
        if (-not [string]::IsNullOrWhiteSpace($logPath) -and (Test-Path -LiteralPath $logPath)) {
            Write-Host "  Tail ${logPath}:" -ForegroundColor Yellow
            Get-Content -LiteralPath $logPath -Tail 40 -ErrorAction SilentlyContinue | ForEach-Object {
                Write-Host "    $_" -ForegroundColor DarkYellow
            }
        }
    }

    $appLogDir = Join-Path $env:APPDATA "OurPlanCore\logs"
    $appLog = Get-ChildItem -Path $appLogDir -File -Filter *.log -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if ($appLog -ne $null) {
        Write-Host "  Tail $($appLog.FullName):" -ForegroundColor Yellow
        Get-Content -LiteralPath $appLog.FullName -Tail 60 -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Host "    $_" -ForegroundColor DarkYellow
        }
    }
}

$job = $null
$previousSettingsOverride = $null
$proc = $null
try {
    $job = New-SmokeJob
    $previousSettingsOverride = Set-SmokeSettings -Job $job

    $appDll = Join-Path $ProjectRoot "cache\verify_build\ourplancore.dll"
    if (Test-Path -LiteralPath $appDll) {
        $proc = Start-Process -FilePath "dotnet" -ArgumentList @($appDll) -WorkingDirectory $ProjectRoot -RedirectStandardOutput $job.StdOutLog -RedirectStandardError $job.StdErrLog -PassThru
    } else {
        $projectPath = Join-Path $ProjectRoot "ourplancore.csproj"
        $proc = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--no-restore", "--project", $projectPath) -WorkingDirectory $ProjectRoot -RedirectStandardOutput $job.StdOutLog -RedirectStandardError $job.StdErrLog -PassThru
    }
    Wait-Until -TimeoutSeconds $TimeoutSeconds -Message "takeoffs move smoke report" -Condition {
        if (Test-Path -LiteralPath $job.ReportPath) { return $true }
        if ($proc -ne $null) {
            try {
                $proc.Refresh()
                if ($proc.HasExited) { return $true }
            } catch {
            }
        }
        return $false
    } | Out-Null

    if (-not (Test-Path -LiteralPath $job.ReportPath)) {
        $exit = ""
        if ($proc -ne $null) {
            try {
                $proc.Refresh()
                if ($proc.HasExited) { $exit = " ExitCode=$($proc.ExitCode)." }
            } catch {
            }
        }
        throw "Takeoffs move smoke did not write a report.$exit"
    }

    $report = Get-Content -LiteralPath $job.ReportPath -Raw | ConvertFrom-Json
    if (-not $report.Passed) {
        Write-Host "Smoke timings: pages=$($report.PageCount), takeoffs=$($report.TakeoffItemCount), selection avg=$($report.SelectionAverageMilliseconds) ms max=$($report.SelectionMaxMilliseconds) ms eventAvg=$($report.SelectionEventAverageMilliseconds) ms takeoffsLayoutAvg=$($report.SelectionTakeoffsLayoutAverageMilliseconds) ms pagesLayoutAvg=$($report.SelectionPagesLayoutAverageMilliseconds) ms, folder create=$($report.CreateFolderMilliseconds) ms, bulk total=$($report.BulkCopyMilliseconds) ms flush=$($report.BulkFlushMilliseconds) ms files=$($report.BulkFileOperationMilliseconds) ms ui=$($report.BulkUiRefreshMilliseconds) ms." -ForegroundColor Yellow
        Write-Host "Bulk UI detail: load=$($report.BulkCopyLoadMilliseconds) ms append=$($report.BulkCopyAppendMilliseconds) ms viewport=$($report.BulkCopyViewportMilliseconds) ms selection=$($report.BulkCopySelectionMilliseconds) ms pages=$($report.BulkCopyPageIndicatorsMilliseconds) ms legend=$($report.BulkCopyLegendMilliseconds) ms estimate=$($report.BulkCopyEstimateMilliseconds) ms total=$($report.BulkCopyTotalMilliseconds) ms." -ForegroundColor Yellow
        $failures = @($report.Failures) -join "; "
        throw "Takeoffs move smoke failed: $failures"
    }

    Write-Host "PASS takeoffs tree large smoke: pages=$($report.PageCount), takeoffs=$($report.TakeoffItemCount), selection avg=$($report.SelectionAverageMilliseconds) ms max=$($report.SelectionMaxMilliseconds) ms eventAvg=$($report.SelectionEventAverageMilliseconds) ms takeoffsLayoutAvg=$($report.SelectionTakeoffsLayoutAverageMilliseconds) ms pagesLayoutAvg=$($report.SelectionPagesLayoutAverageMilliseconds) ms, folder create=$($report.CreateFolderMilliseconds) ms, copied $($report.BulkCopyCount) in $($report.BulkCopyMilliseconds) ms (flush=$($report.BulkFlushMilliseconds) ms, files=$($report.BulkFileOperationMilliseconds) ms, ui=$($report.BulkUiRefreshMilliseconds) ms, pages=$($report.BulkCopyPageIndicatorsMilliseconds) ms, estimate=$($report.BulkCopyEstimateMilliseconds) ms)." -ForegroundColor Green
}
catch {
    Write-SmokeDiagnostics -Job $job -Process $proc
    throw
}
finally {
    if ($proc -ne $null -and -not $KeepAppOpen) {
        try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch {}
    }
    Restore-SmokeSettings $previousSettingsOverride
    if ($job -ne $null -and -not $KeepAppOpen) {
        try { Remove-Item -LiteralPath $job.Root -Recurse -Force -ErrorAction SilentlyContinue } catch {}
    }
}

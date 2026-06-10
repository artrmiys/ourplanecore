param(
    [Parameter(Mandatory)] [string]$BaselinePath,
    [Parameter(Mandatory)] [string]$CurrentPath,
    [double]$MaxRegressionRatio = 1.15,
    [int]$MaxStepRegressionMs = 250,
    [int]$MaxReadyRegressionMs = 250,
    [int]$MaxPaintRegressionMs = 12,
    [int]$MaxRenderRegressionMs = 500,
    [int]$MaxSlowFrameRegression = 3,
    [int]$MaxWorkingSetRegressionMb = 256,
    [int]$MaxDecodeRegressionMs = 80,
    [int]$MaxQueueRegressionCount = 10,
    [int]$MaxRepaintRegressionCount = 80,
    [double]$MaxCacheHitDrop = 0.10,
    [double]$MaxCoalesceDrop = 0.10,
    [switch]$FailOnRegression
)

$ErrorActionPreference = "Stop"

function Read-SmokeReport {
    param([Parameter(Mandatory)] [string]$Path)

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    Get-Content -LiteralPath $resolved -Raw | ConvertFrom-Json
}

function Get-SmokeSteps {
    param([Parameter(Mandatory)] $Report)

    $items = @()
    $items += @($Report.OpenResults)
    $items += @($Report.ReturnResults)
    $items += @($Report.TabResults)
    $items += @($Report.TabReturnResults)
    $items
}

function Max-Property {
    param(
        [Parameter(Mandatory)] [object[]]$Items,
        [Parameter(Mandatory)] [string]$Name
    )

    if ($Items.Count -eq 0) { return 0 }
    $value = ($Items | Measure-Object -Property $Name -Maximum).Maximum
    if ($null -eq $value) { return 0 }
    [double]$value
}

function Number-OrZero {
    param($Value)

    if ($null -eq $Value) { return 0 }
    [double]$Value
}

function Has-SummaryMetric {
    param(
        [Parameter(Mandatory)] $Report,
        [Parameter(Mandatory)] [string]$Name
    )

    $summary = $Report.Performance.Summary
    if ($null -eq $summary) { return $false }
    @($summary.PSObject.Properties.Name) -contains $Name
}

function Get-SmokeMetrics {
    param([Parameter(Mandatory)] $Report)

    $summary = $Report.Performance.Summary
    $steps = @(Get-SmokeSteps -Report $Report)
    [ordered]@{
        Passed = if ($Report.Passed) { 1 } else { 0 }
        DurationMs = [double]$summary.DurationMs
        CacheHitRate = [double]$summary.CacheHitRate
        MaxRenderMs = [double]$summary.MaxRenderMs
        SlowFrames = [double]$summary.SlowFrameCount
        MaxSlowFrameMs = [double]$summary.MaxSlowFrameMs
        MaxPaintMs = [double]$summary.MaxPageBitmapPaintMs
        RepaintRequestCount = Number-OrZero $summary.RepaintRequestCount
        RepaintCoalescedCount = Number-OrZero $summary.RepaintCoalescedCount
        RepaintCoalesceRate = Number-OrZero $summary.RepaintCoalesceRate
        CrossThreadRepaintRequestCount = Number-OrZero $summary.CrossThreadRepaintRequestCount
        RenderQueueCount = Number-OrZero $summary.RenderQueueCount
        RenderQueueReplacementCount = Number-OrZero $summary.RenderQueueReplacementCount
        RenderQueueReplacementRate = Number-OrZero $summary.RenderQueueReplacementRate
        RenderQueueWhileBusyCount = Number-OrZero $summary.RenderQueueWhileBusyCount
        BitmapDecodeCount = Number-OrZero $summary.BitmapDecodeCount
        MaxBitmapDecodeMs = Number-OrZero $summary.MaxBitmapDecodeMs
        AverageBitmapDecodeMs = Number-OrZero $summary.AverageBitmapDecodeMs
        WorkingSetMb = [double]$summary.WorkingSetMb
        ManagedMemoryMb = [double]$summary.ManagedMemoryMb
        MaxStepMs = Max-Property -Items $steps -Name "ElapsedMs"
        MaxReadyMs = Max-Property -Items $steps -Name "RenderReadyMs"
        MaxZoomMs = Max-Property -Items $steps -Name "ZoomExerciseMs"
        MaxPostZoomReadyMs = Max-Property -Items $steps -Name "PostZoomRenderReadyMs"
        InitialPageSettleMs = Number-OrZero $Report.InitialPageSettleMs
    }
}

function Test-LowerIsBetterRegression {
    param(
        [double]$Baseline,
        [double]$Current,
        [double]$Ratio,
        [double]$Absolute
    )

    if ($Current -le $Baseline) { return $false }
    $delta = $Current - $Baseline
    return $delta -gt $Absolute -and $Current -gt ($Baseline * $Ratio)
}

function New-ComparisonRow {
    param(
        [string]$Metric,
        [double]$Baseline,
        [double]$Current,
        [bool]$HigherIsBetter,
        [double]$AbsoluteBudget,
        [double]$RatioBudget,
        [double]$DropBudget = 0,
        [bool]$BaselineMissing = $false
    )

    $delta = $Current - $Baseline
    $status = "ok"
    if ($BaselineMissing) {
        $status = "new metric"
    } elseif ($HigherIsBetter) {
        if (($Baseline - $Current) -gt $DropBudget) {
            $status = "regressed"
        } elseif ($Current -gt $Baseline) {
            $status = "improved"
        }
    } else {
        if (Test-LowerIsBetterRegression -Baseline $Baseline -Current $Current -Ratio $RatioBudget -Absolute $AbsoluteBudget) {
            $status = "regressed"
        } elseif ($Current -lt $Baseline) {
            $status = "improved"
        }
    }

    [pscustomobject]@{
        Metric = $Metric
        Baseline = [math]::Round($Baseline, 4)
        Current = [math]::Round($Current, 4)
        Delta = [math]::Round($delta, 4)
        Status = $status
    }
}

$baseline = Read-SmokeReport -Path $BaselinePath
$current = Read-SmokeReport -Path $CurrentPath
$baseMetrics = Get-SmokeMetrics -Report $baseline
$currentMetrics = Get-SmokeMetrics -Report $current

$rows = @()
$rows += New-ComparisonRow "DurationMs" $baseMetrics.DurationMs $currentMetrics.DurationMs $false 1000 $MaxRegressionRatio
$rows += New-ComparisonRow "CacheHitRate" $baseMetrics.CacheHitRate $currentMetrics.CacheHitRate $true 0 $MaxRegressionRatio $MaxCacheHitDrop
$rows += New-ComparisonRow "MaxRenderMs" $baseMetrics.MaxRenderMs $currentMetrics.MaxRenderMs $false $MaxRenderRegressionMs $MaxRegressionRatio
$rows += New-ComparisonRow "SlowFrames" $baseMetrics.SlowFrames $currentMetrics.SlowFrames $false $MaxSlowFrameRegression 1.0
$rows += New-ComparisonRow "MaxSlowFrameMs" $baseMetrics.MaxSlowFrameMs $currentMetrics.MaxSlowFrameMs $false $MaxPaintRegressionMs $MaxRegressionRatio
$rows += New-ComparisonRow "MaxPaintMs" $baseMetrics.MaxPaintMs $currentMetrics.MaxPaintMs $false $MaxPaintRegressionMs $MaxRegressionRatio
$rows += New-ComparisonRow "RepaintRequestCount" $baseMetrics.RepaintRequestCount $currentMetrics.RepaintRequestCount $false $MaxRepaintRegressionCount $MaxRegressionRatio 0 (-not (Has-SummaryMetric $baseline "RepaintRequestCount"))
$rows += New-ComparisonRow "RepaintCoalesceRate" $baseMetrics.RepaintCoalesceRate $currentMetrics.RepaintCoalesceRate $true 0 $MaxRegressionRatio $MaxCoalesceDrop (-not (Has-SummaryMetric $baseline "RepaintCoalesceRate"))
$rows += New-ComparisonRow "RenderQueueCount" $baseMetrics.RenderQueueCount $currentMetrics.RenderQueueCount $false $MaxQueueRegressionCount $MaxRegressionRatio 0 (-not (Has-SummaryMetric $baseline "RenderQueueCount"))
$rows += New-ComparisonRow "RenderQueueReplacementCount" $baseMetrics.RenderQueueReplacementCount $currentMetrics.RenderQueueReplacementCount $false $MaxQueueRegressionCount $MaxRegressionRatio 0 (-not (Has-SummaryMetric $baseline "RenderQueueReplacementCount"))
$rows += New-ComparisonRow "RenderQueueWhileBusyCount" $baseMetrics.RenderQueueWhileBusyCount $currentMetrics.RenderQueueWhileBusyCount $false $MaxQueueRegressionCount $MaxRegressionRatio 0 (-not (Has-SummaryMetric $baseline "RenderQueueWhileBusyCount"))
$rows += New-ComparisonRow "BitmapDecodeCount" $baseMetrics.BitmapDecodeCount $currentMetrics.BitmapDecodeCount $false $MaxQueueRegressionCount $MaxRegressionRatio 0 (-not (Has-SummaryMetric $baseline "BitmapDecodeCount"))
$rows += New-ComparisonRow "MaxBitmapDecodeMs" $baseMetrics.MaxBitmapDecodeMs $currentMetrics.MaxBitmapDecodeMs $false $MaxDecodeRegressionMs $MaxRegressionRatio 0 (-not (Has-SummaryMetric $baseline "MaxBitmapDecodeMs"))
$rows += New-ComparisonRow "AverageBitmapDecodeMs" $baseMetrics.AverageBitmapDecodeMs $currentMetrics.AverageBitmapDecodeMs $false $MaxDecodeRegressionMs $MaxRegressionRatio 0 (-not (Has-SummaryMetric $baseline "AverageBitmapDecodeMs"))
$rows += New-ComparisonRow "WorkingSetMb" $baseMetrics.WorkingSetMb $currentMetrics.WorkingSetMb $false $MaxWorkingSetRegressionMb $MaxRegressionRatio
$rows += New-ComparisonRow "ManagedMemoryMb" $baseMetrics.ManagedMemoryMb $currentMetrics.ManagedMemoryMb $false $MaxWorkingSetRegressionMb $MaxRegressionRatio
$rows += New-ComparisonRow "MaxStepMs" $baseMetrics.MaxStepMs $currentMetrics.MaxStepMs $false $MaxStepRegressionMs $MaxRegressionRatio
$rows += New-ComparisonRow "MaxReadyMs" $baseMetrics.MaxReadyMs $currentMetrics.MaxReadyMs $false $MaxReadyRegressionMs $MaxRegressionRatio
$rows += New-ComparisonRow "MaxZoomMs" $baseMetrics.MaxZoomMs $currentMetrics.MaxZoomMs $false $MaxStepRegressionMs $MaxRegressionRatio
$rows += New-ComparisonRow "MaxPostZoomReadyMs" $baseMetrics.MaxPostZoomReadyMs $currentMetrics.MaxPostZoomReadyMs $false $MaxReadyRegressionMs $MaxRegressionRatio
$rows += New-ComparisonRow "InitialPageSettleMs" $baseMetrics.InitialPageSettleMs $currentMetrics.InitialPageSettleMs $false $MaxReadyRegressionMs $MaxRegressionRatio

Write-Host "Viewport smoke comparison:" -ForegroundColor Cyan
Write-Host ("  baseline: {0}" -f (Resolve-Path -LiteralPath $BaselinePath).Path)
Write-Host ("  current : {0}" -f (Resolve-Path -LiteralPath $CurrentPath).Path)
$rows | Format-Table -AutoSize | Out-String -Width 220 | Write-Host

$regressions = @($rows | Where-Object { $_.Status -eq "regressed" })
if ($regressions.Count -gt 0) {
    Write-Host ("Regressions: {0}" -f ($regressions.Metric -join ", ")) -ForegroundColor Yellow
    if ($FailOnRegression) {
        exit 2
    }
}

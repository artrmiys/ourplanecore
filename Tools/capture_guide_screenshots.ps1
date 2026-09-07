param(
    [string]$ProjectRoot = "",
    [int]$TimeoutSeconds = 240,
    [int]$CaptureTimeoutMs = 12000,
    [switch]$SkipBuild
)

# Drives OurPlanCore in guide-screenshot capture mode: the app builds a fresh sample job,
# walks every workspace surface, renders each to a real PNG, then exits. The PNGs are copied
# into Assets\GuideScreenshots so SampleJobGuideBuilder can embed them in the guide sample.

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Join-Path $PSScriptRoot ".."
}
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path

$assetDir = Join-Path $ProjectRoot "Assets\GuideScreenshots"
New-Item -ItemType Directory -Force -Path $assetDir | Out-Null
# Clear stale PNGs so renamed/removed surfaces don't linger.
Get-ChildItem -LiteralPath $assetDir -Filter *.png -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

$captureDir = Join-Path $env:TEMP ("onc_guide_capture_out_" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $captureDir | Out-Null
$manifestPath = Join-Path $captureDir "manifest.json"
$captureSettingsPath = Join-Path $captureDir "settings.json"
$stdoutPath = Join-Path $env:TEMP ("onc_guide_capture_stdout_" + [guid]::NewGuid().ToString("N") + ".txt")
$stderrPath = Join-Path $env:TEMP ("onc_guide_capture_stderr_" + [guid]::NewGuid().ToString("N") + ".txt")

$projectPath = Join-Path $ProjectRoot "ourplancore.csproj"
if (-not $SkipBuild) {
    Write-Host "Building ourplancore..." -ForegroundColor Cyan
    dotnet build $projectPath -nologo -clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { throw "Build failed." }
}

$appDll = Join-Path $ProjectRoot "bin\Debug\net9.0-windows\ourplancore.dll"
if (-not (Test-Path -LiteralPath $appDll)) {
    throw "App dll not found at $appDll. Build first or omit -SkipBuild."
}

$oldCapture = $env:OURPLANCORE_GUIDE_SCREENSHOT_CAPTURE
$oldDir = $env:OURPLANCORE_GUIDE_SCREENSHOT_DIR
$oldManifest = $env:OURPLANCORE_GUIDE_SCREENSHOT_MANIFEST
$oldTimeout = $env:OURPLANCORE_GUIDE_SCREENSHOT_TIMEOUT_MS
$oldSettingsPath = $env:OURPLANCORE_SETTINGS_PATH

try {
    $env:OURPLANCORE_GUIDE_SCREENSHOT_CAPTURE = "1"
    $env:OURPLANCORE_GUIDE_SCREENSHOT_DIR = $captureDir
    $env:OURPLANCORE_GUIDE_SCREENSHOT_MANIFEST = $manifestPath
    $env:OURPLANCORE_GUIDE_SCREENSHOT_TIMEOUT_MS = [string]$CaptureTimeoutMs
    $env:OURPLANCORE_SETTINGS_PATH = $captureSettingsPath

    Write-Host "Running capture (output -> $captureDir)..." -ForegroundColor Cyan
    $proc = Start-Process -FilePath "dotnet" -ArgumentList @($appDll) -WorkingDirectory $ProjectRoot -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not $proc.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 250
        $proc.Refresh()
    }
    if (-not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        throw "Capture timed out after $TimeoutSeconds seconds."
    }

    if (-not (Test-Path -LiteralPath $manifestPath)) {
        if (Test-Path -LiteralPath $stderrPath) {
            Get-Content -LiteralPath $stderrPath -Tail 40 | ForEach-Object { Write-Host "  stderr: $_" -ForegroundColor Red }
        }
        throw "Capture did not write a manifest. Exit code: $($proc.ExitCode)."
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $shots = @($manifest.Screenshots)
    Write-Host "Captured $($shots.Count) screenshots." -ForegroundColor Green
    foreach ($failure in @($manifest.Failures)) {
        Write-Host "  warning: $failure" -ForegroundColor Yellow
    }

    $copied = 0
    foreach ($shot in $shots) {
        if (Test-Path -LiteralPath $shot.Path) {
            $dest = Join-Path $assetDir ("{0}.png" -f $shot.Name)
            Copy-Item -LiteralPath $shot.Path -Destination $dest -Force
            $copied++
            Write-Host ("  {0,-22} {1}x{2}" -f $shot.Name, $shot.Width, $shot.Height)
        }
    }
    Write-Host "Copied $copied PNGs into $assetDir" -ForegroundColor Green

    if (-not $manifest.Passed) {
        Write-Host "Capture reported failures (see warnings above)." -ForegroundColor Yellow
    }
}
finally {
    $env:OURPLANCORE_GUIDE_SCREENSHOT_CAPTURE = $oldCapture
    $env:OURPLANCORE_GUIDE_SCREENSHOT_DIR = $oldDir
    $env:OURPLANCORE_GUIDE_SCREENSHOT_MANIFEST = $oldManifest
    $env:OURPLANCORE_GUIDE_SCREENSHOT_TIMEOUT_MS = $oldTimeout
    $env:OURPLANCORE_SETTINGS_PATH = $oldSettingsPath
    foreach ($p in @($stdoutPath, $stderrPath)) {
        if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue }
    }
    if (Test-Path -LiteralPath $captureDir) {
        Remove-Item -LiteralPath $captureDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

#requires -Version 5.1

<#
.SYNOPSIS
Builds, validates, deploys, stages, and optionally publishes an OurPlanCore release.

.DESCRIPTION
The script never stages or commits source files. It requires tracked source to be
clean, builds from a detached clean worktree so local ignored files cannot enter
the EXE, permits untracked files below docs/, and stops if any ourplancore process
is running. GitHub publication is opt-in and pushes only the current branch plus
the single tag supplied with -ReleaseTag.

.PARAMETER TemplatePath
Explicit path to the live macro-enabled TemplateCom workbook to ship.

.PARAMETER ExpectedTemplateSheets
Workbook sheets that must exist in TemplateCom.xlsm. Defaults to Detailed Frame
List and Front Page.

.PARAMETER GhPath
Optional path to gh.exe. If omitted, PATH and the portable C:\tmp location are checked.

.PARAMETER PublishGitHub
After all local validation succeeds, push the current branch and one release tag,
create a GitHub Release, verify its draft assets, then verify pinned and latest
public downloads anonymously and compare every SHA256 hash.

.PARAMETER ReleaseTag
Exact tag for -PublishGitHub, for example ourplancore-v2.2.3-20260715-abc1234.

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\release\Publish-OurPlanCoreRelease.ps1 `
  -TemplatePath "D:\Templates\TemplateCom.xlsm"

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\release\Publish-OurPlanCoreRelease.ps1 `
  -TemplatePath "D:\Templates\TemplateCom.xlsm" `
  -PublishGitHub -ReleaseTag "ourplancore-v2.2.3-20260715-abc1234"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$TemplatePath,

    [Alias('ExpectedTemplateSheet')]
    [ValidateNotNullOrEmpty()]
    [string[]]$ExpectedTemplateSheets = @('Detailed Frame List', 'Front Page'),

    [ValidateNotNullOrEmpty()]
    [string]$RepoRoot = (Join-Path $env:USERPROFILE 'Desktop\ourplanecore'),

    [ValidateNotNullOrEmpty()]
    [string]$UpdateRoot = (Join-Path $env:USERPROFILE 'Desktop\updates\OurPlanCore'),

    [ValidateNotNullOrEmpty()]
    [string]$ShortcutPath = (Join-Path $env:USERPROFILE 'Desktop\OurPlanCore.lnk'),

    [string]$StagingRoot,

    [string]$GhPath,

    [switch]$PublishGitHub,

    [string]$ReleaseTag,

    [string]$ReleaseTitle,

    [ValidateRange(15, 300)]
    [int]$LogTimeoutSeconds = 75
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$releaseHelpers = Join-Path $PSScriptRoot 'OurPlanCoreRelease.Common.ps1'
$localDeployHelpers = Join-Path $PSScriptRoot 'OurPlanCoreRelease.LocalDeploy.ps1'
$githubHelpers = Join-Path $PSScriptRoot 'OurPlanCoreRelease.GitHub.ps1'
foreach ($helper in @($releaseHelpers, $localDeployHelpers, $githubHelpers)) {
    if (-not [System.IO.File]::Exists($helper)) {
        throw "Release helper file is missing: $helper"
    }
}
. $releaseHelpers
. $localDeployHelpers
. $githubHelpers

if ($PublishGitHub -and [string]::IsNullOrWhiteSpace($ReleaseTag)) {
    throw '-ReleaseTag is required with -PublishGitHub.'
}

$RepoRoot = Resolve-FullPath $RepoRoot
$UpdateRoot = Resolve-FullPath $UpdateRoot
$ShortcutPath = Resolve-FullPath $ShortcutPath
$TemplatePath = Resolve-FullPath $TemplatePath

$solutionPath = Join-Path $RepoRoot 'ourplancore.sln'
$projectPath = Join-Path $RepoRoot 'ourplancore.csproj'
$testProjectPath = Join-Path $RepoRoot 'Tests\OurPlanCore.Tests.csproj'
$pythonTestPath = Join-Path $RepoRoot 'Tests\test_pdf_sheet_metadata_precise_v2.py'
$pythonPath = Join-Path $RepoRoot 'Tools\python\python.exe'
$pythonDependencies = Join-Path $RepoRoot 'Tools\python_deps'
$updateExe = Join-Path $UpdateRoot 'ourplancore.exe'
$updateTemplate = Join-Path $UpdateRoot 'TemplateCom.xlsm'
$updateDownload = Join-Path $UpdateRoot 'DOWNLOAD-LATEST.txt'
$logRoot = Join-Path $env:APPDATA 'OurPlanCore\logs'

foreach ($requiredFile in @($solutionPath, $projectPath, $testProjectPath, $pythonTestPath, $pythonPath, $TemplatePath)) {
    Assert-FileExists -Path $requiredFile
}
if (-not [System.IO.Directory]::Exists($pythonDependencies)) {
    throw "Bundled Python dependencies are missing: $pythonDependencies"
}

Assert-CleanTrackedSource -Root $RepoRoot
$resolvedGhPath = $null
if ($PublishGitHub) {
    $resolvedGhPath = Resolve-GhExecutable -PreferredPath $GhPath
    Invoke-Native -FilePath $resolvedGhPath -Arguments @('auth', 'status', '--hostname', 'github.com') -Description 'Preflight GitHub CLI authentication'
    & git -C $RepoRoot check-ref-format "refs/tags/$ReleaseTag"
    if ($LASTEXITCODE -ne 0) { throw "Invalid release tag: $ReleaseTag" }
}
Assert-NoRunningApp

$sourceCommit = Invoke-NativeCapture -FilePath 'git' -Arguments @('-C', $RepoRoot, 'rev-parse', 'HEAD') -Description 'Read source commit'
$projectXml = [xml][System.IO.File]::ReadAllText($projectPath)
$versionNode = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version')
if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw 'ourplancore.csproj does not contain a Version property.'
}
$version = $versionNode.InnerText.Trim()
$repository = Get-GitHubRepositoryName -Root $RepoRoot
$releaseUtc = [DateTime]::UtcNow
$releaseUtcText = $releaseUtc.ToString('yyyy-MM-ddTHH:mm:ssZ')
$effectiveTag = if ([string]::IsNullOrWhiteSpace($ReleaseTag)) {
    "v$version-r$($releaseUtc.ToString('yyyyMMdd.HHmmss'))"
} else { $ReleaseTag }
if ([string]::IsNullOrWhiteSpace($ReleaseTitle)) {
    $ReleaseTitle = "OurPlanCore $version ($effectiveTag)"
}
$runId = "$($releaseUtc.ToString('yyyyMMdd-HHmmss'))-$($sourceCommit.Substring(0, 12))"
$publishDirectory = Join-Path $RepoRoot "publish\ourplancore-release-$runId"
$cleanSourceRoot = Join-Path $env:TEMP "ourplancore-release-source-$runId"
if ([string]::IsNullOrWhiteSpace($StagingRoot)) {
    $StagingRoot = Join-Path $env:USERPROFILE "Desktop\updates\OurPlanCore-release-staging\$runId"
}
$StagingRoot = Resolve-FullPath $StagingRoot
foreach ($newDirectory in @($publishDirectory, $StagingRoot)) {
    if ([System.IO.Directory]::Exists($newDirectory) -or [System.IO.File]::Exists($newDirectory)) {
        throw "Release output already exists; refusing to overwrite it: $newDirectory"
    }
    [System.IO.Directory]::CreateDirectory($newDirectory) | Out-Null
}

$stageTemplate = Join-Path $StagingRoot 'TemplateCom.xlsm'
Copy-FileExclusive -Source $TemplatePath -Destination $stageTemplate
$frozenTemplateHash = (Get-FileHash -LiteralPath $stageTemplate -Algorithm SHA256).Hash
$stageTemplateAttributes = [System.IO.File]::GetAttributes($stageTemplate)
[System.IO.File]::SetAttributes(
    $stageTemplate,
    $stageTemplateAttributes -bor [System.IO.FileAttributes]::ReadOnly)
Test-XlsmTemplate -Path $stageTemplate -ExpectedSheets $ExpectedTemplateSheets

if ([System.IO.Directory]::Exists($cleanSourceRoot) -or [System.IO.File]::Exists($cleanSourceRoot)) {
    throw "Clean release source path already exists; refusing to overwrite it: $cleanSourceRoot"
}
$buildSolutionPath = Join-Path $cleanSourceRoot 'ourplancore.sln'
$buildProjectPath = Join-Path $cleanSourceRoot 'ourplancore.csproj'
$buildTestProjectPath = Join-Path $cleanSourceRoot 'Tests\OurPlanCore.Tests.csproj'
$buildPythonTestPath = Join-Path $cleanSourceRoot 'Tests\test_pdf_sheet_metadata_precise_v2.py'
$buildPythonPath = Join-Path $cleanSourceRoot 'Tools\python\python.exe'
$buildPythonDependencies = Join-Path $cleanSourceRoot 'Tools\python_deps'
$locationPushed = $false
try {
    Invoke-Native -FilePath 'git' -Arguments @(
        '-C', $RepoRoot, 'worktree', 'add', '--detach', $cleanSourceRoot, $sourceCommit
    ) -Description "Create clean source worktree for $sourceCommit"

    foreach ($cleanFile in @($buildSolutionPath, $buildProjectPath, $buildTestProjectPath, $buildPythonTestPath, $buildPythonPath)) {
        Assert-FileExists -Path $cleanFile
    }
    if (-not [System.IO.Directory]::Exists($buildPythonDependencies)) {
        throw "Bundled Python dependencies are missing from clean source: $buildPythonDependencies"
    }

    Push-Location $cleanSourceRoot
    $locationPushed = $true
    Invoke-Native -FilePath 'dotnet' -Arguments @(
        'restore', $buildSolutionPath, '-warnaserror'
    ) -Description 'Restore solution with zero warnings'
    Invoke-Native -FilePath 'dotnet' -Arguments @(
        'build', $buildSolutionPath, '-c', 'Release', '--no-restore', '-warnaserror',
        '-p:TreatWarningsAsErrors=true'
    ) -Description 'Build ourplancore solution with zero warnings'
    Invoke-Native -FilePath 'dotnet' -Arguments @(
        'run', '--project', $buildTestProjectPath, '-c', 'Release', '--no-build'
    ) -Description 'Run C# console regression harness'

    $oldPythonPath = [Environment]::GetEnvironmentVariable('PYTHONPATH', 'Process')
    $oldNoUserSite = [Environment]::GetEnvironmentVariable('PYTHONNOUSERSITE', 'Process')
    $oldNoBytecode = [Environment]::GetEnvironmentVariable('PYTHONDONTWRITEBYTECODE', 'Process')
    try {
        [Environment]::SetEnvironmentVariable('PYTHONPATH', $buildPythonDependencies, 'Process')
        [Environment]::SetEnvironmentVariable('PYTHONNOUSERSITE', '1', 'Process')
        [Environment]::SetEnvironmentVariable('PYTHONDONTWRITEBYTECODE', '1', 'Process')
        Invoke-Native -FilePath $buildPythonPath -Arguments @($buildPythonTestPath) -Description 'Run precise sheet metadata Python tests'
    }
    finally {
        [Environment]::SetEnvironmentVariable('PYTHONPATH', $oldPythonPath, 'Process')
        [Environment]::SetEnvironmentVariable('PYTHONNOUSERSITE', $oldNoUserSite, 'Process')
        [Environment]::SetEnvironmentVariable('PYTHONDONTWRITEBYTECODE', $oldNoBytecode, 'Process')
    }

    Invoke-Native -FilePath 'dotnet' -Arguments @(
        'restore', $buildProjectPath, '-r', 'win-x64', '-warnaserror'
    ) -Description 'Restore ourplancore win-x64 publish target with zero warnings'
    Invoke-Native -FilePath 'dotnet' -Arguments @(
        'publish', $buildProjectPath,
        '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore',
        '-warnaserror', '-p:TreatWarningsAsErrors=true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:IncludeAllContentForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '-p:DebugType=none',
        '-o', $publishDirectory
    ) -Description 'Publish compressed self-contained single-file release'
}
finally {
    if ($locationPushed) { Pop-Location }
    Remove-CleanReleaseWorktree -Root $RepoRoot -Path $cleanSourceRoot
}

$publishedExe = Join-Path $publishDirectory 'ourplancore.exe'
Assert-FileExists -Path $publishedExe
$publishedInfo = Get-Item -LiteralPath $publishedExe
if ([string]::IsNullOrWhiteSpace($publishedInfo.VersionInfo.ProductVersion) -or
    -not $publishedInfo.VersionInfo.ProductVersion.Contains($sourceCommit)) {
    throw "Published ProductVersion does not contain source commit $sourceCommit."
}

$stageExe = Join-Path $StagingRoot 'ourplancore.exe'
Copy-FileExclusive -Source $publishedExe -Destination $stageExe
[System.IO.File]::SetAttributes(
    $stageExe,
    [System.IO.File]::GetAttributes($stageExe) -bor [System.IO.FileAttributes]::ReadOnly)
Test-XlsmTemplate -Path $stageTemplate -ExpectedSheets $ExpectedTemplateSheets

$publishedHash = (Get-FileHash -LiteralPath $publishedExe -Algorithm SHA256).Hash
$stageExeHash = (Get-FileHash -LiteralPath $stageExe -Algorithm SHA256).Hash
if ($stageExeHash -cne $publishedHash) {
    throw 'Published and frozen staged EXE hashes are not identical.'
}
$exeHash = $stageExeHash
$templateHash = (Get-FileHash -LiteralPath $stageTemplate -Algorithm SHA256).Hash
if ($templateHash -cne $frozenTemplateHash) {
    throw 'Frozen TemplateCom.xlsm changed after validation; release aborted.'
}

$latestBase = "https://github.com/$repository/releases/latest/download"
$pinnedBase = "https://github.com/$repository/releases/download/$effectiveTag"
$downloadPath = Join-Path $StagingRoot 'DOWNLOAD-LATEST.txt'
$downloadText = @"
OurPlanCore release downloads

Version: $version
Release UTC: $releaseUtcText
Source commit: $sourceCommit
Release tag: $effectiveTag

SHA256
ourplancore.exe  $exeHash
TemplateCom.xlsm $templateHash

Latest release page:
https://github.com/$repository/releases/latest

Latest application:
$latestBase/ourplancore.exe

Latest Excel template:
$latestBase/TemplateCom.xlsm

Pinned release page:
https://github.com/$repository/releases/tag/$effectiveTag

Pinned application:
$pinnedBase/ourplancore.exe

Pinned Excel template:
$pinnedBase/TemplateCom.xlsm

"@
Write-NewUtf8File -Path $downloadPath -Content $downloadText
[System.IO.File]::SetAttributes(
    $downloadPath,
    [System.IO.File]::GetAttributes($downloadPath) -bor [System.IO.FileAttributes]::ReadOnly)
$downloadHash = (Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash

$notesPath = Join-Path $StagingRoot 'RELEASE-NOTES.txt'
$notesText = @"
$ReleaseTitle

Source commit: $sourceCommit
Version: $version
Release UTC: $releaseUtcText
Release tag: $effectiveTag
ProductVersion: $($publishedInfo.VersionInfo.ProductVersion)
Architecture: Windows x64, self-contained compressed single-file

Verification:
- dotnet restore/build completed
- C# console regression harness completed
- precise sheet metadata Python tests completed
- packaged startup log contains Loaded takeoffs and Viewport with no ERROR after Application startup.

SHA256:
$exeHash  ourplancore.exe
$templateHash  TemplateCom.xlsm
$downloadHash  DOWNLOAD-LATEST.txt
"@
Write-NewUtf8File -Path $notesPath -Content $notesText

$deployment = Invoke-TransactionalLocalDeployment -StageRoot $StagingRoot `
    -StageExe $stageExe -StageTemplate $stageTemplate -StageDownload $downloadPath `
    -UpdateExe $updateExe -UpdateTemplate $updateTemplate -UpdateDownload $updateDownload `
    -ShortcutPath $ShortcutPath -LogRoot $logRoot -LogTimeoutSeconds $LogTimeoutSeconds
$validatedLog = $deployment.ValidatedLog

if ($PublishGitHub) {
    Assert-CleanTrackedSource -Root $RepoRoot
    $currentCommit = Invoke-NativeCapture -FilePath 'git' -Arguments @('-C', $RepoRoot, 'rev-parse', 'HEAD') -Description 'Revalidate source commit before GitHub publication'
    if ($currentCommit -cne $sourceCommit) {
        throw "HEAD changed during release: expected $sourceCommit, got $currentCommit."
    }

    $expectedHashes = @{
        'ourplancore.exe' = $exeHash
        'TemplateCom.xlsm' = $templateHash
        'DOWNLOAD-LATEST.txt' = $downloadHash
    }
    Publish-GitHubRelease -Root $RepoRoot -Repository $repository -Tag $ReleaseTag `
        -Title $ReleaseTitle -SourceCommit $sourceCommit -GhExecutable $resolvedGhPath `
        -AssetExe $stageExe -AssetTemplate $stageTemplate -AssetDownload $downloadPath `
        -Stage $StagingRoot -NotesPath $notesPath -ExpectedHashes $expectedHashes
}

Write-Host ''
Write-Host 'OurPlanCore release workflow completed successfully.'
Write-Host "Source commit : $sourceCommit"
Write-Host "Published exe : $publishedExe"
Write-Host "Installed exe : $updateExe"
Write-Host "Exe backup   : $($deployment.ExeBackup)"
Write-Host "Template     : $updateTemplate"
Write-Host "Template bak : $($deployment.TemplateBackup)"
Write-Host "Download note: $updateDownload"
Write-Host "Download bak : $($deployment.DownloadBackup)"
Write-Host "Staging      : $StagingRoot"
Write-Host "SHA256       : $exeHash"
Write-Host "Shortcut     : $ShortcutPath"
Write-Host "Validated log: $validatedLog"
Write-Host "GitHub       : $(if ($PublishGitHub) { $ReleaseTag } else { 'not requested' })"

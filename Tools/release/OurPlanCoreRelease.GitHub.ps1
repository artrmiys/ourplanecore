#requires -Version 5.1

# GitHub release state helpers. Dot-source after OurPlanCoreRelease.Common.ps1.
Set-StrictMode -Version Latest

function Get-CurrentLatestReleaseTag {
    param(
        [Parameter(Mandatory = $true)][string]$GhExecutable,
        [Parameter(Mandatory = $true)][string]$Repository
    )

    return Invoke-NativeCapture -FilePath $GhExecutable -Arguments @(
        'release', 'view', '--repo', $Repository,
        '--json', 'tagName', '--jq', '.tagName'
    ) -Description 'Read current latest GitHub Release tag'
}

function Initialize-GitHubReleaseAssets {
    param(
        [Parameter(Mandatory = $true)][string]$GhExecutable,
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][string]$NotesPath,
        [Parameter(Mandatory = $true)][string[]]$AssetPaths,
        [Parameter(Mandatory = $true)][string[]]$ExpectedNames
    )

    # A failed upload can leave a partial draft, and an interrupted anonymous
    # check can leave a prerelease. Both states are resumable for the exact tag.
    $viewResult = Invoke-NativeProbe -FilePath $GhExecutable -Arguments @(
        'release', 'view', $Tag, '--repo', $Repository,
        '--json', 'assets,isDraft,isPrerelease,url')
    if ($viewResult.ExitCode -eq 0) {
        $release = $viewResult.Output | ConvertFrom-Json
        $unexpectedAssets = @($release.assets | Where-Object { $ExpectedNames -cnotcontains $_.name })
        if ($unexpectedAssets.Count -gt 0) {
            throw "Release $Tag has unexpected assets: $($unexpectedAssets.name -join ', ')"
        }

        if (-not $release.isDraft) {
            if ($release.isPrerelease) { return 'Prerelease' }
            throw "Release $Tag is already final; refusing to replace public assets."
        }

        Invoke-Native -FilePath $GhExecutable -Arguments @(
            'release', 'edit', $Tag, '--repo', $Repository,
            '--title', $Title, '--notes-file', $NotesPath
        ) -Description "Refresh draft GitHub Release metadata for $Tag" | Out-Host
        $uploadArguments = @('release', 'upload', $Tag) + $AssetPaths + @(
            '--repo', $Repository, '--clobber'
        )
        Invoke-Native -FilePath $GhExecutable -Arguments $uploadArguments `
            -Description "Resume draft GitHub Release asset upload for $Tag" | Out-Host
        return 'Draft'
    }

    $createArguments = @('release', 'create', $Tag) + $AssetPaths + @(
        '--repo', $Repository,
        '--verify-tag', '--draft',
        '--title', $Title,
        '--notes-file', $NotesPath
    )
    Invoke-Native -FilePath $GhExecutable -Arguments $createArguments `
        -Description "Create draft GitHub Release $Tag" | Out-Host
    return 'Draft'
}

function Set-GitHubReleaseBackToDraft {
    param(
        [Parameter(Mandatory = $true)][string]$GhExecutable,
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][string]$Tag
    )

    Invoke-Native -FilePath $GhExecutable -Arguments @(
        'release', 'edit', $Tag, '--repo', $Repository,
        '--draft', '--prerelease=false'
    ) -Description "Return failed prerelease $Tag to draft" | Out-Host
}

function Restore-PreviousLatestGitHubRelease {
    param(
        [Parameter(Mandatory = $true)][string]$GhExecutable,
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][string]$CurrentTag,
        [Parameter(Mandatory = $true)][string]$PreviousLatestTag
    )

    if ([string]::IsNullOrWhiteSpace($PreviousLatestTag) -or $PreviousLatestTag -ceq $CurrentTag) {
        throw 'No distinct previous latest GitHub Release is available for rollback.'
    }
    Invoke-Native -FilePath $GhExecutable -Arguments @(
        'release', 'edit', $CurrentTag, '--repo', $Repository, '--prerelease'
    ) -Description "Return failed latest release $CurrentTag to prerelease" | Out-Host
    Invoke-Native -FilePath $GhExecutable -Arguments @(
        'release', 'edit', $PreviousLatestTag, '--repo', $Repository, '--latest'
    ) -Description "Restore previous latest release $PreviousLatestTag" | Out-Host
}

function Assert-FinalGitHubReleaseState {
    param(
        [Parameter(Mandatory = $true)][string]$GhExecutable,
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][string]$Tag
    )

    $json = Invoke-NativeCapture -FilePath $GhExecutable -Arguments @(
        'release', 'view', $Tag, '--repo', $Repository,
        '--json', 'url,isDraft,isPrerelease,tagName'
    ) -Description 'Read final GitHub Release state'
    $release = $json | ConvertFrom-Json
    if ($release.isDraft -or $release.isPrerelease -or $release.tagName -cne $Tag) {
        throw "GitHub Release $Tag is not in the required final state."
    }
    $latestTag = Get-CurrentLatestReleaseTag -GhExecutable $GhExecutable -Repository $Repository
    if ($latestTag -cne $Tag) {
        throw "GitHub latest tag is $latestTag, expected $Tag."
    }
    Write-Host "Published GitHub Release: $($release.url)"
}

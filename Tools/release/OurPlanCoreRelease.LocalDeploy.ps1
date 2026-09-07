#requires -Version 5.1

# Transactional local deployment helpers. Dot-source after the common helpers.
Set-StrictMode -Version Latest

function New-FileRollbackSnapshot {
    param(
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$SnapshotRoot,
        [Parameter(Mandatory = $true)][string]$SnapshotName
    )

    [System.IO.Directory]::CreateDirectory($SnapshotRoot) | Out-Null
    $existed = [System.IO.File]::Exists($Destination)
    $snapshotPath = Join-Path $SnapshotRoot $SnapshotName
    if ($existed) {
        Copy-FileExclusive -Source $Destination -Destination $snapshotPath
        $sourceHash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
        $snapshotHash = (Get-FileHash -LiteralPath $snapshotPath -Algorithm SHA256).Hash
        if ($snapshotHash -cne $sourceHash) {
            throw "Rollback snapshot hash mismatch: $Destination"
        }
    }

    return [pscustomobject]@{
        Destination = $Destination
        Existed = $existed
        SnapshotPath = $snapshotPath
    }
}

function Restore-FileRollbackSnapshot {
    param([Parameter(Mandatory = $true)]$Snapshot)

    $destination = [string]$Snapshot.Destination
    if (-not [bool]$Snapshot.Existed) {
        if ([System.IO.File]::Exists($destination)) {
            [System.IO.File]::SetAttributes($destination, [System.IO.FileAttributes]::Normal)
            [System.IO.File]::Delete($destination)
        }
        return
    }

    $source = [string]$Snapshot.SnapshotPath
    Assert-FileExists -Path $source
    $destinationDirectory = Split-Path -Parent $destination
    [System.IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null
    $temporary = Join-Path $destinationDirectory ".ourplancore.rollback-$PID-$([Guid]::NewGuid().ToString('N')).tmp"
    $replaceBackup = Join-Path $destinationDirectory ".ourplancore.rollback-replace-$PID-$([Guid]::NewGuid().ToString('N')).tmp"
    $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    try {
        Copy-FileExclusive -Source $source -Destination $temporary
        if ([System.IO.File]::Exists($destination)) {
            [System.IO.File]::SetAttributes($destination, [System.IO.FileAttributes]::Normal)
            [System.IO.File]::Replace($temporary, $destination, $replaceBackup, $true)
        }
        else {
            [System.IO.File]::Move($temporary, $destination)
        }
    }
    finally {
        if ([System.IO.File]::Exists($temporary)) {
            [System.IO.File]::Delete($temporary)
        }
        if ([System.IO.File]::Exists($replaceBackup)) {
            [System.IO.File]::Delete($replaceBackup)
        }
    }
    $restoredHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    if ($restoredHash -cne $sourceHash) {
        throw "Restored file hash mismatch: $destination"
    }
}

function Restore-FileRollbackSnapshots {
    param([Parameter(Mandatory = $true)][object[]]$Snapshots)

    $errors = New-Object System.Collections.Generic.List[string]
    foreach ($snapshot in $Snapshots) {
        try { Restore-FileRollbackSnapshot -Snapshot $snapshot }
        catch { $errors.Add($_.Exception.Message) }
    }
    if ($errors.Count -gt 0) {
        throw ($errors -join ' | ')
    }
}

function Invoke-TransactionalLocalDeployment {
    param(
        [Parameter(Mandatory = $true)][string]$StageRoot,
        [Parameter(Mandatory = $true)][string]$StageExe,
        [Parameter(Mandatory = $true)][string]$StageTemplate,
        [Parameter(Mandatory = $true)][string]$StageDownload,
        [Parameter(Mandatory = $true)][string]$UpdateExe,
        [Parameter(Mandatory = $true)][string]$UpdateTemplate,
        [Parameter(Mandatory = $true)][string]$UpdateDownload,
        [Parameter(Mandatory = $true)][string]$ShortcutPath,
        [Parameter(Mandatory = $true)][string]$LogRoot,
        [Parameter(Mandatory = $true)][int]$LogTimeoutSeconds
    )

    $snapshotRoot = Join-Path $StageRoot 'local-rollback-snapshots'
    $exeSnapshot = New-FileRollbackSnapshot -Destination $UpdateExe -SnapshotRoot $snapshotRoot -SnapshotName 'ourplancore.exe.before'
    $templateSnapshot = New-FileRollbackSnapshot -Destination $UpdateTemplate -SnapshotRoot $snapshotRoot -SnapshotName 'TemplateCom.xlsm.before'
    $downloadSnapshot = New-FileRollbackSnapshot -Destination $UpdateDownload -SnapshotRoot $snapshotRoot -SnapshotName 'DOWNLOAD-LATEST.txt.before'
    $shortcutSnapshot = New-FileRollbackSnapshot -Destination $ShortcutPath -SnapshotRoot $snapshotRoot -SnapshotName 'OurPlanCore.lnk.before'
    try {
        $exeResult = Install-FileSafely -SourcePath $StageExe -DestinationPath $UpdateExe
        $templateResult = Install-FileSafely -SourcePath $StageTemplate -DestinationPath $UpdateTemplate
        $downloadResult = Install-FileSafely -SourcePath $StageDownload -DestinationPath $UpdateDownload
        Set-OurPlanCoreShortcut -Path $ShortcutPath -TargetExe $UpdateExe
        $validatedLog = Start-AndValidateLatestLog -ExePath $UpdateExe -LogRoot $LogRoot -TimeoutSeconds $LogTimeoutSeconds
        return [pscustomobject]@{
            ExeBackup = $exeResult.BackupPath
            TemplateBackup = $templateResult.BackupPath
            DownloadBackup = $downloadResult.BackupPath
            ValidatedLog = $validatedLog
        }
    }
    catch {
        $deploymentError = $_.Exception.Message
        try {
            Restore-FileRollbackSnapshots -Snapshots @(
                $shortcutSnapshot, $downloadSnapshot, $templateSnapshot, $exeSnapshot)
        }
        catch {
            throw "Local deployment failed: $deploymentError Rollback also failed: $($_.Exception.Message)"
        }
        throw "Local deployment failed; previous package restored: $deploymentError"
    }
}

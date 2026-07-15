#requires -Version 5.1

# Internal release helpers. Dot-source only from Publish-OurPlanCoreRelease.ps1.
Set-StrictMode -Version Latest

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.Path]::GetFullPath($Path)
}

function Assert-FileExists {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not [System.IO.File]::Exists($Path)) {
        throw "Required file does not exist: $Path"
    }
}

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Description
    )

    Write-Host "==> $Description"
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Invoke-NativeCapture {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $output = @(& $FilePath @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $detail = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
        throw "$Description failed with exit code $LASTEXITCODE.`n$detail"
    }

    return (($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine).Trim()
}

function Invoke-NativeProbe {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $oldPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $FilePath @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = (($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine).Trim()
    }
}

function Assert-CleanTrackedSource {
    param([Parameter(Mandatory = $true)][string]$Root)

    & git -C $Root diff --quiet --ignore-submodules --
    if ($LASTEXITCODE -ne 0) {
        throw 'Tracked working-tree changes exist. Release aborted.'
    }

    & git -C $Root diff --cached --quiet --ignore-submodules --
    if ($LASTEXITCODE -ne 0) {
        throw 'Staged changes exist. Release aborted.'
    }

    $untracked = @(& git -C $Root -c core.quotepath=false ls-files --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect untracked files.'
    }

    $unexpected = @($untracked | Where-Object {
        $normalized = $_.Replace('\', '/')
        -not $normalized.StartsWith('docs/', [StringComparison]::OrdinalIgnoreCase)
    })
    if ($unexpected.Count -gt 0) {
        throw "Unexpected untracked files exist outside docs/:`n$($unexpected -join "`n")"
    }
}

function Assert-NoRunningApp {
    $running = @(Get-Process -Name 'ourplancore' -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0) {
        return
    }

    $details = foreach ($process in $running) {
        $path = '<path unavailable>'
        try { $path = $process.Path } catch { }
        "PID=$($process.Id) Path=$path"
    }
    throw "Close every running ourplancore process before release. The script never stops processes.`n$($details -join "`n")"
}

function Remove-CleanReleaseWorktree {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $fullPath = Resolve-FullPath $Path
    $tempRoot = (Resolve-FullPath $env:TEMP).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $tempPrefix = $tempRoot + [System.IO.Path]::DirectorySeparatorChar
    $leaf = Split-Path -Leaf $fullPath
    if (-not $fullPath.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not $leaf.StartsWith('ourplancore-release-source-', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean an unexpected release worktree path: $fullPath"
    }

    if (-not [System.IO.Directory]::Exists($fullPath)) {
        & git -C $Root worktree prune
        if ($LASTEXITCODE -ne 0) { throw 'Unable to prune incomplete release worktree metadata.' }
        return
    }

    $removeResult = Invoke-NativeProbe -FilePath 'git' -Arguments @(
        '-C', $Root, 'worktree', 'remove', '--force', $fullPath)
    if ($removeResult.ExitCode -eq 0) { return }

    & git -C $Root worktree prune
    if ($LASTEXITCODE -ne 0) { throw 'Unable to prune release worktree metadata after remove failure.' }
    Remove-Item -LiteralPath $fullPath -Recurse -Force
    & git -C $Root worktree prune
    if ($LASTEXITCODE -ne 0 -or [System.IO.Directory]::Exists($fullPath)) {
        throw "Unable to remove clean release worktree: $fullPath"
    }
}

function Get-ReleaseSecretPatterns {
    return [ordered]@{
        'OpenAI API key' = 'sk-(?:proj-)?[A-Za-z0-9_-]{20,}'
        'GitHub token' = '(?:ghp|github_pat)_[A-Za-z0-9_]{20,}'
        'AWS access key' = 'AKIA[0-9A-Z]{16}'
        'literal Bearer token' = 'Bearer[ \t]+[A-Za-z0-9._~+/-]{20,}'
        'JWT token' = 'eyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}'
        'connection password' = '(?:Password|Pwd)\s*=\s*[^;\s<"]{8,}'
        'Azure storage key' = 'AccountKey=[A-Za-z0-9+/=]{20,}'
    }
}

function Test-XlsmPackageForReleaseSecrets {
    param([Parameter(Mandatory = $true)]$Archive)

    $maximumEntryBytes = 64MB
    $maximumTotalBytes = 256MB
    [long]$totalBytes = 0
    foreach ($entry in $Archive.Entries) {
        $totalBytes += $entry.Length
        if ($entry.Length -gt $maximumEntryBytes -or $totalBytes -gt $maximumTotalBytes) {
            throw 'Template package is too large for complete in-memory secret validation.'
        }
        if ($entry.Length -le 0) { continue }

        $stream = $entry.Open()
        $memory = New-Object System.IO.MemoryStream
        try {
            $stream.CopyTo($memory)
            $bytes = $memory.ToArray()
        }
        finally {
            $memory.Dispose()
            $stream.Dispose()
        }
        Assert-NoReleaseSecretsInText -Text ([System.Text.Encoding]::ASCII.GetString($bytes)) `
            -SourceDescription 'Template package content'
        Assert-NoReleaseSecretsInText -Text ([System.Text.Encoding]::Unicode.GetString($bytes)) `
            -SourceDescription 'Template package content'
    }
}

function Assert-NoReleaseSecretsInText {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$SourceDescription
    )

    foreach ($category in (Get-ReleaseSecretPatterns).Keys) {
        $pattern = (Get-ReleaseSecretPatterns)[$category]
        if ([regex]::IsMatch(
                $Text,
                $pattern,
                [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            throw "$SourceDescription contains a suspected embedded $category. Remove it before public release."
        }
    }
}

function Test-XlsmVbaSourceWithExcel {
    param([Parameter(Mandatory = $true)][string]$Path)

    $excel = $null
    $workbooks = $null
    $workbook = $null
    $project = $null
    $components = $null
    try {
        $excel = New-Object -ComObject Excel.Application
        $excel.Visible = $false
        $excel.DisplayAlerts = $false
        $excel.AskToUpdateLinks = $false
        $excel.AutomationSecurity = 3 # msoAutomationSecurityForceDisable
        $workbooks = $excel.Workbooks
        $workbook = $workbooks.Open($Path, 0, $true)
        $project = $workbook.VBProject
        $components = $project.VBComponents

        for ($index = 1; $index -le $components.Count; $index++) {
            $component = $null
            $module = $null
            try {
                $component = $components.Item($index)
                $module = $component.CodeModule
                if ($module.CountOfLines -gt 0) {
                    $source = $module.Lines(1, $module.CountOfLines)
                    Assert-NoReleaseSecretsInText -Text $source -SourceDescription 'Template VBA source'
                }
            }
            finally {
                if ($null -ne $module) {
                    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($module)
                }
                if ($null -ne $component) {
                    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($component)
                }
            }
        }
    }
    catch {
        throw "Unable to inspect TemplateCom VBA source safely through Excel: $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $components) {
            [void][Runtime.InteropServices.Marshal]::ReleaseComObject($components)
        }
        if ($null -ne $project) {
            [void][Runtime.InteropServices.Marshal]::ReleaseComObject($project)
        }
        if ($null -ne $workbook) {
            $workbook.Close($false)
            [void][Runtime.InteropServices.Marshal]::ReleaseComObject($workbook)
        }
        if ($null -ne $workbooks) {
            [void][Runtime.InteropServices.Marshal]::ReleaseComObject($workbooks)
        }
        if ($null -ne $excel) {
            $excel.Quit()
            [void][Runtime.InteropServices.Marshal]::ReleaseComObject($excel)
        }
        [GC]::Collect()
        [GC]::WaitForPendingFinalizers()
        [GC]::Collect()
        [GC]::WaitForPendingFinalizers()
    }
}

function Test-XlsmTemplate {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string[]]$ExpectedSheets
    )

    if (-not $Path.EndsWith('.xlsm', [StringComparison]::OrdinalIgnoreCase)) {
        throw "TemplatePath must point to an .xlsm file: $Path"
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        foreach ($entryName in @('[Content_Types].xml', 'xl/workbook.xml', 'xl/vbaProject.bin')) {
            $entry = $archive.GetEntry($entryName)
            if ($null -eq $entry -or $entry.Length -le 0) {
                throw "Template is missing required XLSM entry: $entryName"
            }
        }

        Test-XlsmPackageForReleaseSecrets -Archive $archive

        $workbookEntry = $archive.GetEntry('xl/workbook.xml')
        $reader = New-Object System.IO.StreamReader($workbookEntry.Open())
        try {
            [xml]$workbookXml = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $namespace = New-Object System.Xml.XmlNamespaceManager($workbookXml.NameTable)
        $namespace.AddNamespace('m', 'http://schemas.openxmlformats.org/spreadsheetml/2006/main')
        $sheetNames = @($workbookXml.SelectNodes('//m:sheets/m:sheet', $namespace) |
            ForEach-Object { $_.GetAttribute('name') })
        foreach ($expectedSheet in $ExpectedSheets) {
            if (-not ($sheetNames -ccontains $expectedSheet)) {
                throw "Template sheet '$expectedSheet' was not found. Sheets: $($sheetNames -join ', ')"
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    Test-XlsmVbaSourceWithExcel -Path $Path
}

function Copy-FileExclusive {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $sourceStream = $null
    $destinationStream = $null
    $completed = $false
    try {
        $sourceStream = [System.IO.File]::Open($Source, 'Open', 'Read', 'Read')
        $destinationStream = [System.IO.File]::Open($Destination, 'CreateNew', 'Write', 'None')
        $sourceStream.CopyTo($destinationStream)
        $destinationStream.Flush($true)
        $completed = $true
    }
    finally {
        if ($null -ne $destinationStream) { $destinationStream.Dispose() }
        if ($null -ne $sourceStream) { $sourceStream.Dispose() }
        if (-not $completed -and [System.IO.File]::Exists($Destination)) {
            Remove-Item -LiteralPath $Destination -Force
        }
    }
}

function New-ImmutableBackup {
    param([Parameter(Mandatory = $true)][string]$Source)

    $sourceHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
    $primaryBackup = "$Source.bak"
    if (-not [System.IO.File]::Exists($primaryBackup)) {
        $backupPath = $primaryBackup
    }
    else {
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $prefix = $sourceHash.Substring(0, 12).ToLowerInvariant()
        $backupPath = "$Source.bak-$stamp-$prefix"
        if ([System.IO.File]::Exists($backupPath)) {
            $backupPath = "$backupPath-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
        }
    }

    Copy-FileExclusive -Source $Source -Destination $backupPath
    $backupHash = (Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash
    if ($backupHash -cne $sourceHash) {
        throw "Backup hash mismatch: $backupPath"
    }

    $attributes = [System.IO.File]::GetAttributes($backupPath)
    [System.IO.File]::SetAttributes($backupPath, $attributes -bor [System.IO.FileAttributes]::ReadOnly)
    return $backupPath
}

function Install-FileSafely {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    $destinationDirectory = Split-Path -Parent $DestinationPath
    [System.IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null
    $sourceHash = (Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash
    $backupPath = $null
    if ([System.IO.File]::Exists($DestinationPath)) {
        $backupPath = New-ImmutableBackup -Source $DestinationPath
    }

    $temporaryExe = Join-Path $destinationDirectory ".ourplancore.deploy-$PID-$([Guid]::NewGuid().ToString('N')).tmp"
    $replaceBackup = Join-Path $destinationDirectory ".ourplancore.replace-backup-$PID-$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        Copy-FileExclusive -Source $SourcePath -Destination $temporaryExe
        $temporaryHash = (Get-FileHash -LiteralPath $temporaryExe -Algorithm SHA256).Hash
        if ($temporaryHash -cne $sourceHash) {
            throw "Temporary deploy copy failed SHA256 verification: $SourcePath"
        }

        if ([System.IO.File]::Exists($DestinationPath)) {
            $attributes = [System.IO.File]::GetAttributes($DestinationPath)
            if (($attributes -band [System.IO.FileAttributes]::ReadOnly) -ne 0) {
                [System.IO.File]::SetAttributes(
                    $DestinationPath,
                    $attributes -band (-bnot [System.IO.FileAttributes]::ReadOnly))
            }
            [System.IO.File]::Replace($temporaryExe, $DestinationPath, $replaceBackup, $true)
        }
        else {
            [System.IO.File]::Move($temporaryExe, $DestinationPath)
        }
    }
    finally {
        if ([System.IO.File]::Exists($temporaryExe)) {
            Remove-Item -LiteralPath $temporaryExe -Force
        }
        if ([System.IO.File]::Exists($replaceBackup)) {
            Remove-Item -LiteralPath $replaceBackup -Force
        }
    }

    $installedHash = (Get-FileHash -LiteralPath $DestinationPath -Algorithm SHA256).Hash
    if ($installedHash -cne $sourceHash) {
        throw "Installed file does not match its verified source: $DestinationPath"
    }

    return [pscustomobject]@{ Hash = $installedHash; BackupPath = $backupPath }
}

function Set-OurPlanCoreShortcut {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$TargetExe
    )

    $workingDirectory = Split-Path -Parent $TargetExe
    $shell = New-Object -ComObject WScript.Shell
    try {
        $shortcut = $shell.CreateShortcut($Path)
        $shortcut.TargetPath = $TargetExe
        $shortcut.WorkingDirectory = $workingDirectory
        $shortcut.Arguments = ''
        $shortcut.IconLocation = "$TargetExe,0"
        $shortcut.Save()

        $check = $shell.CreateShortcut($Path)
        if ((Resolve-FullPath $check.TargetPath) -cne (Resolve-FullPath $TargetExe) -or
            (Resolve-FullPath $check.WorkingDirectory) -cne (Resolve-FullPath $workingDirectory)) {
            throw "Shortcut validation failed: $Path"
        }
    }
    finally {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }
}

function Get-GitHubRepositoryName {
    param([Parameter(Mandatory = $true)][string]$Root)

    $remote = Invoke-NativeCapture -FilePath 'git' -Arguments @('-C', $Root, 'remote', 'get-url', 'origin') -Description 'Read origin URL'
    if ($remote -notmatch 'github\.com[/:](?<repo>[^/\s]+/[^/\s]+?)(?:\.git)?$') {
        throw "Cannot derive GitHub owner/repository from origin: $remote"
    }
    return $Matches.repo
}

function Write-NewUtf8File {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $encoding = New-Object System.Text.UTF8Encoding($false)
    $stream = [System.IO.File]::Open($Path, 'CreateNew', 'Write', 'None')
    try {
        $writer = New-Object System.IO.StreamWriter($stream, $encoding)
        try { $writer.Write($Content) } finally { $writer.Dispose() }
    }
    finally {
        $stream.Dispose()
    }
}

function Save-AnonymousHttpAsset {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    Add-Type -AssemblyName System.Net.Http
    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $true
    $client = New-Object System.Net.Http.HttpClient($handler)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd('OurPlanCore-public-release-verifier/1.0')
    $response = $null
    $input = $null
    $output = $null
    $completed = $false
    try {
        $response = $client.GetAsync(
            $Url,
            [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) {
            throw "Anonymous download returned HTTP $([int]$response.StatusCode): $Url"
        }

        $input = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $output = [System.IO.File]::Open($Destination, 'CreateNew', 'Write', 'None')
        $input.CopyTo($output)
        $output.Flush($true)
        $completed = $true
    }
    finally {
        if ($null -ne $output) { $output.Dispose() }
        if ($null -ne $input) { $input.Dispose() }
        if ($null -ne $response) { $response.Dispose() }
        $client.Dispose()
        $handler.Dispose()
        if (-not $completed -and [System.IO.File]::Exists($Destination)) {
            Remove-Item -LiteralPath $Destination -Force
        }
    }
}

function Test-AnonymousReleaseAssets {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [Parameter(Mandatory = $true)][string]$DestinationRoot,
        [Parameter(Mandatory = $true)][hashtable]$ExpectedHashes,
        [ValidateRange(1, 10)][int]$Attempts = 5
    )

    [System.IO.Directory]::CreateDirectory($DestinationRoot) | Out-Null
    foreach ($name in $ExpectedHashes.Keys) {
        $destination = Join-Path $DestinationRoot $name
        $encodedName = [Uri]::EscapeDataString($name)
        $url = "$BaseUrl/$encodedName"
        $lastError = $null
        for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
            try {
                if ([System.IO.File]::Exists($destination)) {
                    Remove-Item -LiteralPath $destination -Force
                }
                Save-AnonymousHttpAsset -Url $url -Destination $destination
                $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
                if ($hash -cne $ExpectedHashes[$name]) {
                    throw "Anonymous GitHub asset hash mismatch: $name"
                }
                $lastError = $null
                break
            }
            catch {
                $lastError = $_
                if ($attempt -lt $Attempts) { Start-Sleep -Seconds 2 }
            }
        }
        if ($null -ne $lastError) { throw $lastError }
    }
}

function Get-AppendedLogText {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][long]$BaselineLength
    )

    $stream = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
    try {
        if ($stream.Length -lt $BaselineLength) { $BaselineLength = 0 }
        [void]$stream.Seek($BaselineLength, [System.IO.SeekOrigin]::Begin)
        $reader = New-Object System.IO.StreamReader($stream)
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    finally {
        $stream.Dispose()
    }
}

function Start-AndValidateLatestLog {
    param(
        [Parameter(Mandatory = $true)][string]$ExePath,
        [Parameter(Mandatory = $true)][string]$LogRoot,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    [System.IO.Directory]::CreateDirectory($LogRoot) | Out-Null
    $baseline = @{}
    foreach ($file in @(Get-ChildItem -LiteralPath $LogRoot -File -Filter 'app-*.log' -ErrorAction SilentlyContinue)) {
        $baseline[$file.FullName] = $file.Length
    }

    $process = Start-Process -FilePath $ExePath -WorkingDirectory (Split-Path -Parent $ExePath) -PassThru
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.HasExited) {
            throw "Packaged app exited during log validation with code $($process.ExitCode)."
        }

        $logs = @(Get-ChildItem -LiteralPath $LogRoot -File -Filter 'app-*.log' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending)
        foreach ($log in $logs) {
            $offset = 0L
            if ($baseline.ContainsKey($log.FullName)) { $offset = [long]$baseline[$log.FullName] }
            $newText = Get-AppendedLogText -Path $log.FullName -BaselineLength $offset
            $marker = $newText.LastIndexOf('Application startup.', [StringComparison]::Ordinal)
            if ($marker -lt 0) { continue }

            $segment = $newText.Substring($marker)
            if ($segment.Contains("`tERROR`t")) {
                throw "Packaged app logged ERROR after the latest startup marker: $($log.FullName)"
            }
            if ($segment.Contains('Loaded takeoffs') -and $segment.Contains('Viewport')) {
                Start-Sleep -Seconds 2
                $process.Refresh()
                if ($process.HasExited) {
                    throw "Packaged app exited after emitting validation signals with code $($process.ExitCode)."
                }
                $finalText = Get-AppendedLogText -Path $log.FullName -BaselineLength $offset
                $finalMarker = $finalText.LastIndexOf('Application startup.', [StringComparison]::Ordinal)
                $finalSegment = $finalText.Substring($finalMarker)
                if ($finalSegment.Contains("`tERROR`t")) {
                    throw "Packaged app logged ERROR after validation signals: $($log.FullName)"
                }
                return $log.FullName
            }
        }
    }

        throw "Timed out after $TimeoutSeconds seconds waiting for Application startup., Loaded takeoffs, and Viewport log signals."
    }
    catch {
        $validationError = $_.Exception.Message
        try {
            $process.Refresh()
            if (-not $process.HasExited) {
                Stop-Process -Id $process.Id -Force
                if (-not $process.WaitForExit(5000)) {
                    throw "PID $($process.Id) did not exit after Stop-Process."
                }
            }
        }
        catch {
            throw "Packaged validation failed: $validationError Failed to stop its process: $($_.Exception.Message)"
        }
        throw $validationError
    }
}

function Get-RemoteTagCommit {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Tag
    )

    $text = Invoke-NativeCapture -FilePath 'git' -Arguments @('-C', $Root, 'ls-remote', 'origin', "refs/tags/$Tag", "refs/tags/$Tag^{}") -Description 'Inspect remote release tag'
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    $lines = @($text -split "`r?`n")
    $peeled = @($lines | Where-Object { $_ -match "refs/tags/$([regex]::Escape($Tag))\^\{\}$" })
    $selected = if ($peeled.Count -gt 0) { $peeled[0] } else { $lines[0] }
    return ($selected -split "`t")[0]
}

function Resolve-GhExecutable {
    param([string]$PreferredPath)

    if (-not [string]::IsNullOrWhiteSpace($PreferredPath)) {
        $resolved = Resolve-FullPath $PreferredPath
        Assert-FileExists -Path $resolved
        return $resolved
    }

    $command = Get-Command 'gh' -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }

    $portableGh = 'C:\tmp\gh-2.96.0\bin\gh.exe'
    if ([System.IO.File]::Exists($portableGh)) { return $portableGh }
    throw 'GitHub CLI was not found in PATH or C:\tmp\gh-2.96.0\bin\gh.exe. Use -GhPath.'
}

function Publish-GitHubRelease {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Repository,
        [Parameter(Mandatory = $true)][string]$Tag,
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][string]$SourceCommit,
        [Parameter(Mandatory = $true)][string]$GhExecutable,
        [Parameter(Mandatory = $true)][string]$AssetExe,
        [Parameter(Mandatory = $true)][string]$AssetTemplate,
        [Parameter(Mandatory = $true)][string]$AssetDownload,
        [Parameter(Mandatory = $true)][string]$Stage,
        [Parameter(Mandatory = $true)][string]$NotesPath,
        [Parameter(Mandatory = $true)][hashtable]$ExpectedHashes
    )
    Assert-FileExists -Path $GhExecutable
    Invoke-Native -FilePath $GhExecutable -Arguments @('auth', 'status', '--hostname', 'github.com') -Description 'Validate GitHub CLI authentication'
    $previousLatestTag = Get-CurrentLatestReleaseTag -GhExecutable $GhExecutable -Repository $Repository
    & git -C $Root check-ref-format "refs/tags/$Tag"
    if ($LASTEXITCODE -ne 0) { throw "Invalid release tag: $Tag" }

    $branch = Invoke-NativeCapture -FilePath 'git' -Arguments @('-C', $Root, 'branch', '--show-current') -Description 'Read current branch'
    if ([string]::IsNullOrWhiteSpace($branch)) { throw 'Detached HEAD cannot be published.' }
    $remoteTagCommit = Get-RemoteTagCommit -Root $Root -Tag $Tag
    if ($null -ne $remoteTagCommit -and $remoteTagCommit -cne $SourceCommit) {
        throw "Remote tag $Tag already points to $remoteTagCommit, not $SourceCommit."
    }

    $localTagExists = $true
    & git -C $Root rev-parse --verify --quiet "refs/tags/$Tag" *> $null
    if ($LASTEXITCODE -ne 0) { $localTagExists = $false }
    if ($localTagExists) {
        $localTagCommit = Invoke-NativeCapture -FilePath 'git' -Arguments @('-C', $Root, 'rev-list', '-n', '1', $Tag) -Description 'Validate local release tag'
        if ($localTagCommit -cne $SourceCommit) {
            throw "Local tag $Tag already points to $localTagCommit, not $SourceCommit."
        }
    }
    elseif ($null -eq $remoteTagCommit) {
        Invoke-Native -FilePath 'git' -Arguments @('-C', $Root, 'tag', '-a', $Tag, $SourceCommit, '-m', $Title) -Description "Create one release tag: $Tag"
    }

    Invoke-Native -FilePath 'git' -Arguments @('-C', $Root, 'push', 'origin', "HEAD:refs/heads/$branch") -Description "Push current branch only: $branch"

    if ($null -eq $remoteTagCommit) {
        Invoke-Native -FilePath 'git' -Arguments @('-C', $Root, 'push', 'origin', "refs/tags/$Tag:refs/tags/$Tag") -Description "Push one release tag only: $Tag"
    }

    $assetPaths = @($AssetExe, $AssetTemplate, $AssetDownload)
    $releaseState = Initialize-GitHubReleaseAssets -GhExecutable $GhExecutable -Repository $Repository `
        -Tag $Tag -Title $Title -NotesPath $NotesPath -AssetPaths $assetPaths `
        -ExpectedNames @($ExpectedHashes.Keys)

    $verifyDirectory = Join-Path $Stage "verify-download-$([Guid]::NewGuid().ToString('N'))"
    [System.IO.Directory]::CreateDirectory($verifyDirectory) | Out-Null
    Invoke-Native -FilePath $GhExecutable -Arguments @('release', 'download', $Tag, '--repo', $Repository, '--dir', $verifyDirectory) -Description 'Re-download draft GitHub Release assets'

    foreach ($name in $ExpectedHashes.Keys) {
        $downloadedPath = Join-Path $verifyDirectory $name
        Assert-FileExists -Path $downloadedPath
        $downloadedHash = (Get-FileHash -LiteralPath $downloadedPath -Algorithm SHA256).Hash
        if ($downloadedHash -cne $ExpectedHashes[$name]) {
            throw "Downloaded GitHub asset hash mismatch: $name"
        }
    }

    if ($releaseState -ceq 'Draft') {
        # Publish temporarily as a prerelease so the old latest remains intact
        # while exact pinned URLs are checked without GitHub authentication.
        Invoke-Native -FilePath $GhExecutable -Arguments @(
            'release', 'edit', $Tag, '--repo', $Repository,
            '--draft=false', '--prerelease'
        ) -Description 'Publish verified Release temporarily as a prerelease'
    }
    elseif ($releaseState -cne 'Prerelease') {
        throw "Unexpected resumable GitHub Release state: $releaseState"
    }

    $anonymousDirectory = Join-Path $Stage "verify-anonymous-pinned-$([Guid]::NewGuid().ToString('N'))"
    try {
        $encodedTag = [Uri]::EscapeDataString($Tag)
        $pinnedBaseUrl = "https://github.com/$Repository/releases/download/$encodedTag"
        Test-AnonymousReleaseAssets -BaseUrl $pinnedBaseUrl `
            -DestinationRoot $anonymousDirectory -ExpectedHashes $ExpectedHashes
    }
    catch {
        $verificationError = $_.Exception.Message
        try {
            Set-GitHubReleaseBackToDraft -GhExecutable $GhExecutable -Repository $Repository -Tag $Tag
        }
        catch {
            throw "Pinned anonymous verification failed: $verificationError Rollback to draft also failed: $($_.Exception.Message)"
        }
        throw "Pinned anonymous verification failed; release returned to draft: $verificationError"
    }

    Invoke-Native -FilePath $GhExecutable -Arguments @(
        'release', 'edit', $Tag, '--repo', $Repository,
        '--prerelease=false', '--latest'
    ) -Description 'Mark anonymously verified Release as latest'

    $latestDirectory = Join-Path $Stage "verify-anonymous-latest-$([Guid]::NewGuid().ToString('N'))"
    $latestBaseUrl = "https://github.com/$Repository/releases/latest/download"
    try {
        Test-AnonymousReleaseAssets -BaseUrl $latestBaseUrl `
            -DestinationRoot $latestDirectory -ExpectedHashes $ExpectedHashes -Attempts 10
        Assert-FinalGitHubReleaseState -GhExecutable $GhExecutable -Repository $Repository -Tag $Tag
    }
    catch {
        $verificationError = $_.Exception.Message
        try {
            Restore-PreviousLatestGitHubRelease -GhExecutable $GhExecutable -Repository $Repository `
                -CurrentTag $Tag -PreviousLatestTag $previousLatestTag
        }
        catch {
            throw "Latest anonymous verification failed: $verificationError Previous latest rollback also failed: $($_.Exception.Message)"
        }
        throw "Latest anonymous verification failed; previous latest restored: $verificationError"
    }
}

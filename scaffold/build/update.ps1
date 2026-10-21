# Scaffold updater for Windows PowerShell 5.1. Managed files move atomically,
# user-owned files are preserved, and a failed smoke test restores the backup
# journaled under build/temp/update/<stamp>.

[CmdletBinding()]
Param(
    [string]$Source,
    [string]$Sha,
    [string]$Target = $(if ($env:PLUTONIUM_TARGET) { $env:PLUTONIUM_TARGET } else { (Get-Location).Path }),
    [switch]$NoSmoke,
    [switch]$ForceSmoke
)

$ErrorActionPreference = "Stop"
$ConfirmPreference = "None"
$DefaultSource = "https://github.com/Kiryuumaru/PlutoniumBuild.git"

function Stop-Update {
    param([string]$Message)
    throw $Message
}

function Get-LockValue {
    param([string]$Path, [string]$Key)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }
    $prefix = $Key + "="
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith("#", [System.StringComparison]::Ordinal)) { continue }
        if ($trimmed.StartsWith($prefix, [System.StringComparison]::Ordinal)) {
            return $trimmed.Substring($prefix.Length).Trim()
        }
    }
    return $null
}

function Get-InstalledSourcePath {
    param([string]$Root, [string]$Relative)
    if ($Relative.StartsWith("build/", [System.StringComparison]::Ordinal)) {
        return Join-Path (Join-Path $Root "scaffold") ("build/" + $Relative.Substring("build/".Length))
    }
    if ($Relative -in @("build.sh", "build.ps1", "build.cmd")) {
        return Join-Path (Join-Path $Root "scaffold/root") $Relative
    }
    return $null
}

function Test-SafeManifestPath {
    param([string]$Relative)
    if ($Relative -in @("build.sh", "build.ps1", "build.cmd")) {
        return $true
    }
    if (-not $Relative.StartsWith("build/", [System.StringComparison]::Ordinal)) {
        return $false
    }
    $rest = $Relative.Substring("build/".Length)
    if ([string]::IsNullOrWhiteSpace($rest)) {
        return $false
    }
    foreach ($segment in $rest.Split("/")) {
        if ([string]::IsNullOrEmpty($segment) -or $segment -eq "." -or $segment -eq "..") {
            return $false
        }
    }
    if ($rest.Contains("\")) {
        return $false
    }
    return $true
}

function Get-InstalledPath {
    param([string]$Relative)
    if ($Relative.StartsWith("build/", [System.StringComparison]::Ordinal)) {
        return Join-Path $BuildDirectory ($Relative.Substring("build/".Length).Replace("/", [System.IO.Path]::DirectorySeparatorChar))
    }
    if ($Relative -in @("build.sh", "build.ps1", "build.cmd")) {
        return Join-Path $Target $Relative
    }
    return $null
}

function Assert-SourceUrl {
    param([string]$Value, [string]$Label)
    if ([string]::IsNullOrWhiteSpace($Value) -or
        $Value -match "://[^/]*@" -or
        $Value.Contains("?") -or
        $Value.Contains("#") -or
        $Value -match "[\s\x00-\x1F]") {
        Stop-Update "build/update.ps1: $Label must not contain credentials (userinfo, query, or fragment)."
    }
}

function Test-LockHasPath {
    param([string]$LockPath, [string]$Relative)
    foreach ($line in [System.IO.File]::ReadAllLines($LockPath)) {
        if ($line -match "^\s*(managed|preserve)\s+(\S+)(\s+.*)?$" -and $Matches[2] -eq $Relative) {
            return $true
        }
    }
    return $false
}

function Test-LockHasManaged {
    param([string]$LockPath, [string]$Relative)
    foreach ($line in [System.IO.File]::ReadAllLines($LockPath)) {
        if ($line -match "^\s*managed\s+(\S+)\s+[0-9a-fA-F]{64}\s*$" -and $Matches[1] -eq $Relative) {
            return $true
        }
    }
    return $false
}

function Test-PreservedInLock {
    param([string]$LockPath, [string]$Relative)
    foreach ($line in [System.IO.File]::ReadAllLines($LockPath)) {
        if ($line -match "^\s*preserve\s+(\S+)\s*$" -and $Matches[1] -eq $Relative) {
            return $true
        }
    }
    return $false
}

function Get-PackagePin {
    param([string]$ProjectPath)
    if (-not (Test-Path -LiteralPath $ProjectPath -PathType Leaf)) {
        return $null
    }
    [xml]$project = Get-Content -LiteralPath $ProjectPath -Raw
    $reference = $project.SelectSingleNode("//*[local-name()='PackageReference' and @Include='PlutoniumBuild']")
    if ($null -eq $reference) {
        return $null
    }
    if ($reference.HasAttribute("Version")) {
        return $reference.GetAttribute("Version").Trim()
    }
    $versionNode = $reference.SelectSingleNode("*[local-name()='Version']")
    if ($null -ne $versionNode) {
        return $versionNode.InnerText.Trim()
    }
    return $null
}

function Assert-VersionInvariant {
    param([string]$Root, [string]$LockPath)
    if ((Get-LockValue -Path $LockPath -Key "format") -ne "1") {
        Stop-Update "build/update.ps1: source lock has an unsupported format."
    }
    $packageVersion = Get-LockValue -Path $LockPath -Key "package_version"
    if ([string]::IsNullOrWhiteSpace($packageVersion)) {
        Stop-Update "build/update.ps1: source lock has no package_version."
    }
    $propsPath = Join-Path $Root "src/Directory.Build.props"
    if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) {
        Stop-Update "build/update.ps1: source src/Directory.Build.props is missing."
    }
    [xml]$props = Get-Content -LiteralPath $propsPath -Raw
    $versionNode = $props.SelectSingleNode("//*[local-name()='Version']")
    if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
        Stop-Update "build/update.ps1: source src/Directory.Build.props has no package version."
    }
    $propsVersion = $versionNode.InnerText.Trim()
    if ($propsVersion -ne $packageVersion) {
        Stop-Update "build/update.ps1: source src/Directory.Build.props version $propsVersion does not match lock package_version $packageVersion."
    }
    $pin = Get-PackagePin -ProjectPath (Join-Path $Root "scaffold/build/build.csproj")
    if ([string]::IsNullOrWhiteSpace($pin)) {
        Stop-Update "build/update.ps1: source build.csproj has no PlutoniumBuild package pin."
    }
    if ($pin -ne $packageVersion) {
        Stop-Update "build/update.ps1: source package pin $pin does not match lock package_version $packageVersion."
    }
}

function Assert-SourceContract {
    param([string]$Root, [string]$LockPath)
    $buildRoot = Join-Path $Root "scaffold/build"
    foreach ($item in Get-ChildItem -LiteralPath $buildRoot -Recurse -File -Force) {
        $relative = "build/" + $item.FullName.Substring($buildRoot.Length + 1).Replace("\", "/")
        if ($relative -eq "build/plutonium.lock") { continue }
        if (-not (Test-LockHasPath -LockPath $LockPath -Relative $relative)) {
            Stop-Update "build/update.ps1: source scaffold contains an unlisted file: $relative"
        }
    }

    $rootRoot = Join-Path $Root "scaffold/root"
    foreach ($item in Get-ChildItem -LiteralPath $rootRoot -File -Force) {
        if (-not (Test-LockHasPath -LockPath $LockPath -Relative $item.Name)) {
            Stop-Update "build/update.ps1: source root contains an unlisted wrapper: $($item.Name)"
        }
    }

    foreach ($line in [System.IO.File]::ReadAllLines($LockPath)) {
        if ($line -notmatch "^\s*preserve\s+(\S+)\s*$") { continue }
        $relative = $Matches[1]
        if (-not (Test-SafeManifestPath -Relative $relative)) {
            Stop-Update "build/update.ps1: manifest contains an unsafe preserve path: $relative"
        }
        $sourceFile = Get-InstalledSourcePath -Root $Root -Relative $relative
        if ([string]::IsNullOrWhiteSpace($sourceFile) -or -not (Test-Path -LiteralPath $sourceFile -PathType Leaf)) {
            Stop-Update "build/update.ps1: source scaffold is missing preserved file $relative."
        }
    }
}

function Set-ManagedHash {
    param([string]$LockPath, [string]$Relative, [string]$Hash)
    $rewritten = New-Object System.Collections.Generic.List[string]
    $found = $false
    foreach ($line in [System.IO.File]::ReadAllLines($LockPath)) {
        if ($line -match "^\s*managed\s+(\S+)\s+[0-9a-fA-F]{64}\s*$" -and $Matches[1] -eq $Relative) {
            $rewritten.Add("managed $Relative $Hash")
            $found = $true
        }
        else {
            $rewritten.Add($line)
        }
    }
    if (-not $found) {
        throw "build/update.ps1: lock has no managed entry for $Relative."
    }
    [System.IO.File]::WriteAllLines($LockPath, $rewritten)
}

if ($Sha -notmatch "^[0-9a-fA-F]{40}$") {
    Stop-Update "build/update.ps1: --Sha must be a full 40-character commit SHA; resolve the target first (for example with git ls-remote)."
}
$Sha = $Sha.ToLowerInvariant()

if (-not (Test-Path -LiteralPath $Target -PathType Container)) {
    Stop-Update "build/update.ps1: target directory not found: $Target"
}
$Target = (Resolve-Path -LiteralPath $Target).Path
$BuildDirectory = Join-Path $Target "build"
$LockFile = Join-Path $BuildDirectory "plutonium.lock"
if (-not (Test-Path -LiteralPath $LockFile -PathType Leaf)) {
    Stop-Update "build/update.ps1: $BuildDirectory\plutonium.lock not found; run init first."
}

if ([string]::IsNullOrWhiteSpace($Source)) {
    $Source = Get-LockValue -Path $LockFile -Key "source"
}
if ([string]::IsNullOrWhiteSpace($Source) -or $Source -eq "github.com/Kiryuumaru/PlutoniumBuild") {
    $Source = $DefaultSource
}
Assert-SourceUrl -Value $Source -Label "--Source"

$currentSource = Get-LockValue -Path $LockFile -Key "source"
$currentSourceUrl = Get-LockValue -Path $LockFile -Key "source_url"
if ([string]::IsNullOrWhiteSpace($currentSource)) {
    $currentSource = "github.com/Kiryuumaru/PlutoniumBuild"
}
Assert-SourceUrl -Value $currentSource -Label "installed lock source"
if (-not [string]::IsNullOrWhiteSpace($currentSourceUrl)) {
    Assert-SourceUrl -Value $currentSourceUrl -Label "installed lock source_url"
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Stop-Update "build/update.ps1: git is required to fetch the scaffold source."
}

$fetchDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("plutonium-update-" + [guid]::NewGuid().ToString("N"))
$stage = $null
$backup = $null
$backupFiles = $null
$actions = New-Object System.Collections.Generic.List[object]
$applied = New-Object System.Collections.Generic.List[string]
$backedUp = New-Object 'System.Collections.Generic.HashSet[string]'
$updateStarted = $false
$updateCommitted = $false

function Restore-Backup {
    if ($null -eq $backupFiles) {
        return
    }
    $hadBackup = @{}
    foreach ($action in $actions) {
        $hadBackup[$action.Relative] = $action.HadBackup
    }
    foreach ($relative in $applied) {
        if ($hadBackup.ContainsKey($relative) -and $hadBackup[$relative]) {
            continue
        }
        $destination = Get-InstalledPath -Relative $relative
        if (-not [string]::IsNullOrWhiteSpace($destination)) {
            Remove-Item -LiteralPath $destination -Force -ErrorAction SilentlyContinue
        }
    }
    foreach ($action in $actions) {
        $destination = Get-InstalledPath -Relative $action.Relative
        $backupPath = Join-Path $backupFiles ($action.Relative.Replace("/", [System.IO.Path]::DirectorySeparatorChar))
        if ($action.HadBackup -and (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            Copy-Item -LiteralPath $backupPath -Destination $destination -Force
        }
        elseif (-not $action.HadBackup) {
            Remove-Item -LiteralPath $destination -Force -ErrorAction SilentlyContinue
        }
    }
}

try {
    New-Item -ItemType Directory -Path $fetchDirectory -Force | Out-Null
    & git -C $fetchDirectory init -q
    if ($LASTEXITCODE -ne 0) { Stop-Update "build/update.ps1: git init failed." }
    & git -C $fetchDirectory remote add origin $Source
    if ($LASTEXITCODE -ne 0) { Stop-Update "build/update.ps1: git remote add failed." }

    $env:GIT_TERMINAL_PROMPT = "0"
    $fetchOutput = & git -C $fetchDirectory fetch --depth 1 --no-tags origin $Sha 2>&1
    if ($LASTEXITCODE -ne 0) {
        Stop-Update "build/update.ps1: could not fetch commit $Sha from $Source.`n$($fetchOutput -join [Environment]::NewLine)"
    }
    & git -C $fetchDirectory checkout -q --detach FETCH_HEAD
    if ($LASTEXITCODE -ne 0) { Stop-Update "build/update.ps1: could not check out commit $Sha." }

    $headSha = (& git -C $fetchDirectory rev-parse HEAD).Trim()
    if ($headSha -ne $Sha) {
        Stop-Update "build/update.ps1: fetched $headSha but expected $Sha."
    }

    $sourceBuild = Join-Path $fetchDirectory "scaffold/build"
    $sourceRoot = Join-Path $fetchDirectory "scaffold/root"
    $sourceLock = Join-Path $sourceBuild "plutonium.lock"
    if (-not (Test-Path -LiteralPath $sourceLock -PathType Leaf)) {
        Stop-Update "build/update.ps1: commit $Sha does not contain the scaffold payload."
    }

    foreach ($item in Get-ChildItem -LiteralPath (Join-Path $fetchDirectory "scaffold") -Recurse -Force) {
        if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
            Stop-Update "build/update.ps1: scaffold payload contains a symbolic link; refusing to install it."
        }
    }

    Assert-VersionInvariant -Root $fetchDirectory -LockPath $sourceLock
    Assert-SourceContract -Root $fetchDirectory -LockPath $sourceLock

    foreach ($line in [System.IO.File]::ReadAllLines($sourceLock)) {
        if ($line -notmatch "^\s*managed\s+(\S+)\s+([0-9a-fA-F]{64})\s*$") { continue }
        $relative = $Matches[1]
        $expected = $Matches[2].ToLowerInvariant()
        if (-not (Test-SafeManifestPath -Relative $relative)) {
            Stop-Update "build/update.ps1: manifest contains an unsafe path: $relative"
        }
        $sourceFile = Get-InstalledSourcePath -Root $fetchDirectory -Relative $relative
        if ([string]::IsNullOrWhiteSpace($sourceFile) -or -not (Test-Path -LiteralPath $sourceFile -PathType Leaf)) {
            Stop-Update "build/update.ps1: source scaffold is missing $relative."
        }
        $actual = (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $expected) {
            Stop-Update "build/update.ps1: manifest hash mismatch for $relative; refusing the update."
        }
    }

    $newPackageVersion = Get-LockValue -Path $sourceLock -Key "package_version"
    if ([string]::IsNullOrWhiteSpace($newPackageVersion)) {
        Stop-Update "build/update.ps1: source lock has no package_version."
    }

    $currentCommit = Get-LockValue -Path $LockFile -Key "scaffold_commit"
    if ($currentCommit -eq $Sha) {
        $upToDate = $true
        foreach ($line in [System.IO.File]::ReadAllLines($LockFile)) {
            if ($line -notmatch "^\s*managed\s+(\S+)\s+([0-9a-fA-F]{64})\s*$") { continue }
            $relative = $Matches[1]
            $expected = $Matches[2].ToLowerInvariant()
            if (Test-PreservedInLock -LockPath $LockFile -Relative $relative) { continue }
            $destination = Get-InstalledPath -Relative $relative
            if (-not (Test-Path -LiteralPath $destination -PathType Leaf)) {
                $upToDate = $false
                break
            }
            $actual = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actual -ne $expected) {
                $upToDate = $false
                break
            }
        }
        if ($upToDate) {
            Write-Host "PlutoniumBuild: already at $Sha; nothing to update."
            exit 0
        }
    }

    $stamp = [DateTime]::UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'")
    $backup = Join-Path $BuildDirectory ("temp/update/" + $stamp)
    if (Test-Path -LiteralPath $backup) {
        $backup = $backup + "-" + $PID
    }
    $backupFiles = Join-Path $backup "files"
    New-Item -ItemType Directory -Path $backupFiles -Force | Out-Null

    function Add-Backup {
        param([string]$Relative)
        if (-not $backedUp.Add($Relative)) {
            return
        }
        $sourceFile = Get-InstalledPath -Relative $Relative
        $item = Get-Item -LiteralPath $sourceFile -Force -ErrorAction SilentlyContinue
        if ($null -ne $item -and $item.PSIsContainer) {
            throw "build/update.ps1: installed path is not a regular file: $Relative"
        }
        $hadBackup = $null -ne $item
        if ($hadBackup) {
            $backupPath = Join-Path $backupFiles ($Relative.Replace("/", [System.IO.Path]::DirectorySeparatorChar))
            New-Item -ItemType Directory -Path (Split-Path -Parent $backupPath) -Force | Out-Null
            Copy-Item -LiteralPath $sourceFile -Destination $backupPath -Force
        }
        $actions.Add([pscustomobject]@{ Relative = $Relative; HadBackup = $hadBackup })
    }

    foreach ($backupLock in @($LockFile, $sourceLock)) {
        foreach ($line in [System.IO.File]::ReadAllLines($backupLock)) {
            if ($line -match "^\s*(managed|preserve)\s+(\S+)") {
                Add-Backup -Relative $Matches[2]
            }
        }
    }
    Add-Backup -Relative "build/plutonium.lock"

    $stage = Join-Path $BuildDirectory ("temp/update-stage-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $stage -Force | Out-Null

    foreach ($line in [System.IO.File]::ReadAllLines($sourceLock)) {
        if ($line -match "^\s*managed\s+(\S+)\s+([0-9a-fA-F]{64})\s*$") {
            $relative = $Matches[1]
            if (-not (Test-SafeManifestPath -Relative $relative)) {
                Stop-Update "build/update.ps1: manifest contains an unsafe path: $relative"
            }
            $sourceFile = Get-InstalledSourcePath -Root $fetchDirectory -Relative $relative
            $destination = Get-InstalledPath -Relative $relative
            if (Test-PreservedInLock -LockPath $sourceLock -Relative $relative) {
                if (Test-Path -LiteralPath $destination -PathType Container) {
                    Stop-Update "build/update.ps1: preserved path is not a regular file: $relative"
                }
                if (Test-Path -LiteralPath $destination) {
                    continue
                }
            }
            $stagedPath = Join-Path $stage ($relative.Replace("/", [System.IO.Path]::DirectorySeparatorChar))
            New-Item -ItemType Directory -Path (Split-Path -Parent $stagedPath) -Force | Out-Null
            Copy-Item -LiteralPath $sourceFile -Destination $stagedPath -Force
        }
        elseif ($line -match "^\s*preserve\s+(\S+)") {
            $relative = $Matches[1]
            if (-not (Test-SafeManifestPath -Relative $relative)) {
                Stop-Update "build/update.ps1: manifest contains an unsafe path: $relative"
            }
            $destination = Get-InstalledPath -Relative $relative
            if (Test-Path -LiteralPath $destination -PathType Container) {
                Stop-Update "build/update.ps1: preserved path is not a regular file: $relative"
            }
            if (Test-Path -LiteralPath $destination) {
                continue
            }
            $sourceFile = Get-InstalledSourcePath -Root $fetchDirectory -Relative $relative
            if (Test-Path -LiteralPath $sourceFile -PathType Leaf) {
                $stagedPath = Join-Path $stage ($relative.Replace("/", [System.IO.Path]::DirectorySeparatorChar))
                New-Item -ItemType Directory -Path (Split-Path -Parent $stagedPath) -Force | Out-Null
                Copy-Item -LiteralPath $sourceFile -Destination $stagedPath -Force
            }
        }
    }

    $stagedLock = Join-Path $stage "build/plutonium.lock"
    New-Item -ItemType Directory -Path (Split-Path -Parent $stagedLock) -Force | Out-Null
    Copy-Item -LiteralPath $sourceLock -Destination $stagedLock -Force

    $rewritten = New-Object System.Collections.Generic.List[string]
    foreach ($line in [System.IO.File]::ReadAllLines($stagedLock)) {
        if ($line.StartsWith("source=", [System.StringComparison]::Ordinal)) {
            $rewritten.Add("source=$currentSource")
        }
        elseif ($line.StartsWith("source_url=", [System.StringComparison]::Ordinal)) {
            if (-not [string]::IsNullOrWhiteSpace($currentSourceUrl)) {
                $rewritten.Add("source_url=$currentSourceUrl")
            }
        }
        elseif ($line.StartsWith("scaffold_commit=", [System.StringComparison]::Ordinal)) {
            $rewritten.Add("scaffold_commit=$Sha")
        }
        else {
            $rewritten.Add($line)
        }
    }
    [System.IO.File]::WriteAllLines($stagedLock, $rewritten)

    foreach ($line in [System.IO.File]::ReadAllLines($sourceLock)) {
        if ($line -notmatch "^\s*preserve\s+(\S+)\s*$") { continue }
        $preserveRelative = $Matches[1]
        if (-not (Test-LockHasManaged -LockPath $sourceLock -Relative $preserveRelative)) {
            continue
        }
        $stagedFile = Join-Path $stage ($preserveRelative.Replace("/", [System.IO.Path]::DirectorySeparatorChar))
        $installedFile = Get-InstalledPath -Relative $preserveRelative
        $preservedHash = $null
        if (Test-Path -LiteralPath $stagedFile -PathType Leaf) {
            $preservedHash = (Get-FileHash -LiteralPath $stagedFile -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        elseif (Test-Path -LiteralPath $installedFile -PathType Leaf) {
            $preservedHash = (Get-FileHash -LiteralPath $installedFile -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        if ($null -ne $preservedHash) {
            Set-ManagedHash -LockPath $stagedLock -Relative $preserveRelative -Hash $preservedHash
        }
    }

    $stagedProject = Join-Path $stage "build/build.csproj"
    if (Test-Path -LiteralPath $stagedProject -PathType Leaf) {
        [xml]$projectXml = Get-Content -LiteralPath $stagedProject -Raw
        $reference = $projectXml.SelectSingleNode("//*[local-name()='PackageReference' and @Include='PlutoniumBuild']")
        if ($null -eq $reference) {
            Stop-Update "build/update.ps1: staged build.csproj has no PlutoniumBuild PackageReference."
        }
        if ($reference.HasAttribute("Version")) {
            $reference.SetAttribute("Version", $newPackageVersion)
        }
        else {
            $versionElement = $reference.SelectSingleNode("*[local-name()='Version']")
            if ($null -eq $versionElement) {
                $versionElement = $projectXml.CreateElement("Version")
                [void]$reference.AppendChild($versionElement)
            }
            $versionElement.InnerText = $newPackageVersion
        }
        $projectXml.Save($stagedProject)
        $projectHash = (Get-FileHash -LiteralPath $stagedProject -Algorithm SHA256).Hash.ToLowerInvariant()
        Set-ManagedHash -LockPath $stagedLock -Relative "build/build.csproj" -Hash $projectHash
    }

    foreach ($line in [System.IO.File]::ReadAllLines($stagedLock)) {
        if ($line -notmatch "^\s*managed\s+(\S+)\s+([0-9a-fA-F]{64})\s*$") { continue }
        $relative = $Matches[1]
        $expected = $Matches[2].ToLowerInvariant()
        $stagedPath = Join-Path $stage ($relative.Replace("/", [System.IO.Path]::DirectorySeparatorChar))
        $installedPath = Get-InstalledPath -Relative $relative
        if (Test-Path -LiteralPath $stagedPath -PathType Leaf) {
            $actual = (Get-FileHash -LiteralPath $stagedPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        elseif (Test-Path -LiteralPath $installedPath -PathType Leaf) {
            $actual = (Get-FileHash -LiteralPath $installedPath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        else {
            throw "build/update.ps1: staged lock names a file that is neither staged nor installed: $relative"
        }
        if ($actual -ne $expected) {
            throw "build/update.ps1: staged hash for $relative does not match the new lock."
        }
    }
    foreach ($stagedFile in (Get-ChildItem -LiteralPath $stage -Recurse -File -Force)) {
        $relative = $stagedFile.FullName.Substring($stage.Length + 1).Replace([System.IO.Path]::DirectorySeparatorChar, "/")
        if ($relative -eq "build/plutonium.lock") { continue }
        if (-not (Test-LockHasPath -LockPath $stagedLock -Relative $relative)) {
            throw "build/update.ps1: staged file is not listed by the new lock: $relative"
        }
    }

    $applyError = $null
    $updateStarted = $true
    try {
        foreach ($stagedFile in (Get-ChildItem -LiteralPath $stage -Recurse -File -Force)) {
            $relative = $stagedFile.FullName.Substring($stage.Length + 1).Replace([System.IO.Path]::DirectorySeparatorChar, "/")
            $destination = Get-InstalledPath -Relative $relative
            if ([string]::IsNullOrWhiteSpace($destination)) {
                throw "build/update.ps1: staged file outside the scaffold layout: $relative"
            }
            if (Test-Path -LiteralPath $destination -PathType Container) {
                throw "build/update.ps1: destination is not a regular file: $relative"
            }
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            Move-Item -LiteralPath $stagedFile.FullName -Destination $destination -Force
            $applied.Add($relative)
        }
    }
    catch {
        $applyError = $_
    }

    if ($null -ne $applyError) {
        throw $applyError
    }

    $smokeFailed = $false
    if (-not $NoSmoke) {
        $project = Join-Path $BuildDirectory "build.csproj"
        $assets = Join-Path $BuildDirectory "obj/project.assets.json"
        if (Test-Path -LiteralPath $project -PathType Leaf) {
            if ($ForceSmoke -or (Test-Path -LiteralPath $assets -PathType Leaf)) {
                try {
                    . (Join-Path $BuildDirectory "bootstrap.ps1")
                    $toolchain = Resolve-PlutoniumDotNet `
                        -GlobalJson (Join-Path $BuildDirectory "global.json") `
                        -CacheDirectory (Join-Path $BuildDirectory "temp") `
                        -LockFile $LockFile
                    $buildArguments = @($project, "/nodeReuse:false", "/p:UseSharedCompilation=false", "-nologo", "-clp:NoSummary", "--verbosity", "quiet")
                    & $toolchain.Exe build @buildArguments
                    if ($LASTEXITCODE -ne 0) { $smokeFailed = $true }
                }
                catch {
                    $smokeFailed = $true
                }
            }
            elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
                & dotnet msbuild $project -getProperty:TargetFramework -nologo | Out-Null
                if ($LASTEXITCODE -ne 0) { $smokeFailed = $true }
            }
            else {
                Write-Warning "PlutoniumBuild: smoke test skipped (no restored assets and no dotnet on PATH)."
            }
        }
    }

    if ($smokeFailed) {
        Stop-Update "PlutoniumBuild: update smoke test failed; the previous scaffold is being restored."
    }

    @(
        "updated_at=$stamp",
        "from_commit=$currentCommit",
        "to_commit=$Sha",
        "source=$Source",
        "package_version=$newPackageVersion",
        "status=ok"
    ) | Set-Content -LiteralPath (Join-Path $backup "journal.txt") -Encoding UTF8
    $updateCommitted = $true

    Write-Host "PlutoniumBuild: updated scaffold from $currentCommit to $Sha."
    Write-Host "PlutoniumBuild: previous files are journaled under build/temp/update/$stamp."
}
finally {
    if ($updateStarted -and -not $updateCommitted) {
        try {
            Restore-Backup
            Write-Warning "PlutoniumBuild: update rolled back; the previous scaffold is restored."
        }
        catch {
            Write-Warning "PlutoniumBuild: update rollback failed: $($_.Exception.Message)"
        }
    }
    if ($null -ne $stage -and (Test-Path -LiteralPath $stage)) {
        Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $fetchDirectory) {
        Remove-Item -LiteralPath $fetchDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

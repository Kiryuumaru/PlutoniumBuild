# Bootstrap scaffold installer for Windows PowerShell 5.1. It fetches a pinned
# source commit, verifies the managed-file manifest, refuses to overwrite an
# existing scaffold, and installs the payload atomically.

[CmdletBinding()]
Param(
    [string]$Source = $(if ($env:PLUTONIUM_SOURCE) { $env:PLUTONIUM_SOURCE } else { "https://github.com/Kiryuumaru/PlutoniumBuild.git" }),
    [string]$Sha = $env:PLUTONIUM_SHA,
    [string]$Target = $(if ($env:PLUTONIUM_TARGET) { $env:PLUTONIUM_TARGET } else { (Get-Location).Path })
)

$ErrorActionPreference = "Stop"
$ConfirmPreference = "None"
$DefaultSource = "https://github.com/Kiryuumaru/PlutoniumBuild.git"

function Stop-Init {
    param([string]$Message)
    Write-Error $Message
    exit 1
}

function Invoke-NativeCommand {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $FilePath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    [pscustomobject]@{
        Output = @($output)
        ExitCode = $exitCode
    }
}

function Test-ReparsePoint {
    param([string]$Path)
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return $false
    }
    return [bool]($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)
}

function Get-DestinationConflict {
    param([string]$Path)
    if (Test-ReparsePoint -Path $Path) {
        return "is a reparse point"
    }
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -ne $item) {
        return "already exists"
    }
    return $null
}

function Assert-SourceUrl {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value) -or
        $Value -match "://[^/]*@" -or
        $Value.Contains("?") -or
        $Value.Contains("#") -or
        $Value -match "[\s\x00-\x1F]") {
        Stop-Init "init.ps1: --Source must not contain credentials (userinfo, query, or fragment)."
    }
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

function ConvertTo-LfLineEndings {
    param([string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $changed = $false
    $normalized = New-Object System.Collections.Generic.List[byte]
    foreach ($value in $bytes) {
        if ($value -eq 13) {
            $changed = $true
            continue
        }
        $normalized.Add($value)
    }
    if ($changed) {
        [System.IO.File]::WriteAllBytes($Path, $normalized.ToArray())
    }
}

function Write-LfLines {
    param([string]$Path, [System.Collections.Generic.IEnumerable[string]]$Lines)
    $text = [string]::Join("`n", [string[]]$Lines)
    if ($text.Length -gt 0) {
        $text += "`n"
    }
    $encoding = New-Object System.Text.UTF8Encoding -ArgumentList $false
    [System.IO.File]::WriteAllText($Path, $text, $encoding)
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

function Test-LockHasPath {
    param([string]$LockPath, [string]$Relative)
    foreach ($line in [System.IO.File]::ReadAllLines($LockPath)) {
        if ($line -match "^\s*(managed|preserve)\s+(\S+)(\s+.*)?$" -and $Matches[2] -eq $Relative) {
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
        Stop-Init "init.ps1: source lock has an unsupported format."
    }
    $packageVersion = Get-LockValue -Path $LockPath -Key "package_version"
    if ([string]::IsNullOrWhiteSpace($packageVersion)) {
        Stop-Init "init.ps1: source lock has no package_version."
    }
    $propsPath = Join-Path $Root "src/Directory.Build.props"
    if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) {
        Stop-Init "init.ps1: source src/Directory.Build.props is missing."
    }
    [xml]$props = Get-Content -LiteralPath $propsPath -Raw
    $versionNode = $props.SelectSingleNode("//*[local-name()='Version']")
    if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
        Stop-Init "init.ps1: source src/Directory.Build.props has no package version."
    }
    $propsVersion = $versionNode.InnerText.Trim()
    if ($propsVersion -ne $packageVersion) {
        Stop-Init "init.ps1: source src/Directory.Build.props version $propsVersion does not match lock package_version $packageVersion."
    }
    $pin = Get-PackagePin -ProjectPath (Join-Path $Root "scaffold/build/build.csproj")
    if ([string]::IsNullOrWhiteSpace($pin)) {
        Stop-Init "init.ps1: source build.csproj has no PlutoniumBuild package pin."
    }
    if ($pin -ne $packageVersion) {
        Stop-Init "init.ps1: source package pin $pin does not match lock package_version $packageVersion."
    }
}

function Assert-SourceContract {
    param([string]$Root, [string]$LockPath)
    $buildRoot = Join-Path $Root "scaffold/build"
    foreach ($item in Get-ChildItem -LiteralPath $buildRoot -Recurse -File -Force) {
        $relative = "build/" + $item.FullName.Substring($buildRoot.Length + 1).Replace("\", "/")
        if ($relative -eq "build/plutonium.lock") { continue }
        if (-not (Test-LockHasPath -LockPath $LockPath -Relative $relative)) {
            Stop-Init "init.ps1: source scaffold contains an unlisted file: $relative"
        }
    }

    $rootRoot = Join-Path $Root "scaffold/root"
    foreach ($item in Get-ChildItem -LiteralPath $rootRoot -File -Force) {
        if (-not (Test-LockHasPath -LockPath $LockPath -Relative $item.Name)) {
            Stop-Init "init.ps1: source root contains an unlisted wrapper: $($item.Name)"
        }
    }

    foreach ($line in [System.IO.File]::ReadAllLines($LockPath)) {
        if ($line -notmatch "^\s*preserve\s+(\S+)\s*$") { continue }
        $relative = $Matches[1]
        if (-not (Test-SafeManifestPath -Relative $relative)) {
            Stop-Init "init.ps1: manifest contains an unsafe preserve path: $relative"
        }
        $payloadFile = Get-InstalledSourcePath -Root $Root -Relative $relative
        if ([string]::IsNullOrWhiteSpace($payloadFile) -or -not (Test-Path -LiteralPath $payloadFile -PathType Leaf)) {
            Stop-Init "init.ps1: source scaffold is missing preserved file $relative."
        }
    }
}

Assert-SourceUrl -Value $Source

if ($Sha -notmatch "^[0-9a-fA-F]{40}$") {
    Stop-Init "init.ps1: --Sha must be the full 40-character source commit SHA."
}
$Sha = $Sha.ToLowerInvariant()

if (-not (Test-Path -LiteralPath $Target -PathType Container)) {
    Stop-Init "init.ps1: target directory not found: $Target"
}
$Target = (Resolve-Path -LiteralPath $Target).Path

$buildConflict = Get-DestinationConflict -Path (Join-Path $Target "build")
if ($null -ne $buildConflict) {
    Stop-Init "PlutoniumBuild init: refusing to install: build/ $buildConflict."
}
foreach ($wrapper in @("build.sh", "build.ps1", "build.cmd")) {
    $wrapperConflict = Get-DestinationConflict -Path (Join-Path $Target $wrapper)
    if ($null -ne $wrapperConflict) {
        Stop-Init "PlutoniumBuild init: refusing to install: root $wrapper $wrapperConflict."
    }
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Stop-Init "init.ps1: git is required to fetch the scaffold source."
}

$fetchDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("plutonium-init-" + [guid]::NewGuid().ToString("N"))
$stage = $null
New-Item -ItemType Directory -Path $fetchDirectory -Force | Out-Null

try {
    $result = Invoke-NativeCommand -FilePath git -Arguments @("-C", $fetchDirectory, "init", "-q")
    if ($result.ExitCode -ne 0) { Stop-Init "init.ps1: git init failed." }
    # Git for Windows defaults to core.autocrlf=true; the lock records LF hashes.
    $result = Invoke-NativeCommand -FilePath git -Arguments @("-C", $fetchDirectory, "config", "core.autocrlf", "false")
    if ($result.ExitCode -ne 0) { Stop-Init "init.ps1: git config failed." }
    $result = Invoke-NativeCommand -FilePath git -Arguments @("-C", $fetchDirectory, "config", "core.eol", "lf")
    if ($result.ExitCode -ne 0) { Stop-Init "init.ps1: git config failed." }
    $result = Invoke-NativeCommand -FilePath git -Arguments @("-C", $fetchDirectory, "remote", "add", "origin", $Source)
    if ($result.ExitCode -ne 0) { Stop-Init "init.ps1: git remote add failed." }

    $env:GIT_TERMINAL_PROMPT = "0"
    $result = Invoke-NativeCommand -FilePath git -Arguments @("-C", $fetchDirectory, "fetch", "--depth", "1", "--no-tags", "origin", $Sha)
    if ($result.ExitCode -ne 0) {
        Stop-Init "init.ps1: could not fetch commit $Sha from $Source.`n$($result.Output -join [Environment]::NewLine)"
    }

    $result = Invoke-NativeCommand -FilePath git -Arguments @("-C", $fetchDirectory, "checkout", "-q", "--detach", "FETCH_HEAD")
    if ($result.ExitCode -ne 0) { Stop-Init "init.ps1: could not check out commit $Sha." }

    $result = Invoke-NativeCommand -FilePath git -Arguments @("-C", $fetchDirectory, "rev-parse", "HEAD")
    if ($result.ExitCode -ne 0) { Stop-Init "init.ps1: git rev-parse failed." }
    $headSha = ($result.Output -join "`n").Trim()
    if ($headSha -ne $Sha) {
        Stop-Init "init.ps1: fetched $headSha but expected $Sha."
    }

    $sourceBuild = Join-Path $fetchDirectory "scaffold/build"
    $sourceRoot = Join-Path $fetchDirectory "scaffold/root"
    $sourceLock = Join-Path $sourceBuild "plutonium.lock"
    if (-not (Test-Path -LiteralPath $sourceBuild -PathType Container) -or
        -not (Test-Path -LiteralPath $sourceRoot -PathType Container) -or
        -not (Test-Path -LiteralPath $sourceLock -PathType Leaf)) {
        Stop-Init "init.ps1: commit $Sha does not contain the scaffold payload."
    }

    foreach ($item in Get-ChildItem -LiteralPath (Join-Path $fetchDirectory "scaffold") -Recurse -Force) {
        if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
            Stop-Init "init.ps1: scaffold payload contains a symbolic link; refusing to install it."
        }
    }

    foreach ($item in Get-ChildItem -LiteralPath (Join-Path $fetchDirectory "scaffold") -Recurse -File -Force) {
        ConvertTo-LfLineEndings -Path $item.FullName
    }

    Assert-VersionInvariant -Root $fetchDirectory -LockPath $sourceLock
    Assert-SourceContract -Root $fetchDirectory -LockPath $sourceLock

    foreach ($line in [System.IO.File]::ReadAllLines($sourceLock)) {
        if ($line -notmatch "^\s*managed\s+(\S+)\s+([0-9a-fA-F]{64})\s*$") {
            continue
        }
        $relative = $Matches[1]
        $expected = $Matches[2].ToLowerInvariant()
        if (-not (Test-SafeManifestPath -Relative $relative)) {
            Stop-Init "init.ps1: manifest contains an unsafe path: $relative"
        }
        $payloadFile = Get-InstalledSourcePath -Root $fetchDirectory -Relative $relative
        if ([string]::IsNullOrWhiteSpace($payloadFile) -or -not (Test-Path -LiteralPath $payloadFile -PathType Leaf)) {
            Stop-Init "init.ps1: source scaffold is missing $relative."
        }
        $actual = (Get-FileHash -LiteralPath $payloadFile -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $expected) {
            Stop-Init "init.ps1: manifest hash mismatch for $relative; refusing to install the scaffold."
        }
    }

    foreach ($wrapper in @("build.sh", "build.ps1", "build.cmd")) {
        if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot $wrapper) -PathType Leaf)) {
            Stop-Init "init.ps1: source scaffold is missing root $wrapper."
        }
    }

    $stage = Join-Path $Target (".plutonium-init-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    foreach ($line in [System.IO.File]::ReadAllLines($sourceLock)) {
        if ($line -notmatch "^\s*(managed|preserve)\s+(\S+)") { continue }
        $relative = $Matches[2]
        $payloadFile = Get-InstalledSourcePath -Root $fetchDirectory -Relative $relative
        if ([string]::IsNullOrWhiteSpace($payloadFile) -or -not (Test-Path -LiteralPath $payloadFile -PathType Leaf)) {
            Stop-Init "init.ps1: source scaffold is missing $relative."
        }
        $relativePath = $relative.Replace("/", [System.IO.Path]::DirectorySeparatorChar)
        $destination = Join-Path $stage $relativePath
        $parent = Split-Path -Parent $destination
        if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }
        Copy-Item -LiteralPath $payloadFile -Destination $destination -Force
    }
    Copy-Item -LiteralPath $sourceLock -Destination (Join-Path $stage "build/plutonium.lock") -Force

    foreach ($stagedFile in (Get-ChildItem -LiteralPath $stage -Recurse -File -Force)) {
        $relative = $stagedFile.FullName.Substring($stage.Length + 1).Replace("\", "/")
        if ($relative -eq "build/plutonium.lock") { continue }
        if (-not (Test-LockHasPath -LockPath $sourceLock -Relative $relative)) {
            Stop-Init "init.ps1: staged file is not listed by the source lock: $relative"
        }
    }

    $manifestSource = $Source
    if ($Source -eq $DefaultSource) {
        $manifestSource = "github.com/Kiryuumaru/PlutoniumBuild"
    }

    $lockPath = Join-Path $stage "build/plutonium.lock"
    $rewritten = New-Object System.Collections.Generic.List[string]
    $wroteSource = $false
    $wroteSourceUrl = $false
    $wroteCommit = $false
    foreach ($line in [System.IO.File]::ReadAllLines($lockPath)) {
        if ($line.StartsWith("source=", [System.StringComparison]::Ordinal)) {
            $rewritten.Add("source=$manifestSource")
            $wroteSource = $true
        }
        elseif ($line.StartsWith("source_url=", [System.StringComparison]::Ordinal)) {
            $rewritten.Add("source_url=$Source")
            $wroteSourceUrl = $true
        }
        elseif ($line.StartsWith("scaffold_commit=", [System.StringComparison]::Ordinal)) {
            $rewritten.Add("scaffold_commit=$Sha")
            $wroteCommit = $true
        }
        else {
            $rewritten.Add($line)
        }
    }
    if (-not $wroteSource) { $rewritten.Add("source=$manifestSource") }
    if (-not $wroteSourceUrl) { $rewritten.Add("source_url=$Source") }
    if (-not $wroteCommit) { $rewritten.Add("scaffold_commit=$Sha") }
    Write-LfLines -Path $lockPath -Lines $rewritten

    $installedBuild = $false
    $movedWrappers = New-Object System.Collections.Generic.List[string]
    try {
        $buildDestination = Join-Path $Target "build"
        $buildConflict = Get-DestinationConflict -Path $buildDestination
        if ($null -ne $buildConflict) {
            throw "PlutoniumBuild init: refusing to install: build/ $buildConflict."
        }
        Move-Item -LiteralPath (Join-Path $stage "build") -Destination $buildDestination
        $installedBuild = $true
        foreach ($wrapper in @("build.sh", "build.ps1", "build.cmd")) {
            $wrapperDestination = Join-Path $Target $wrapper
            $wrapperConflict = Get-DestinationConflict -Path $wrapperDestination
            if ($null -ne $wrapperConflict) {
                throw "PlutoniumBuild init: refusing to install: root $wrapper $wrapperConflict."
            }
            Move-Item -LiteralPath (Join-Path $stage $wrapper) -Destination $wrapperDestination
            $movedWrappers.Add($wrapper)
        }
    }
    catch {
        if ($installedBuild -and (Test-Path -LiteralPath (Join-Path $Target "build"))) {
            Remove-Item -LiteralPath (Join-Path $Target "build") -Recurse -Force -ErrorAction SilentlyContinue
        }
        foreach ($wrapper in $movedWrappers) {
            Remove-Item -LiteralPath (Join-Path $Target $wrapper) -Force -ErrorAction SilentlyContinue
        }
        throw
    }

    Write-Host "PlutoniumBuild: installed scaffold at commit $Sha."
    Write-Host "Next steps:"
    Write-Host "  1. Review build/build.cs and replace the sample terminal calls."
    Write-Host "  2. Run build\build.cmd to build and test."
}
finally {
    if ($null -ne $stage -and (Test-Path -LiteralPath $stage)) {
        Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $fetchDirectory) {
        Remove-Item -LiteralPath $fetchDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

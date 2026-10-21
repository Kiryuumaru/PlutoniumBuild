# Canonical verified .NET SDK bootstrap for Windows PowerShell 5.1. Dot-source
# this file and call Resolve-PlutoniumDotNet. It never installs globally, never
# edits the user PATH, and refuses to run a download that does not match the
# pins in plutonium.lock. Executable-affecting dotnet and NuGet environment
# variables are cleared before probing, and sdk.rollForward / allowPrerelease
# are validated from global.json before any SDK is accepted.

function Get-PlutoniumLockValue {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Key
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    $prefix = $Key + "="
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith("#", [System.StringComparison]::Ordinal)) {
            continue
        }

        if ($trimmed.StartsWith($prefix, [System.StringComparison]::Ordinal)) {
            return $trimmed.Substring($prefix.Length).Trim()
        }
    }

    return $null
}

function Clear-PlutoniumEnvironment {
    foreach ($name in @(
        "DOTNET_STARTUP_HOOKS",
        "DOTNET_ADDITIONAL_DEPS",
        "DOTNET_SHARED_STORE",
        "NUGET_PACKAGES",
        "NUGET_HTTP_CACHE_PATH",
        "DOTNET_HOST_PATH",
        "DOTNET_ROOT",
        "DOTNET_ROLL_FORWARD"
    )) {
        Remove-Item -LiteralPath ("Env:\" + $name) -ErrorAction SilentlyContinue
    }
}

function Get-PlutoniumSdkSettings {
    param([Parameter(Mandatory = $true)][string]$GlobalJson)

    if (-not (Test-Path -LiteralPath $GlobalJson -PathType Leaf)) {
        throw "global.json not found at $GlobalJson."
    }

    $json = Get-Content -LiteralPath $GlobalJson -Raw | ConvertFrom-Json
    if ($null -eq $json.PSObject.Properties["sdk"] -or
        $null -eq $json.sdk.PSObject.Properties["version"] -or
        $null -eq $json.sdk.PSObject.Properties["rollForward"] -or
        $null -eq $json.sdk.PSObject.Properties["allowPrerelease"]) {
        throw "global.json must set sdk.version, sdk.rollForward, and sdk.allowPrerelease; refusing to guess."
    }

    $version = [string]$json.sdk.version
    $rollForward = [string]$json.sdk.rollForward
    $allowPrerelease = $json.sdk.allowPrerelease
    if ($allowPrerelease -isnot [bool]) {
        throw "global.json sdk.allowPrerelease must be a boolean."
    }
    if ($rollForward -notin @("disable", "patch", "feature", "minor", "major", "latestPatch", "latestFeature", "latestMinor", "latestMajor")) {
        throw "global.json sdk.rollForward is not supported: $rollForward."
    }
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "global.json must pin sdk.version; refusing to install an unpinned SDK."
    }
    if ($version.Contains("-") -and -not $allowPrerelease) {
        throw "global.json sdk.version is a prerelease but sdk.allowPrerelease is false."
    }

    return [pscustomobject]@{
        Version = $version
        RollForward = $rollForward
        AllowPrerelease = $allowPrerelease
    }
}

function Test-PlutoniumVersionSatisfies {
    param(
        [Parameter(Mandatory = $true)][string]$Actual,
        [Parameter(Mandatory = $true)][string]$Pinned,
        [Parameter(Mandatory = $true)][string]$RollForward,
        [Parameter(Mandatory = $true)][bool]$AllowPrerelease
    )

    if ($Actual.Contains("-") -and -not $AllowPrerelease) {
        return $false
    }
    if ($Actual -eq $Pinned) {
        return $true
    }

    $actualCore = $Actual.Split("-")[0]
    $pinnedCore = $Pinned.Split("-")[0]
    $actualParts = $actualCore.Split(".")
    $pinnedParts = $pinnedCore.Split(".")
    if ($actualParts.Length -lt 3 -or $pinnedParts.Length -lt 3) {
        return $false
    }

    $actualNumbers = @()
    $pinnedNumbers = @()
    foreach ($part in $actualParts[0..2]) {
        $number = 0
        if (-not [int]::TryParse($part, [ref]$number)) { return $false }
        $actualNumbers += $number
    }
    foreach ($part in $pinnedParts[0..2]) {
        $number = 0
        if (-not [int]::TryParse($part, [ref]$number)) { return $false }
        $pinnedNumbers += $number
    }

    if ($actualNumbers[0] -lt $pinnedNumbers[0]) { return $false }
    if ($actualNumbers[0] -eq $pinnedNumbers[0] -and $actualNumbers[1] -lt $pinnedNumbers[1]) { return $false }
    if ($actualNumbers[0] -eq $pinnedNumbers[0] -and
        $actualNumbers[1] -eq $pinnedNumbers[1] -and
        $actualNumbers[2] -lt $pinnedNumbers[2]) {
        return $false
    }

    switch ($RollForward.ToLowerInvariant()) {
        "disable" { return $false }
        "patch" {
            return ($actualNumbers[0] -eq $pinnedNumbers[0] -and
                $actualNumbers[1] -eq $pinnedNumbers[1] -and
                [math]::Floor($actualNumbers[2] / 100) -eq [math]::Floor($pinnedNumbers[2] / 100))
        }
        "latestPatch" {
            return ($actualNumbers[0] -eq $pinnedNumbers[0] -and
                $actualNumbers[1] -eq $pinnedNumbers[1] -and
                [math]::Floor($actualNumbers[2] / 100) -eq [math]::Floor($pinnedNumbers[2] / 100))
        }
        "feature" {
            return ($actualNumbers[0] -eq $pinnedNumbers[0] -and $actualNumbers[1] -eq $pinnedNumbers[1])
        }
        "latestFeature" {
            return ($actualNumbers[0] -eq $pinnedNumbers[0] -and $actualNumbers[1] -eq $pinnedNumbers[1])
        }
        "minor" {
            return ($actualNumbers[0] -eq $pinnedNumbers[0])
        }
        "latestMinor" {
            return ($actualNumbers[0] -eq $pinnedNumbers[0])
        }
        "major" { return $true }
        "latestMajor" { return $true }
        default { return $false }
    }
}

function Get-PlutoniumProbe {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string]$ProbeDirectory,
        [Parameter(Mandatory = $true)][string]$PinnedVersion,
        [Parameter(Mandatory = $true)][string]$RollForward,
        [Parameter(Mandatory = $true)][bool]$AllowPrerelease
    )

    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) {
        return $null
    }

    Push-Location -LiteralPath $ProbeDirectory
    try {
        $reported = (& $Executable --version 2>$null | Select-Object -First 1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0 -or [string]::IsNullOrWhiteSpace($reported)) {
        return $null
    }

    $reported = ([string]$reported).Trim()
    if (-not (Test-PlutoniumVersionSatisfies -Actual $reported -Pinned $PinnedVersion -RollForward $RollForward -AllowPrerelease $AllowPrerelease)) {
        return $null
    }

    return $reported
}

function Resolve-PlutoniumDotNet {
    param(
        [Parameter(Mandatory = $true)][string]$GlobalJson,
        [Parameter(Mandatory = $true)][string]$CacheDirectory,
        [Parameter(Mandatory = $true)][string]$LockFile
    )

    Clear-PlutoniumEnvironment
    $settings = Get-PlutoniumSdkSettings -GlobalJson $GlobalJson
    $pinnedVersion = $settings.Version
    $rollForward = $settings.RollForward
    $allowPrerelease = $settings.AllowPrerelease
    $probeDirectory = Split-Path -Parent (Resolve-Path -LiteralPath $GlobalJson).Path
    $installDirectory = Join-Path $CacheDirectory "dotnet-win"
    $cacheExecutable = Join-Path $installDirectory "dotnet.exe"

    if (Test-Path -LiteralPath $cacheExecutable -PathType Leaf) {
        $reported = Get-PlutoniumProbe -Executable $cacheExecutable -ProbeDirectory $probeDirectory `
            -PinnedVersion $pinnedVersion -RollForward $rollForward -AllowPrerelease $allowPrerelease
        if ($null -ne $reported) {
            return Set-PlutoniumToolchain -Executable $cacheExecutable -Version $reported
        }

        Remove-Item -LiteralPath $installDirectory -Recurse -Force
    }

    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        $candidate = [System.IO.Path]::GetFullPath($command.Path)
        $reported = Get-PlutoniumProbe -Executable $candidate -ProbeDirectory $probeDirectory `
            -PinnedVersion $pinnedVersion -RollForward $rollForward -AllowPrerelease $allowPrerelease
        if ($null -ne $reported) {
            if ($env:PLUTONIUM_REQUIRE_VERIFIED_DOTNET -eq "1") {
                Write-Warning "PlutoniumBuild: ignoring version-matching dotnet on PATH ($candidate); PLUTONIUM_REQUIRE_VERIFIED_DOTNET requires the checksum-verified install."
            }
            else {
                Write-Warning "PlutoniumBuild: using version-verified .NET SDK $reported from PATH; the checksum-verified cache was not available ($candidate)"
                return Set-PlutoniumToolchain -Executable $candidate -Version $reported
            }
        }
    }

    $installerRepository = Get-PlutoniumLockValue -Path $LockFile -Key "installer_repo"
    $installerCommit = Get-PlutoniumLockValue -Path $LockFile -Key "installer_commit"
    $installerHash = Get-PlutoniumLockValue -Path $LockFile -Key "installer_ps1_sha256"
    if ($installerRepository -ne "dotnet/install-scripts") {
        throw "plutonium.lock must pin installer_repo=dotnet/install-scripts."
    }
    if ($installerCommit -notmatch "^[0-9a-fA-F]{40}$") {
        throw "plutonium.lock must pin a full installer_commit SHA."
    }
    if ($installerHash -notmatch "^[0-9a-fA-F]{64}$") {
        throw "plutonium.lock must pin installer_ps1_sha256."
    }

    $architecture = "x64"
    if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") {
        $architecture = "arm64"
    }
    $rid = "win-" + $architecture

    if (-not (Test-Path -LiteralPath $CacheDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $CacheDirectory -Force | Out-Null
    }

    $temporaryDirectory = Join-Path $CacheDirectory (".sdk-install-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $temporaryDirectory -Force | Out-Null

    try {
        $installerUrl = "https://raw.githubusercontent.com/$installerRepository/$installerCommit/src/dotnet-install.ps1"
        $installerPath = Join-Path $temporaryDirectory "dotnet-install.ps1"
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $installerUrl -OutFile $installerPath
        }
        catch {
            throw "Failed to download the .NET installer for SDK $pinnedVersion (offline or network failure): $($_.Exception.Message)"
        }

        $actualHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $installerHash.ToLowerInvariant()) {
            throw "Installer checksum mismatch (sha256) for SDK $pinnedVersion; refusing to execute it."
        }

        $installerOutput = & powershell -NoProfile -ExecutionPolicy Bypass -File $installerPath `
            -Version $pinnedVersion -InstallDir $installDirectory -NoPath -DryRun -Architecture $architecture 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "The verified .NET installer failed for SDK $pinnedVersion."
        }

        if (Test-Path -LiteralPath (Join-Path $installDirectory "dotnet.exe") -PathType Leaf) {
            $reported = Get-PlutoniumProbe -Executable $cacheExecutable -ProbeDirectory $probeDirectory `
                -PinnedVersion $pinnedVersion -RollForward $rollForward -AllowPrerelease $allowPrerelease
            if ($null -eq $reported) {
                throw "The installer produced a dotnet that does not satisfy SDK $pinnedVersion."
            }

            return Set-PlutoniumToolchain -Executable $cacheExecutable -Version $reported
        }

        $archiveUrl = $null
        foreach ($line in $installerOutput) {
            $text = [string]$line
            if ($text -match "URL #0 - primary: (?<url>https://\S+)") {
                $archiveUrl = $Matches["url"]
                break
            }
        }

        if ([string]::IsNullOrWhiteSpace($archiveUrl)) {
            throw "The verified installer did not report a download URL for SDK $pinnedVersion."
        }
        if (-not $archiveUrl.StartsWith("https://builds.dotnet.microsoft.com/dotnet/Sdk/$pinnedVersion/", [System.StringComparison]::Ordinal)) {
            throw "Installer reported an unexpected SDK URL: $archiveUrl"
        }

        $archivePath = Join-Path $temporaryDirectory "sdk.zip"
        try {
            Invoke-WebRequest -UseBasicParsing -Uri $archiveUrl -OutFile $archivePath
        }
        catch {
            throw "Failed to download the .NET SDK $pinnedVersion archive (offline or network failure): $($_.Exception.Message)"
        }

        $expectedHash = Get-PlutoniumLockValue -Path $LockFile -Key ("sdk_archive_" + $rid + "_sha512")
        if ($expectedHash -notmatch "^[0-9a-fA-F]{128}$") {
            throw "plutonium.lock has no SHA-512 pin for $rid (SDK $pinnedVersion)."
        }
        $actualArchiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA512).Hash.ToLowerInvariant()
        if ($actualArchiveHash -ne $expectedHash.ToLowerInvariant()) {
            throw "SDK archive checksum mismatch (sha512) for $rid; refusing to extract it."
        }

        $extractDirectory = Join-Path $temporaryDirectory "extract"
        Expand-Archive -LiteralPath $archivePath -DestinationPath $extractDirectory -Force
        $extractedDotNet = Join-Path $extractDirectory "dotnet.exe"
        $reported = Get-PlutoniumProbe -Executable $extractedDotNet -ProbeDirectory $probeDirectory `
            -PinnedVersion $pinnedVersion -RollForward $rollForward -AllowPrerelease $allowPrerelease
        if ($null -eq $reported) {
            throw "The verified .NET SDK archive does not report SDK $pinnedVersion."
        }

        if (Test-Path -LiteralPath $installDirectory) {
            Remove-Item -LiteralPath $installDirectory -Recurse -Force
        }
        Move-Item -LiteralPath $extractDirectory -Destination $installDirectory
        return Set-PlutoniumToolchain -Executable $cacheExecutable -Version $reported
    }
    finally {
        if (Test-Path -LiteralPath $temporaryDirectory) {
            Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Set-PlutoniumToolchain {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string]$Version
    )

    $root = Split-Path -Parent $Executable
    Clear-PlutoniumEnvironment
    $env:DOTNET_ROOT = $root
    $env:DOTNET_MULTILEVEL_LOOKUP = "0"
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
    $env:DOTNET_NOLOGO = "1"
    if (-not ($env:PATH -split [System.IO.Path]::PathSeparator | Where-Object { $_ -eq $root })) {
        $env:PATH = $root + [System.IO.Path]::PathSeparator + $env:PATH
    }

    Write-Host "PlutoniumBuild: using .NET SDK $Version ($Executable)"
    return [pscustomobject]@{
        Exe = $Executable
        Root = $root
        Version = $Version
    }
}

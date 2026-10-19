# PlutoniumBuild consumer runner template: uses a suitable .NET SDK when one is
# on PATH, otherwise bootstraps the SDK pinned in global.json into .plutonium\temp
# (repo-local, --no-path), then quiet-builds your build app and runs it with every
# argument passed through. Copy this file next to build.sh/build.cmd in your
# repository root.

[CmdletBinding()]
Param(
    [Parameter(Position=0,Mandatory=$false,ValueFromRemainingArguments=$true)]
    [string[]]$BuildArguments
)

Set-StrictMode -Version 2.0; $ErrorActionPreference = "Stop"; $ConfirmPreference = "None"; trap { Write-Error $_ -ErrorAction Continue; exit 1 }

###########################################################################
# CONFIGURATION
###########################################################################

# The one line to edit: your build app project, relative to this script (or absolute).
# Setting PLUTONIUM_APP_PROJECT in the environment overrides it for one-off runs.
$AppProject = if ($env:PLUTONIUM_APP_PROJECT) { $env:PLUTONIUM_APP_PROJECT } else { "build\_build.csproj" }

$TempDirectory = Join-Path $PSScriptRoot ".plutonium\temp"
$DotNetGlobalFile = Join-Path $PSScriptRoot "global.json"
$DotNetInstallUrl = "https://dot.net/v1/dotnet-install.ps1"
$DotNetChannel = "STS"

# Quiet, deterministic CLI behavior. LatestMajor keeps your build app runnable on
# machines where only the bootstrapped SDK's runtime exists.
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 1
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = 1
$env:DOTNET_MULTILEVEL_LOOKUP = 0
$env:DOTNET_NOLOGO = 1
$env:DOTNET_ROLL_FORWARD = "LatestMajor"

###########################################################################
# EXECUTION
###########################################################################

function ExecSafe([scriptblock] $cmd) {
    & $cmd
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}

# Run from the repository root so SDK resolution, the build app, and relative
# paths behave the same no matter where this script is invoked from.
Set-Location -LiteralPath $PSScriptRoot

if (-not [System.IO.Path]::IsPathRooted($AppProject)) {
    $AppProject = Join-Path $PSScriptRoot $AppProject
}

# A runnable dotnet whose `--version` succeeds is the de-facto global.json gate:
# no explicit version compare, only bootstrap when nothing usable is present.
if ($null -ne (Get-Command "dotnet" -ErrorAction SilentlyContinue) -and $(dotnet --version) -and $LASTEXITCODE -eq 0) {
    $DotNetExe = (Get-Command "dotnet").Path
}
else {
    $DotNetInstallFile = Join-Path $TempDirectory "dotnet-install.ps1"
    New-Item -ItemType Directory -LiteralPath $TempDirectory -Force | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    (New-Object System.Net.WebClient).DownloadFile($DotNetInstallUrl, $DotNetInstallFile)

    $DotNetVersion = $null
    if (Test-Path -LiteralPath $DotNetGlobalFile) {
        $globalJson = Get-Content -LiteralPath $DotNetGlobalFile -Raw | ConvertFrom-Json
        if ($globalJson.PSObject.Properties["sdk"] -and $globalJson.sdk.PSObject.Properties["version"]) {
            $DotNetVersion = $globalJson.sdk.version
        }
    }

    $DotNetDirectory = Join-Path $TempDirectory "dotnet-win"
    if ($null -eq $DotNetVersion) {
        Write-Output "PlutoniumBuild: no SDK version pinned; installing the latest $DotNetChannel SDK."
        ExecSafe { & powershell -NoProfile -ExecutionPolicy ByPass -File $DotNetInstallFile -InstallDir $DotNetDirectory -Channel $DotNetChannel -NoPath }
    }
    else {
        ExecSafe { & powershell -NoProfile -ExecutionPolicy ByPass -File $DotNetInstallFile -InstallDir $DotNetDirectory -Version $DotNetVersion -NoPath }
    }

    $DotNetExe = Join-Path $DotNetDirectory "dotnet.exe"
    $env:PATH = "$DotNetDirectory;$env:PATH"
}

$env:DOTNET_EXE = $DotNetExe
Write-Output "PlutoniumBuild: using .NET SDK $(& $DotNetExe --version) ($DotNetExe)"
Write-Output "PlutoniumBuild: running build app $AppProject"

$passthrough = @()
if ($null -ne $BuildArguments) { $passthrough = $BuildArguments }

ExecSafe { & $DotNetExe build $AppProject /nodeReuse:false /p:UseSharedCompilation=false -nologo -clp:NoSummary --verbosity quiet }
ExecSafe { & $DotNetExe run --project $AppProject --no-build -- @passthrough }

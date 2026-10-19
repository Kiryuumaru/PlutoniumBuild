# PlutoniumBuild runner: uses a suitable .NET SDK when one is on PATH, otherwise
# bootstraps the SDK pinned in global.json into .plutonium\temp (repo-local,
# --no-path), then builds the solution and runs every test.

Set-StrictMode -Version 2.0; $ErrorActionPreference = "Stop"; $ConfirmPreference = "None"; trap { Write-Error $_ -ErrorAction Continue; exit 1 }

###########################################################################
# CONFIGURATION
###########################################################################

$SolutionFile = "PlutoniumBuild.sln"
$TempDirectory = Join-Path $PSScriptRoot ".plutonium\temp"
$DotNetGlobalFile = Join-Path $PSScriptRoot "global.json"
$DotNetInstallUrl = "https://dot.net/v1/dotnet-install.ps1"
$DotNetChannel = "STS"

# Quiet, deterministic CLI behavior. LatestMajor lets the net8.0/net9.0/net10.0
# test matrix run on machines where only the bootstrapped SDK's runtime exists.
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

# Run from the repository root so SDK resolution, tests, and relative paths
# behave the same no matter where this script is invoked from.
Set-Location -LiteralPath $PSScriptRoot

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

ExecSafe { & $DotNetExe build $SolutionFile /nodeReuse:false /p:UseSharedCompilation=false -nologo -clp:NoSummary --verbosity quiet }
ExecSafe { & $DotNetExe test $SolutionFile --no-build -nologo --verbosity minimal }

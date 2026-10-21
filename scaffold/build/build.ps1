# Consumer build launcher for Windows PowerShell 5.1. Resolves the pinned SDK,
# builds build/build.csproj, and forwards every argument to the build app.

[CmdletBinding()]
Param(
    [Parameter(Position=0,Mandatory=$false,ValueFromRemainingArguments=$true)]
    [string[]]$BuildArguments
)

$ErrorActionPreference = "Stop"
$ConfirmPreference = "None"

$BuildDirectory = $PSScriptRoot
$RepositoryRoot = Split-Path -Parent $BuildDirectory
. (Join-Path $BuildDirectory "bootstrap.ps1")

$toolchain = Resolve-PlutoniumDotNet `
    -GlobalJson (Join-Path $BuildDirectory "global.json") `
    -CacheDirectory (Join-Path $BuildDirectory "temp") `
    -LockFile (Join-Path $BuildDirectory "plutonium.lock")

$project = Join-Path $BuildDirectory "build.csproj"
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    Write-Error "build/build.csproj is missing; run build/update.ps1 or restore the scaffold."
    exit 1
}

Set-Location -LiteralPath $RepositoryRoot

& $toolchain.Exe build $project /nodeReuse:false /p:UseSharedCompilation=false -nologo -clp:NoSummary --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$forward = @("run", "--project", $project, "--no-build", "--")
if ($null -ne $BuildArguments) {
    $forward += $BuildArguments
}

& $toolchain.Exe @forward
exit $LASTEXITCODE

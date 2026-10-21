# Maintainer runner: resolves the pinned SDK through the same verified bootstrap
# the scaffold ships, then builds the solution and runs every test.

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"
$ConfirmPreference = "None"
$global:LASTEXITCODE = 0

$RepositoryRoot = $PSScriptRoot
. (Join-Path $RepositoryRoot "scaffold/build/bootstrap.ps1")

$toolchain = Resolve-PlutoniumDotNet `
    -GlobalJson (Join-Path $RepositoryRoot "global.json") `
    -CacheDirectory (Join-Path $RepositoryRoot ".plutonium/temp") `
    -LockFile (Join-Path $RepositoryRoot "scaffold/build/plutonium.lock")

Set-Location -LiteralPath $RepositoryRoot

& $toolchain.Exe build PlutoniumBuild.sln /nodeReuse:false /p:UseSharedCompilation=false -nologo -clp:NoSummary --verbosity quiet
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

# The test matrix targets net8.0/net9.0/net10.0. LatestMajor lets every target
# execute on the pinned SDK's runtime when the host has no older runtimes.
$env:DOTNET_ROLL_FORWARD = "LatestMajor"
& $toolchain.Exe test PlutoniumBuild.sln --no-build -nologo --verbosity minimal
exit $LASTEXITCODE

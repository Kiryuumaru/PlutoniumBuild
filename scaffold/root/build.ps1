# Thin repository-root wrapper. Generated CI invokes ./build.cmd, so this file
# must stay at the root and only forward into build/build.ps1.

$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "build\build.ps1") @args
exit $LASTEXITCODE

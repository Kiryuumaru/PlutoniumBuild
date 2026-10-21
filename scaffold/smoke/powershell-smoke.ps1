# Standalone PowerShell execution smoke for the scaffold.
#
# It creates a temporary source repository and consumer, then exercises
# init.ps1, update.ps1, and (unless -SkipBuild is set) build.ps1 in child
# PowerShell processes. The child-process boundary is deliberate: the installed
# scripts call exit on refusal, so an in-process call would terminate this smoke.

[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$KeepTemp,
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
)

$ErrorActionPreference = "Stop"
$ConfirmPreference = "None"

if ($PSVersionTable.PSEdition -eq "Desktop") {
    $hostExecutable = Join-Path $PSHOME "powershell.exe"
}
else {
    $hostExecutable = Join-Path $PSHOME "pwsh"
}
if (-not (Test-Path -LiteralPath $hostExecutable -PathType Leaf)) {
    $command = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        $command = Get-Command powershell -ErrorAction SilentlyContinue
    }
    if ($null -eq $command) {
        throw "powershell-smoke: could not locate a PowerShell host executable."
    }
    $hostExecutable = $command.Path
}

$failures = 0
function Fail-Smoke {
    param([string]$Message)
    Write-Host "powershell-smoke: $Message" -ForegroundColor Red
    $script:failures = $script:failures + 1
}

function Invoke-SmokeScript {
    param(
        [string]$ScriptPath,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [string]$Label,
        [switch]$ExpectFailure
    )
    Push-Location -LiteralPath $WorkingDirectory
    try {
        $output = & $hostExecutable -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    $text = ($output | Out-String)
    if ($exitCode -ne 0 -and -not $ExpectFailure) {
        Write-Host "powershell-smoke: $Label exited $exitCode." -ForegroundColor Yellow
        Write-Host $text
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $text
    }
}

function Set-SourceManagedHash {
    param([string]$SourceRoot, [string]$Relative)
    $sourceFile = Join-Path (Join-Path $SourceRoot "scaffold") ($Relative.Replace("/", [System.IO.Path]::DirectorySeparatorChar))
    if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf)) {
        throw "powershell-smoke: source file not found for hash: $Relative"
    }
    $hash = (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $lockPath = Join-Path $SourceRoot "scaffold/build/plutonium.lock"
    $rewritten = New-Object System.Collections.Generic.List[string]
    $found = $false
    foreach ($line in [System.IO.File]::ReadAllLines($lockPath)) {
        if ($line -match "^\s*managed\s+(\S+)\s+[0-9a-fA-F]{64}\s*$" -and $Matches[1] -eq $Relative) {
            $rewritten.Add("managed $Relative $hash")
            $found = $true
        }
        else {
            $rewritten.Add($line)
        }
    }
    if (-not $found) {
        throw "powershell-smoke: source lock has no managed entry for $Relative"
    }
    [System.IO.File]::WriteAllLines($lockPath, $rewritten)
}

function Add-SourceManagedEntry {
    param([string]$SourceRoot, [string]$Relative)
    $sourceFile = Join-Path (Join-Path $SourceRoot "scaffold") ($Relative.Replace("/", [System.IO.Path]::DirectorySeparatorChar))
    if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf)) {
        throw "powershell-smoke: source file not found for entry: $Relative"
    }
    $hash = (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $lockPath = Join-Path $SourceRoot "scaffold/build/plutonium.lock"
    $existing = [System.IO.File]::ReadAllLines($lockPath)
    if ($existing -match "^\s*managed\s+([^\s]+)\s+[0-9a-fA-F]{64}\s*$" -and $Matches[1] -eq $Relative) {
        Set-SourceManagedHash -SourceRoot $SourceRoot -Relative $Relative
        return
    }
    [System.IO.File]::AppendAllText($lockPath, "managed $Relative $hash" + [Environment]::NewLine)
}

$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ("plutonium-ps-smoke-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temporary -Force | Out-Null
$source = Join-Path $temporary "source"
$consumer = Join-Path $temporary "consumer"

try {
    New-Item -ItemType Directory -Path $source -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $RepositoryRoot "scaffold") -Destination (Join-Path $source "scaffold") -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $RepositoryRoot "init.ps1") -Destination (Join-Path $source "init.ps1") -Force
    New-Item -ItemType Directory -Path (Join-Path $source "src") -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $RepositoryRoot "src/Directory.Build.props") -Destination (Join-Path $source "src/Directory.Build.props") -Force

    & git -C $source init -q
    if ($LASTEXITCODE -ne 0) { throw "powershell-smoke: git init failed for source." }
    & git -C $source config user.email smoke@example.test
    & git -C $source config user.name Smoke
    & git -C $source add -A
    & git -C $source commit -qm initial
    if ($LASTEXITCODE -ne 0) { throw "powershell-smoke: source commit failed." }
    $sha1 = (& git -C $source rev-parse HEAD).Trim()

    # Credential-bearing source URLs must be rejected before any fetch/log/write.
    $badTarget = Join-Path $temporary "bad-source"
    New-Item -ItemType Directory -Path $badTarget -Force | Out-Null
    $bad = Invoke-SmokeScript -ScriptPath (Join-Path $RepositoryRoot "init.ps1") `
        -Arguments @("-Source", "https://user:secret-token@example.test/repo.git", "-Sha", $sha1, "-Target", $badTarget) `
        -WorkingDirectory $RepositoryRoot -Label "init with userinfo source" -ExpectFailure
    if ($bad.ExitCode -eq 0) { Fail-Smoke "init accepted a userinfo source URL." }
    if ($bad.Output -match "secret-token") { Fail-Smoke "init echoed the source credential." }
    if (Test-Path -LiteralPath (Join-Path $badTarget "build")) { Fail-Smoke "init wrote a scaffold for a rejected source." }

    $badQueryTarget = Join-Path $temporary "bad-query-source"
    New-Item -ItemType Directory -Path $badQueryTarget -Force | Out-Null
    $badQuery = Invoke-SmokeScript -ScriptPath (Join-Path $RepositoryRoot "init.ps1") `
        -Arguments @("-Source", "https://example.test/repo.git?access_token=secret-token", "-Sha", $sha1, "-Target", $badQueryTarget) `
        -WorkingDirectory $RepositoryRoot -Label "init with query source" -ExpectFailure
    if ($badQuery.ExitCode -eq 0) { Fail-Smoke "init accepted a query-token source URL." }
    if ($badQuery.Output -match "secret-token") { Fail-Smoke "init echoed the query credential." }

    # A dangling reparse-point destination must be refused.
    $linkTarget = Join-Path $temporary "link-target"
    New-Item -ItemType Directory -Path $linkTarget -Force | Out-Null
    $linkPath = Join-Path $linkTarget "build"
    try {
        New-Item -ItemType SymbolicLink -Path $linkPath -Target (Join-Path $temporary "missing-target") -Force | Out-Null
        $link = Invoke-SmokeScript -ScriptPath (Join-Path $RepositoryRoot "init.ps1") `
            -Arguments @("-Source", $source, "-Sha", $sha1, "-Target", $linkTarget) `
            -WorkingDirectory $RepositoryRoot -Label "init with reparse-point build"
        if ($link.ExitCode -eq 0) { Fail-Smoke "init accepted a dangling reparse-point build destination." }
        $linkItem = Get-Item -LiteralPath $linkPath -Force -ErrorAction SilentlyContinue
        if ($null -eq $linkItem) { Fail-Smoke "init removed the reparse-point destination." }

        $wrapTarget = Join-Path $temporary "link-wrapper-target"
        New-Item -ItemType Directory -Path $wrapTarget -Force | Out-Null
        New-Item -ItemType SymbolicLink -Path (Join-Path $wrapTarget "build.sh") -Target (Join-Path $temporary "missing-wrapper") -Force | Out-Null
        $wrapLink = Invoke-SmokeScript -ScriptPath (Join-Path $RepositoryRoot "init.ps1") `
            -Arguments @("-Source", $source, "-Sha", $sha1, "-Target", $wrapTarget) `
            -WorkingDirectory $RepositoryRoot -Label "init with reparse-point wrapper"
        if ($wrapLink.ExitCode -eq 0) { Fail-Smoke "init accepted a dangling reparse-point root wrapper." }
    }
    catch {
        Write-Warning "powershell-smoke: symbolic-link coverage skipped: $($_.Exception.Message)"
    }

    # A source whose props version disagrees with the lock must be rejected.
    $propsSource = Join-Path $temporary "props-source"
    & git clone -q $source $propsSource
    if ($LASTEXITCODE -ne 0) { throw "powershell-smoke: could not clone props source." }
    $propsPath = Join-Path $propsSource "src/Directory.Build.props"
    $propsText = [System.IO.File]::ReadAllText($propsPath)
    [System.IO.File]::WriteAllText($propsPath, $propsText.Replace("<Version>0.1.0</Version>", "<Version>9.9.9</Version>"))
    & git -C $propsSource add -A
    & git -C $propsSource commit -qm props-mismatch
    $propsSha = (& git -C $propsSource rev-parse HEAD).Trim()
    $propsTarget = Join-Path $temporary "props-target"
    New-Item -ItemType Directory -Path $propsTarget -Force | Out-Null
    $propsInstall = Invoke-SmokeScript -ScriptPath (Join-Path $RepositoryRoot "init.ps1") `
        -Arguments @("-Source", $propsSource, "-Sha", $propsSha, "-Target", $propsTarget) `
        -WorkingDirectory $RepositoryRoot -Label "init with props mismatch" -ExpectFailure
    if ($propsInstall.ExitCode -eq 0) { Fail-Smoke "init accepted a props version mismatch." }
    if ($propsInstall.Output -notmatch "Directory.Build.props version 9.9.9 does not match") {
        Fail-Smoke "init did not report the props version mismatch."
    }

    New-Item -ItemType Directory -Path $consumer -Force | Out-Null
    & git -C $consumer init -q
    & git -C $consumer config user.email smoke@example.test
    & git -C $consumer config user.name Smoke
    $install = Invoke-SmokeScript -ScriptPath (Join-Path $RepositoryRoot "init.ps1") `
        -Arguments @("-Source", $source, "-Sha", $sha1, "-Target", $consumer) `
        -WorkingDirectory $RepositoryRoot -Label "init"
    if ($install.ExitCode -ne 0) { throw "powershell-smoke: init failed." }
    $lockText = Get-Content -LiteralPath (Join-Path $consumer "build/plutonium.lock") -Raw
    if ($lockText -match "secret-token") { Fail-Smoke "installed lock contains a credential." }

    Add-Content -LiteralPath (Join-Path $consumer "build/build.cs") -Value "# consumer sentinel"
    Add-Content -LiteralPath (Join-Path $consumer "build/Program.cs") -Value "// consumer sentinel"
    $preservedProgram = Join-Path $consumer "build/Program.cs"
    $preservedHash = (Get-FileHash -LiteralPath $preservedProgram -Algorithm SHA256).Hash.ToLowerInvariant()

    # Add preserve drift to the source: Program.cs becomes preserve-only in
    # effect, and its managed hash must follow the installed copy on update.
    Add-Content -LiteralPath (Join-Path $source "scaffold/build/build.sh") -Value "# source marker"
    Add-Content -LiteralPath (Join-Path $source "scaffold/build/Program.cs") -Value "// source marker"
    $sourceLock = Join-Path $source "scaffold/build/plutonium.lock"
    $sourceLockText = [System.IO.File]::ReadAllText($sourceLock)
    if ($sourceLockText -notmatch "(?m)^preserve build/Program\.cs\s*$") {
        [System.IO.File]::AppendAllText($sourceLock, "preserve build/Program.cs" + [Environment]::NewLine)
    }
    Set-SourceManagedHash -SourceRoot $source -Relative "build/build.sh"
    Set-SourceManagedHash -SourceRoot $source -Relative "build/Program.cs"
    & git -C $source add -A
    & git -C $source commit -qm update
    if ($LASTEXITCODE -ne 0) { throw "powershell-smoke: update commit failed." }
    $sha2 = (& git -C $source rev-parse HEAD).Trim()

    $update = Invoke-SmokeScript -ScriptPath (Join-Path $consumer "build/update.ps1") `
        -Arguments @("-Source", $source, "-Sha", $sha2, "-Target", $consumer, "-NoSmoke") `
        -WorkingDirectory $consumer -Label "update"
    if ($update.ExitCode -ne 0) { throw "powershell-smoke: update failed." }
    if (-not (Select-String -LiteralPath (Join-Path $consumer "build/build.sh") -Pattern "# source marker" -Quiet)) {
        Fail-Smoke "update did not install the managed build.sh change."
    }
    if ((Get-FileHash -LiteralPath $preservedProgram -Algorithm SHA256).Hash.ToLowerInvariant() -ne $preservedHash) {
        Fail-Smoke "update overwrote a preserved file."
    }
    $installedLock = Get-Content -LiteralPath (Join-Path $consumer "build/plutonium.lock") -Raw
    if ($installedLock -notmatch "managed build/Program\.cs $preservedHash") {
        Fail-Smoke "update did not rewrite the dual-tag managed hash to the installed copy."
    }

    $again = Invoke-SmokeScript -ScriptPath (Join-Path $consumer "build/update.ps1") `
        -Arguments @("-Source", $source, "-Sha", $sha2, "-Target", $consumer, "-NoSmoke") `
        -WorkingDirectory $consumer -Label "idempotent update"
    if ($again.ExitCode -ne 0 -or $again.Output -notmatch "already at") {
        Fail-Smoke "update was not idempotent at the same commit."
    }

    # On Linux, a fake cached toolchain can force the smoke build to fail and
    # prove that update.ps1 removes files introduced by the new lock.
    $isWindowsHost = $PSVersionTable.PSEdition -eq "Desktop" -or $env:OS -eq "Windows_NT"
    if (-not $isWindowsHost) {
        $fakeDirectory = Join-Path $consumer "build/temp/dotnet-win"
        New-Item -ItemType Directory -Path $fakeDirectory -Force | Out-Null
        $fakeDotNet = Join-Path $fakeDirectory "dotnet.exe"
        $fakeScript = @"
#!/usr/bin/env bash
if [ "`${1:-}" = "--version" ]; then
    echo "10.0.402"
    exit 0
fi
exit 1
"@
        [System.IO.File]::WriteAllText($fakeDotNet, $fakeScript)
        & chmod 755 $fakeDotNet
        if ($LASTEXITCODE -ne 0) { throw "powershell-smoke: could not mark the fake dotnet executable." }

        $lockBeforeRollback = (Get-FileHash -LiteralPath (Join-Path $consumer "build/plutonium.lock") -Algorithm SHA256).Hash
        $buildShBeforeRollback = (Get-FileHash -LiteralPath (Join-Path $consumer "build/build.sh") -Algorithm SHA256).Hash
        Set-Content -LiteralPath (Join-Path $source "scaffold/build/rollback-probe.txt") -Value "probe"
        Add-SourceManagedEntry -SourceRoot $source -Relative "build/rollback-probe.txt"
        & git -C $source add -A
        & git -C $source commit -qm linux-smoke-rollback
        if ($LASTEXITCODE -ne 0) { throw "powershell-smoke: linux smoke-rollback commit failed." }
        $sha3 = (& git -C $source rev-parse HEAD).Trim()

        $failedUpdate = Invoke-SmokeScript -ScriptPath (Join-Path $consumer "build/update.ps1") `
            -Arguments @("-Source", $source, "-Sha", $sha3, "-Target", $consumer, "-ForceSmoke") `
            -WorkingDirectory $consumer -Label "linux smoke rollback update" -ExpectFailure
        if ($failedUpdate.ExitCode -eq 0) {
            Fail-Smoke "update reported success while the fake smoke build failed."
        }
        if (Test-Path -LiteralPath (Join-Path $consumer "build/rollback-probe.txt")) {
            Fail-Smoke "smoke failure left a file introduced by the new lock."
        }
        if ((Get-FileHash -LiteralPath (Join-Path $consumer "build/plutonium.lock") -Algorithm SHA256).Hash -ne $lockBeforeRollback) {
            Fail-Smoke "smoke failure did not restore the previous lock."
        }
        if ((Get-FileHash -LiteralPath (Join-Path $consumer "build/build.sh") -Algorithm SHA256).Hash -ne $buildShBeforeRollback) {
            Fail-Smoke "smoke failure did not restore a managed file."
        }
    }

    if (-not $SkipBuild) {
        $feed = Join-Path $temporary "feed"
        New-Item -ItemType Directory -Path $feed -Force | Out-Null
        . (Join-Path $RepositoryRoot "scaffold/build/bootstrap.ps1")
        $toolchain = Resolve-PlutoniumDotNet `
            -GlobalJson (Join-Path $RepositoryRoot "global.json") `
            -CacheDirectory (Join-Path $consumer "build/temp") `
            -LockFile (Join-Path $RepositoryRoot "scaffold/build/plutonium.lock")
        & $toolchain.Exe pack (Join-Path $RepositoryRoot "src/PlutoniumBuild.Package/PlutoniumBuild.Package.csproj") `
            --configuration Release --output $feed --nologo
        if ($LASTEXITCODE -ne 0) { throw "powershell-smoke: local package pack failed." }

        $feedPath = $feed.Replace("\", "/")
        $nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feedPath" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@
        [System.IO.File]::WriteAllText((Join-Path $consumer "NuGet.config"), $nugetConfig)

        $build = Invoke-SmokeScript -ScriptPath (Join-Path $consumer "build.ps1") `
            -Arguments @("Test") -WorkingDirectory $consumer -Label "build.ps1 Test"
        if ($build.ExitCode -ne 0) { Fail-Smoke "build.ps1 Test failed." }

        $lockBeforeRollback = (Get-FileHash -LiteralPath (Join-Path $consumer "build/plutonium.lock") -Algorithm SHA256).Hash
        $buildShBeforeRollback = (Get-FileHash -LiteralPath (Join-Path $consumer "build/build.sh") -Algorithm SHA256).Hash
        Add-Content -LiteralPath $preservedProgram -Value "#error smoke rollback probe"
        Set-Content -LiteralPath (Join-Path $source "scaffold/build/rollback-probe.txt") -Value "probe"
        Add-SourceManagedEntry -SourceRoot $source -Relative "build/rollback-probe.txt"
        & git -C $source add -A
        & git -C $source commit -qm smoke-rollback
        if ($LASTEXITCODE -ne 0) { throw "powershell-smoke: smoke-rollback commit failed." }
        $sha3 = (& git -C $source rev-parse HEAD).Trim()

        $failedUpdate = Invoke-SmokeScript -ScriptPath (Join-Path $consumer "build/update.ps1") `
            -Arguments @("-Source", $source, "-Sha", $sha3, "-Target", $consumer, "-ForceSmoke") `
            -WorkingDirectory $consumer -Label "smoke rollback update" -ExpectFailure
        if ($failedUpdate.ExitCode -eq 0) {
            Fail-Smoke "update reported success while the smoke build failed."
        }
        if (Test-Path -LiteralPath (Join-Path $consumer "build/rollback-probe.txt")) {
            Fail-Smoke "smoke failure left a file introduced by the new lock."
        }
        if ((Get-FileHash -LiteralPath (Join-Path $consumer "build/plutonium.lock") -Algorithm SHA256).Hash -ne $lockBeforeRollback) {
            Fail-Smoke "smoke failure did not restore the previous lock."
        }
        if ((Get-FileHash -LiteralPath (Join-Path $consumer "build/build.sh") -Algorithm SHA256).Hash -ne $buildShBeforeRollback) {
            Fail-Smoke "smoke failure did not restore a managed file."
        }
    }
    else {
        Write-Host "powershell-smoke: build.ps1 execution skipped by -SkipBuild."
    }
}
finally {
    if ($KeepTemp) {
        Write-Host "powershell-smoke: kept $temporary"
    }
    else {
        Remove-Item -LiteralPath $temporary -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($failures -ne 0) {
    throw "powershell-smoke: $failures failure(s)."
}

Write-Host "powershell-smoke: ok"

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
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path,
    [string]$BootstrapScript = (Join-Path $RepositoryRoot "scaffold/build/bootstrap.ps1")
)

$ErrorActionPreference = "Stop"
$ConfirmPreference = "None"

$isWindowsHost = $PSVersionTable.PSEdition -eq "Desktop" -or $env:OS -eq "Windows_NT"

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

function Write-LfLines {
    param([string]$Path, [System.Collections.Generic.IEnumerable[string]]$Lines)
    $text = [string]::Join("`n", [string[]]$Lines)
    if ($text.Length -gt 0) {
        $text += "`n"
    }
    $encoding = New-Object System.Text.UTF8Encoding -ArgumentList $false
    [System.IO.File]::WriteAllText($Path, $text, $encoding)
}

function Add-LfLine {
    param([string]$Path, [string]$Value)
    [System.IO.File]::AppendAllText($Path, $Value + "`n")
}

function Set-LfFile {
    param([string]$Path, [string]$Value)
    [System.IO.File]::WriteAllText($Path, $Value + "`n")
}

function Set-CrlfFile {
    param([string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $lf = New-Object System.Collections.Generic.List[byte]
    foreach ($value in $bytes) {
        if ($value -ne 13) {
            $lf.Add($value)
        }
    }
    $crlf = New-Object System.Collections.Generic.List[byte]
    foreach ($value in $lf) {
        if ($value -eq 10) {
            $crlf.Add(13)
        }
        $crlf.Add($value)
    }
    [System.IO.File]::WriteAllBytes($Path, $crlf.ToArray())
}

function Assert-LfFile {
    param([string]$Path, [string]$Label)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Fail-Smoke "$Label is missing: $Path"
        return
    }
    $text = [System.IO.File]::ReadAllText($Path)
    if ($text.Contains("`r")) {
        Fail-Smoke "$Label contains a carriage return."
    }
}

function Assert-CrlfFile {
    param([string]$Path, [string]$Label)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Fail-Smoke "$Label is missing: $Path"
        return
    }
    $text = [System.IO.File]::ReadAllText($Path)
    if (-not $text.Contains("`r`n")) {
        Fail-Smoke "$Label does not contain CRLF line endings."
    }
}

function Get-RawBlobHash {
    param([string]$Repository, [string]$Path)
    $formatOutput = (& git -C $Repository rev-parse --show-object-format 2>$null)
    $format = if ($null -eq $formatOutput) { "" } else { ([string]$formatOutput).Trim() }
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($format)) {
        $format = "sha1"
    }
    $global:LASTEXITCODE = 0
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $header = [System.Text.Encoding]::ASCII.GetBytes("blob " + $bytes.Length + [char]0)
    $payload = New-Object byte[] ($header.Length + $bytes.Length)
    [System.Array]::Copy($header, 0, $payload, 0, $header.Length)
    [System.Array]::Copy($bytes, 0, $payload, $header.Length, $bytes.Length)
    if ($format -eq "sha256") {
        $algorithm = [System.Security.Cryptography.SHA256]::Create()
    }
    else {
        $algorithm = [System.Security.Cryptography.SHA1]::Create()
    }
    try {
        $hash = $algorithm.ComputeHash($payload)
    }
    finally {
        $algorithm.Dispose()
    }
    return ([System.BitConverter]::ToString($hash).Replace("-", "").ToLowerInvariant())
}

function Assert-CrlfCommitted {
    param([string]$Repository, [string]$Relative, [string]$Label)
    $worktree = Join-Path $Repository ($Relative.Replace("/", [System.IO.Path]::DirectorySeparatorChar))
    Assert-CrlfFile -Path $worktree -Label $Label
    $worktreeHash = Get-RawBlobHash -Repository $Repository -Path $worktree
    $committedOutput = (& git -C $Repository rev-parse "HEAD:$Relative" 2>$null)
    if ($LASTEXITCODE -ne 0 -or $null -eq $committedOutput) {
        Fail-Smoke "$Label could not resolve the committed blob."
        return
    }
    $committedHash = ([string]$committedOutput).Trim()
    if ($worktreeHash -ne $committedHash) {
        Fail-Smoke "$Label commit does not contain the CRLF worktree bytes."
    }
}

function Get-InstalledPath {
    param([string]$Consumer, [string]$Relative)
    if ($Relative.StartsWith("build/", [System.StringComparison]::Ordinal)) {
        return Join-Path (Join-Path $Consumer "build") ($Relative.Substring(6).Replace("/", [System.IO.Path]::DirectorySeparatorChar))
    }
    return Join-Path $Consumer $Relative
}

function Assert-ManagedHashes {
    param([string]$Consumer, [string]$Label)
    $lockPath = Join-Path $Consumer "build/plutonium.lock"
    foreach ($line in [System.IO.File]::ReadAllLines($lockPath)) {
        if ($line -notmatch "^\s*managed\s+(\S+)\s+([0-9a-fA-F]{64})\s*$") { continue }
        $relative = $Matches[1]
        $expected = $Matches[2].ToLowerInvariant()
        $installed = Get-InstalledPath -Consumer $Consumer -Relative $relative
        if (-not (Test-Path -LiteralPath $installed -PathType Leaf)) {
            Fail-Smoke "$Label is missing managed file $relative."
            continue
        }
        $actual = (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $expected) {
            Fail-Smoke "$Label has a managed hash mismatch for $relative."
        }
    }
}

function Assert-PreserveEntries {
    param([string]$Consumer, [string]$Label)
    $lockPath = Join-Path $Consumer "build/plutonium.lock"
    $lines = [System.IO.File]::ReadAllLines($lockPath)
    foreach ($relative in @("build/build.cs", "build/build.slnx", "build/Directory.Build.props", "build/Directory.Build.targets")) {
        $found = $false
        foreach ($line in $lines) {
            if ($line -match "^\s*preserve\s+(\S+)\s*$" -and $Matches[1] -eq $relative) {
                $found = $true
                break
            }
        }
        if (-not $found) {
            Fail-Smoke "$Label lost the preserve entry for $relative."
            continue
        }
        $installed = Get-InstalledPath -Consumer $Consumer -Relative $relative
        if (-not (Test-Path -LiteralPath $installed -PathType Leaf)) {
            Fail-Smoke "$Label is missing preserved file $relative."
        }
    }
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
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try {
            $output = & $hostExecutable -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments 2>&1
            $exitCode = $LASTEXITCODE
            $global:LASTEXITCODE = 0
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
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
    Write-LfLines -Path $lockPath -Lines $rewritten
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
    [System.IO.File]::AppendAllText($lockPath, "managed $Relative $hash`n")
}

function New-FakeProbeExecutable {
    param(
        [string]$Directory,
        [string]$Name,
        [string]$VersionLine,
        [int]$ExitCode
    )
    if ($script:isWindowsHost) {
        $path = Join-Path $Directory ($Name + ".cmd")
        $content = "@echo off`r`necho $VersionLine`r`nping -n 2 127.0.0.1 >nul`r`necho after-line`r`nexit /b $ExitCode`r`n"
        [System.IO.File]::WriteAllText($path, $content)
        return $path
    }

    $path = Join-Path $Directory ($Name + ".sh")
    $content = "#!/usr/bin/env bash`necho $VersionLine`nsleep 0.3`necho after-line`nexit $ExitCode`n"
    [System.IO.File]::WriteAllText($path, $content)
    & chmod 755 $path
    if ($LASTEXITCODE -ne 0) {
        throw "powershell-smoke: could not mark the fake probe executable."
    }
    return $path
}

$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ("plutonium-ps-smoke-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temporary -Force | Out-Null
$source = Join-Path $temporary "source"
$consumer = Join-Path $temporary "consumer"

try {
    if (-not (Test-Path -LiteralPath $BootstrapScript -PathType Leaf)) {
        throw "powershell-smoke: bootstrap script not found: $BootstrapScript"
    }
    . $BootstrapScript

    # Get-PlutoniumProbe must capture the native output and exit code without
    # piping the live process into Select-Object -First, which force-closes the
    # process on Windows PowerShell 5.1 and records a false failure. Clearing
    # LASTEXITCODE first keeps a stale value from masking a missed capture.
    $probeSettings = Get-PlutoniumSdkSettings -GlobalJson (Join-Path $RepositoryRoot "global.json")
    $probeDirectory = Join-Path $temporary "probe"
    New-Item -ItemType Directory -Path $probeDirectory -Force | Out-Null

    $successProbe = New-FakeProbeExecutable -Directory $probeDirectory -Name "probe-success" `
        -VersionLine $probeSettings.Version -ExitCode 0
    $global:LASTEXITCODE = $null
    $successDiagnostics = $null
    $successReported = Get-PlutoniumProbe -Executable $successProbe -ProbeDirectory $probeDirectory `
        -PinnedVersion $probeSettings.Version -RollForward $probeSettings.RollForward `
        -AllowPrerelease $probeSettings.AllowPrerelease -Diagnostics ([ref]$successDiagnostics)
    if ($successReported -ne $probeSettings.Version) {
        Fail-Smoke "Get-PlutoniumProbe did not return the reported SDK version (got '$successReported')."
    }
    if ($null -eq $successDiagnostics -or $successDiagnostics.ExitCode -ne 0 -or
        $successDiagnostics.Reported -ne $probeSettings.Version) {
        Fail-Smoke "Get-PlutoniumProbe reported a failure diagnostic for a successful probe."
    }

    $exitProbe = New-FakeProbeExecutable -Directory $probeDirectory -Name "probe-exit" `
        -VersionLine $probeSettings.Version -ExitCode 3
    $global:LASTEXITCODE = $null
    $exitDiagnostics = $null
    $exitReported = Get-PlutoniumProbe -Executable $exitProbe -ProbeDirectory $probeDirectory `
        -PinnedVersion $probeSettings.Version -RollForward $probeSettings.RollForward `
        -AllowPrerelease $probeSettings.AllowPrerelease -Diagnostics ([ref]$exitDiagnostics)
    if ($null -ne $exitReported) {
        Fail-Smoke "Get-PlutoniumProbe accepted a nonzero probe exit."
    }
    if ($null -eq $exitDiagnostics -or $exitDiagnostics.ExitCode -ne 3 -or
        $exitDiagnostics.Reported -ne $probeSettings.Version) {
        Fail-Smoke "Get-PlutoniumProbe did not capture the nonzero probe exit code and reported version."
    }

    $wrongProbe = New-FakeProbeExecutable -Directory $probeDirectory -Name "probe-wrong" `
        -VersionLine "0.0.1" -ExitCode 0
    $global:LASTEXITCODE = $null
    $wrongDiagnostics = $null
    $wrongReported = Get-PlutoniumProbe -Executable $wrongProbe -ProbeDirectory $probeDirectory `
        -PinnedVersion $probeSettings.Version -RollForward $probeSettings.RollForward `
        -AllowPrerelease $probeSettings.AllowPrerelease -Diagnostics ([ref]$wrongDiagnostics)
    if ($null -ne $wrongReported) {
        Fail-Smoke "Get-PlutoniumProbe accepted a version that does not satisfy the pin."
    }
    if ($null -eq $wrongDiagnostics -or $wrongDiagnostics.ExitCode -ne 0 -or
        $wrongDiagnostics.Reported -ne "0.0.1") {
        Fail-Smoke "Get-PlutoniumProbe did not capture the unsatisfied reported version."
    }

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
    & git -C $propsSource config user.email smoke@example.test
    & git -C $propsSource config user.name Smoke
    $propsPath = Join-Path $propsSource "src/Directory.Build.props"
    $propsText = [System.IO.File]::ReadAllText($propsPath)
    [System.IO.File]::WriteAllText($propsPath, $propsText.Replace("<Version>0.1.0</Version>", "<Version>9.9.9</Version>"))
    & git -C $propsSource add -A
    & git -C $propsSource commit -qm props-mismatch
    if ($LASTEXITCODE -ne 0) { throw "powershell-smoke: props mismatch commit failed." }
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

    # The build app resolves HEAD for its run identity, so the consumer must be
    # a committed repository before the build step runs.
    & git -C $consumer add -A
    & git -C $consumer commit -qm "consumer baseline"
    if ($LASTEXITCODE -ne 0) { throw "powershell-smoke: consumer commit failed." }

    Add-Content -LiteralPath (Join-Path $consumer "build/build.cs") -Value "// consumer sentinel"
    Add-Content -LiteralPath (Join-Path $consumer "build/Program.cs") -Value "// consumer sentinel"
    $preservedProgram = Join-Path $consumer "build/Program.cs"
    $preservedHash = (Get-FileHash -LiteralPath $preservedProgram -Algorithm SHA256).Hash.ToLowerInvariant()

    # Add preserve drift to the source: Program.cs becomes preserve-only in
    # effect, and its managed hash must follow the installed copy on update.
    Add-LfLine -Path (Join-Path $source "scaffold/build/build.sh") -Value "# source marker"
    Add-LfLine -Path (Join-Path $source "scaffold/build/Program.cs") -Value "// source marker"
    $sourceLock = Join-Path $source "scaffold/build/plutonium.lock"
    $sourceLockText = [System.IO.File]::ReadAllText($sourceLock)
    if ($sourceLockText -notmatch "(?m)^preserve build/Program\.cs\s*$") {
        [System.IO.File]::AppendAllText($sourceLock, "preserve build/Program.cs`n")
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
    if (-not (Select-String -LiteralPath (Join-Path $consumer "build/build.cs") -Pattern "// consumer sentinel" -Quiet)) {
        Fail-Smoke "update overwrote a preserved build definition."
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

    # A Windows checkout can hand the installer a CRLF source tree and a CRLF
    # installed lock. Both must normalize to LF before hashing or parsing.
    $crlfSource = Join-Path $temporary "crlf-source"
    & git clone -q $source $crlfSource
    if ($LASTEXITCODE -ne 0) { throw "powershell-smoke: could not clone the CRLF source." }
    & git -C $crlfSource config user.email smoke@example.test
    & git -C $crlfSource config user.name Smoke
    & git -C $crlfSource config core.autocrlf false
    # Keep the committed blobs CRLF even if the source tree carries eol rules:
    # info/attributes outranks .gitattributes, and a tree without eol rules
    # makes the installer's fetch checkout deliver the CRLF bytes unchanged.
    $crlfAttributes = Join-Path $crlfSource ".gitattributes"
    if (Test-Path -LiteralPath $crlfAttributes -PathType Leaf) {
        Remove-Item -LiteralPath $crlfAttributes -Force
    }
    $infoAttributes = Join-Path $crlfSource ".git/info/attributes"
    [System.IO.File]::WriteAllText($infoAttributes, "* -text`n")
    Get-ChildItem -LiteralPath (Join-Path $crlfSource "scaffold") -Recurse -File -Force | ForEach-Object {
        Set-CrlfFile -Path $_.FullName
    }
    & git -C $crlfSource add -A
    & git -C $crlfSource commit -qm crlf-source
    if ($LASTEXITCODE -ne 0) { throw "powershell-smoke: CRLF source commit failed." }
    Assert-CrlfCommitted -Repository $crlfSource -Relative "scaffold/build/build.sh" -Label "CRLF source build.sh"
    Assert-CrlfCommitted -Repository $crlfSource -Relative "scaffold/build/plutonium.lock" -Label "CRLF source lock"
    $crlfSha1 = (& git -C $crlfSource rev-parse HEAD).Trim()

    $crlfConsumer = Join-Path $temporary "crlf-consumer"
    New-Item -ItemType Directory -Path $crlfConsumer -Force | Out-Null
    & git -C $crlfConsumer init -q
    & git -C $crlfConsumer config user.email smoke@example.test
    & git -C $crlfConsumer config user.name Smoke
    $crlfInstall = Invoke-SmokeScript -ScriptPath (Join-Path $RepositoryRoot "init.ps1") `
        -Arguments @("-Source", $crlfSource, "-Sha", $crlfSha1, "-Target", $crlfConsumer) `
        -WorkingDirectory $RepositoryRoot -Label "CRLF init"
    if ($crlfInstall.ExitCode -ne 0) { throw "powershell-smoke: CRLF init failed." }
    $crlfLock = Join-Path $crlfConsumer "build/plutonium.lock"
    Assert-LfFile -Path $crlfLock -Label "CRLF init lock"
    Assert-LfFile -Path (Join-Path $crlfConsumer "build/build.sh") -Label "CRLF init build.sh"
    Assert-LfFile -Path (Join-Path $crlfConsumer "build/build.csproj") -Label "CRLF init build.csproj"
    Assert-LfFile -Path (Join-Path $crlfConsumer "build.sh") -Label "CRLF init root build.sh"
    if ((Get-Content -LiteralPath $crlfLock -Raw) -notmatch "scaffold_commit=$crlfSha1") {
        Fail-Smoke "CRLF init lock does not record the requested commit."
    }
    Assert-PreserveEntries -Consumer $crlfConsumer -Label "CRLF init"
    Assert-ManagedHashes -Consumer $crlfConsumer -Label "CRLF init"

    $crlfBuildSh = Join-Path $crlfSource "scaffold/build/build.sh"
    $crlfBuildLines = New-Object System.Collections.Generic.List[string]
    $crlfBuildLines.AddRange([System.IO.File]::ReadAllLines($crlfBuildSh))
    $crlfBuildLines.Add("# crlf update marker")
    Write-LfLines -Path $crlfBuildSh -Lines $crlfBuildLines
    Set-SourceManagedHash -SourceRoot $crlfSource -Relative "build/build.sh"
    Set-CrlfFile -Path $crlfBuildSh
    Set-CrlfFile -Path (Join-Path $crlfSource "scaffold/build/plutonium.lock")
    & git -C $crlfSource add -A
    & git -C $crlfSource commit -qm crlf-update
    if ($LASTEXITCODE -ne 0) { throw "powershell-smoke: CRLF update commit failed." }
    Assert-CrlfCommitted -Repository $crlfSource -Relative "scaffold/build/build.sh" -Label "CRLF update build.sh"
    Assert-CrlfCommitted -Repository $crlfSource -Relative "scaffold/build/plutonium.lock" -Label "CRLF update lock"
    $crlfSha2 = (& git -C $crlfSource rev-parse HEAD).Trim()

    Set-CrlfFile -Path $crlfLock
    $crlfUpdate = Invoke-SmokeScript -ScriptPath (Join-Path $crlfConsumer "build/update.ps1") `
        -Arguments @("-Source", $crlfSource, "-Sha", $crlfSha2, "-Target", $crlfConsumer, "-NoSmoke") `
        -WorkingDirectory $crlfConsumer -Label "CRLF update"
    if ($crlfUpdate.ExitCode -ne 0) { throw "powershell-smoke: CRLF update failed." }
    Assert-LfFile -Path $crlfLock -Label "CRLF update lock"
    Assert-LfFile -Path (Join-Path $crlfConsumer "build/build.sh") -Label "CRLF update build.sh"
    if (-not (Select-String -LiteralPath (Join-Path $crlfConsumer "build/build.sh") -Pattern "# crlf update marker" -Quiet)) {
        Fail-Smoke "CRLF update did not install the managed build.sh change."
    }
    if ((Get-Content -LiteralPath $crlfLock -Raw) -notmatch "scaffold_commit=$crlfSha2") {
        Fail-Smoke "CRLF update lock does not record the requested commit."
    }
    Assert-PreserveEntries -Consumer $crlfConsumer -Label "CRLF update"
    Assert-ManagedHashes -Consumer $crlfConsumer -Label "CRLF update"

    $crlfJournal = Get-ChildItem -LiteralPath (Join-Path $crlfConsumer "build/temp/update") -Filter "journal.txt" -Recurse | Select-Object -First 1
    if ($null -eq $crlfJournal) {
        Fail-Smoke "CRLF update did not journal the previous commit."
    }
    elseif ((Get-Content -LiteralPath $crlfJournal.FullName -Raw) -notmatch "(?m)^from_commit=$crlfSha1\s*$") {
        Fail-Smoke "CRLF update did not read the previous commit from the CRLF lock."
    }

    Set-CrlfFile -Path $crlfLock
    $crlfAgain = Invoke-SmokeScript -ScriptPath (Join-Path $crlfConsumer "build/update.ps1") `
        -Arguments @("-Source", $crlfSource, "-Sha", $crlfSha2, "-Target", $crlfConsumer, "-NoSmoke") `
        -WorkingDirectory $crlfConsumer -Label "CRLF idempotent update"
    if ($crlfAgain.ExitCode -ne 0 -or $crlfAgain.Output -notmatch "already at") {
        Fail-Smoke "update was not idempotent at the same commit with a CRLF lock."
    }

    # On Linux, a fake cached toolchain can force the smoke build to fail and
    # prove that update.ps1 removes files introduced by the new lock.
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
        Set-LfFile -Path (Join-Path $source "scaffold/build/rollback-probe.txt") -Value "probe"
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
        Set-LfFile -Path (Join-Path $source "scaffold/build/rollback-probe.txt") -Value "probe"
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
    Write-Host "powershell-smoke: $failures failure(s)." -ForegroundColor Red
    exit 1
}

Write-Host "powershell-smoke: ok"
exit 0

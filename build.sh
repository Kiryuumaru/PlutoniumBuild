#!/usr/bin/env bash
# Maintainer runner: resolves the pinned SDK through the same verified bootstrap
# the scaffold ships, then builds the solution and runs every test.

set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)

# shellcheck source=scaffold/build/bootstrap.sh
. "$ROOT/scaffold/build/bootstrap.sh"

plutonium_bootstrap \
    "$ROOT/global.json" \
    "$ROOT/.plutonium/temp" \
    "$ROOT/scaffold/build/plutonium.lock"

cd "$ROOT"

"$PLUTONIUM_DOTNET_EXE" build PlutoniumBuild.sln \
    -nodeReuse:false -p:UseSharedCompilation=false -nologo -clp:NoSummary --verbosity quiet

# The test matrix targets net8.0/net9.0/net10.0. LatestMajor lets every target
# execute on the pinned SDK's runtime when the host has no older runtimes.
DOTNET_ROLL_FORWARD=LatestMajor \
    "$PLUTONIUM_DOTNET_EXE" test PlutoniumBuild.sln --no-build -nologo --verbosity minimal

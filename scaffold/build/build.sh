#!/usr/bin/env bash
# Consumer build launcher. Resolves the SDK pinned in build/global.json through
# the verified bootstrap, builds build/build.csproj, and runs it with every
# argument passed through unchanged.

set -euo pipefail

BUILD_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
REPO_ROOT=$(dirname "$BUILD_DIR")

# shellcheck source=bootstrap.sh
. "$BUILD_DIR/bootstrap.sh"

plutonium_bootstrap "$BUILD_DIR/global.json" "$BUILD_DIR/temp" "$BUILD_DIR/plutonium.lock"

if [ ! -f "$BUILD_DIR/build.csproj" ]; then
    echo "PlutoniumBuild: build/build.csproj is missing; run build/update.sh or restore the scaffold." >&2
    exit 1
fi

cd "$REPO_ROOT"

"$PLUTONIUM_DOTNET_EXE" build "$BUILD_DIR/build.csproj" \
    -nodeReuse:false -p:UseSharedCompilation=false -nologo -clp:NoSummary --verbosity quiet

exec "$PLUTONIUM_DOTNET_EXE" run --project "$BUILD_DIR/build.csproj" --no-build -- "$@"

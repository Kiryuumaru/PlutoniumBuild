#!/usr/bin/env bash
# PlutoniumBuild consumer runner template: uses a suitable .NET SDK when one is
# on PATH, otherwise bootstraps the SDK pinned in global.json into .plutonium/temp
# (repo-local, --no-path), then quiet-builds your build app and runs it with every
# argument passed through. Copy this file next to build.ps1/build.cmd in your
# repository root.

set -eo pipefail

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)

###########################################################################
# CONFIGURATION
###########################################################################

# The one line to edit: your build app project, relative to this script (or absolute).
# Setting PLUTONIUM_APP_PROJECT in the environment overrides it for one-off runs.
APP_PROJECT="${PLUTONIUM_APP_PROJECT:-build/_build.csproj}"

TEMP_DIRECTORY="$SCRIPT_DIR/.plutonium/temp"
DOTNET_GLOBAL_FILE="$SCRIPT_DIR/global.json"
DOTNET_INSTALL_URL="https://dot.net/v1/dotnet-install.sh"
DOTNET_CHANNEL="STS"

# Quiet, deterministic CLI behavior. LatestMajor keeps your build app runnable on
# machines where only the bootstrapped SDK's runtime exists.
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_MULTILEVEL_LOOKUP=0
export DOTNET_NOLOGO=1
export DOTNET_ROLL_FORWARD=LatestMajor

###########################################################################
# EXECUTION
###########################################################################

# Reads sdk.version from a global.json. Prints nothing when the key is absent.
# Lines are joined first so a single-line file with later "version" keys still
# yields the sdk block's value.
ReadGlobalJsonSdkVersion() {
    sed -n '/"sdk"[[:space:]]*:/,/^[[:space:]]*}/p' "$1" \
        | tr -d '\n' \
        | sed -n 's/.*"sdk"[[:space:]]*:[[:space:]]*{[^}]*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p'
}

# Run from the repository root so SDK resolution, the build app, and relative
# paths behave the same no matter where this script is invoked from.
cd "$SCRIPT_DIR"

case "$APP_PROJECT" in
    /*) APP_PROJECT_FILE="$APP_PROJECT" ;;
    [A-Za-z]:[/\\]*) APP_PROJECT_FILE="$APP_PROJECT" ;;
    *)  APP_PROJECT_FILE="$SCRIPT_DIR/$APP_PROJECT" ;;
esac

# A runnable dotnet whose `--version` succeeds is the de-facto global.json gate:
# no explicit version compare, only bootstrap when nothing usable is present.
if [ -x "$(command -v dotnet)" ] && dotnet --version >/dev/null 2>&1; then
    DOTNET_EXE="$(command -v dotnet)"
else
    if ! command -v curl >/dev/null 2>&1; then
        echo "PlutoniumBuild: curl is required to download the .NET installer." >&2
        exit 1
    fi

    DOTNET_INSTALL_FILE="$TEMP_DIRECTORY/dotnet-install.sh"
    mkdir -p "$TEMP_DIRECTORY"
    curl -Lsfo "$DOTNET_INSTALL_FILE" "$DOTNET_INSTALL_URL"
    chmod +x "$DOTNET_INSTALL_FILE"

    DOTNET_VERSION=""
    if [ -f "$DOTNET_GLOBAL_FILE" ]; then
        DOTNET_VERSION=$(ReadGlobalJsonSdkVersion "$DOTNET_GLOBAL_FILE")
    fi

    DOTNET_DIRECTORY="$TEMP_DIRECTORY/dotnet-unix"
    if [ -n "$DOTNET_VERSION" ]; then
        "$DOTNET_INSTALL_FILE" --install-dir "$DOTNET_DIRECTORY" --version "$DOTNET_VERSION" --no-path
    else
        echo "PlutoniumBuild: no SDK version pinned; installing the latest $DOTNET_CHANNEL SDK."
        "$DOTNET_INSTALL_FILE" --install-dir "$DOTNET_DIRECTORY" --channel "$DOTNET_CHANNEL" --no-path
    fi

    DOTNET_EXE="$DOTNET_DIRECTORY/dotnet"
    PATH="$DOTNET_DIRECTORY:$PATH"
    export PATH
fi

export DOTNET_EXE
echo "PlutoniumBuild: using .NET SDK $("$DOTNET_EXE" --version) ($DOTNET_EXE)"
echo "PlutoniumBuild: running build app $APP_PROJECT_FILE"

"$DOTNET_EXE" build "$APP_PROJECT_FILE" /nodeReuse:false /p:UseSharedCompilation=false -nologo -clp:NoSummary --verbosity quiet
"$DOTNET_EXE" run --project "$APP_PROJECT_FILE" --no-build -- "$@"

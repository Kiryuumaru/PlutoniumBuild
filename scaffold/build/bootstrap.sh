#!/usr/bin/env bash
# Canonical verified .NET SDK bootstrap, shared by the maintainer runner and the
# scaffold. It never installs globally, never edits PATH outside the current
# process, and never executes a downloaded file that has not matched the pins
# recorded in plutonium.lock. Executable-affecting dotnet and NuGet environment
# variables are cleared before probing, and sdk.rollForward / allowPrerelease
# are validated from global.json before any SDK is accepted.
#
# Callers set the three inputs and read PLUTONIUM_DOTNET_EXE afterwards:
#   plutonium_bootstrap <global.json> <cache-directory> <plutonium.lock>

_plutonium_fail() {
    echo "PlutoniumBuild: $*" >&2
    return 1
}

_plutonium_sanitize_environment() {
    # A dotnet host or restore can be steered by these variables. Clear them
    # before probing, installing, or handing the toolchain to a caller.
    unset DOTNET_STARTUP_HOOKS DOTNET_ADDITIONAL_DEPS DOTNET_SHARED_STORE \
        NUGET_PACKAGES NUGET_HTTP_CACHE_PATH DOTNET_HOST_PATH DOTNET_ROOT \
        DOTNET_ROLL_FORWARD
}

PLUTONIUM_BOOTSTRAP_TEMP="${PLUTONIUM_BOOTSTRAP_TEMP:-}"

_plutonium_cleanup() {
    if [ -n "${PLUTONIUM_BOOTSTRAP_TEMP:-}" ] && [ -d "$PLUTONIUM_BOOTSTRAP_TEMP" ]; then
        rm -rf -- "$PLUTONIUM_BOOTSTRAP_TEMP"
    fi
    PLUTONIUM_BOOTSTRAP_TEMP=""
}

_plutonium_abspath() {
    local candidate="$1"
    if [ -L "$candidate" ]; then
        local target
        target=$(readlink "$candidate" 2>/dev/null || true)
        if [ -n "$target" ]; then
            case "$target" in
                /*) _plutonium_abspath "$target" ;;
                *) _plutonium_abspath "$(dirname "$candidate")/$target" ;;
            esac
            return
        fi
    fi

    case "$candidate" in
        /*) printf '%s\n' "$candidate" ;;
        *) printf '%s/%s\n' "$(cd "$(dirname "$candidate")" && pwd -P)" "$(basename "$candidate")" ;;
    esac
}

_plutonium_sha256_file() {
    local file="$1"
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$file" | awk '{print $1}'
    elif command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$file" | awk '{print $1}'
    elif command -v openssl >/dev/null 2>&1; then
        openssl dgst -sha256 "$file" | sed 's/^.*= //'
    else
        return 1
    fi
}

_plutonium_sha512_file() {
    local file="$1"
    if command -v sha512sum >/dev/null 2>&1; then
        sha512sum "$file" | awk '{print $1}'
    elif command -v shasum >/dev/null 2>&1; then
        shasum -a 512 "$file" | awk '{print $1}'
    elif command -v openssl >/dev/null 2>&1; then
        openssl dgst -sha512 "$file" | sed 's/^.*= //'
    else
        return 1
    fi
}

_plutonium_lock_get() {
    local file="$1" key="$2"
    [ -f "$file" ] || return 1
    awk -v key="$key" '
        /^[[:space:]]*#/ { next }
        {
            line = $0
            sub(/^[[:space:]]+/, "", line)
            prefix = key "="
            if (index(line, prefix) == 1) {
                value = substr(line, length(prefix) + 1)
                sub(/[[:space:]]+$/, "", value)
                print value
                exit
            }
        }
    ' "$file"
}

_plutonium_read_sdk_version() {
    local file="$1"
    [ -f "$file" ] || return 1
    sed -n '/"sdk"[[:space:]]*:/,/^[[:space:]]*}/p' "$file" \
        | tr -d '\n\r' \
        | sed -n 's/.*"sdk"[[:space:]]*:[[:space:]]*{[^}]*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p'
}

_plutonium_read_sdk_field() {
    local file="$1" key="$2"
    [ -f "$file" ] || return 1
    sed -n '/"sdk"[[:space:]]*:/,/^[[:space:]]*}/p' "$file" \
        | tr -d '\n\r' \
        | sed -n "s/.*\"$key\"[[:space:]]*:[[:space:]]*\"\\([^\"]*\\)\".*/\\1/p"
}

_plutonium_read_sdk_bool() {
    local file="$1" key="$2"
    [ -f "$file" ] || return 1
    sed -n '/"sdk"[[:space:]]*:/,/^[[:space:]]*}/p' "$file" \
        | tr -d '\n\r' \
        | sed -n "s/.*\"$key\"[[:space:]]*:[[:space:]]*\\(true\\|false\\).*/\\1/p"
}

_plutonium_read_sdk_settings() {
    local file="$1" version roll_forward allow_prerelease
    version=$(_plutonium_read_sdk_field "$file" version)
    roll_forward=$(_plutonium_read_sdk_field "$file" rollForward)
    allow_prerelease=$(_plutonium_read_sdk_bool "$file" allowPrerelease)
    [ -n "$version" ] && [ -n "$roll_forward" ] && [ -n "$allow_prerelease" ] || return 1
    case "$roll_forward" in
        disable|patch|feature|minor|major|latestPatch|latestFeature|latestMinor|latestMajor) ;;
        *) return 1 ;;
    esac
    case "$allow_prerelease" in
        true|false) ;;
        *) return 1 ;;
    esac
    case "$version" in
        *-*) [ "$allow_prerelease" = true ] || return 1 ;;
    esac
    printf '%s\n%s\n%s\n' "$version" "$roll_forward" "$allow_prerelease"
}

_plutonium_version_satisfies() {
    local actual="$1" pinned="$2" roll_forward="$3" allow_prerelease="$4"
    local actual_core pinned_core
    actual_core=${actual%%-*}
    pinned_core=${pinned%%-*}

    case "$actual" in
        *-*) [ "$allow_prerelease" = true ] || return 1 ;;
    esac
    [ "$actual" = "$pinned" ] && return 0

    local actual_major actual_minor actual_patch pinned_major pinned_minor pinned_patch
    IFS=. read -r actual_major actual_minor actual_patch <<< "$actual_core"
    IFS=. read -r pinned_major pinned_minor pinned_patch <<< "$pinned_core"

    [ -n "$actual_major" ] && [ -n "$actual_minor" ] && [ -n "$actual_patch" ] || return 1
    [ -n "$pinned_major" ] && [ -n "$pinned_minor" ] && [ -n "$pinned_patch" ] || return 1
    case "$actual_major$actual_minor$actual_patch$pinned_major$pinned_minor$pinned_patch" in
        *[!0-9]*) return 1 ;;
    esac

    [ $((10#$actual_major)) -ge $((10#$pinned_major)) ] || return 1
    if [ $((10#$actual_major)) -eq $((10#$pinned_major)) ]; then
        [ $((10#$actual_minor)) -ge $((10#$pinned_minor)) ] || return 1
    fi
    if [ $((10#$actual_major)) -eq $((10#$pinned_major)) ] &&
       [ $((10#$actual_minor)) -eq $((10#$pinned_minor)) ]; then
        [ $((10#$actual_patch)) -ge $((10#$pinned_patch)) ] || return 1
    fi

    case "$roll_forward" in
        disable) return 1 ;;
        patch|latestPatch)
            [ $((10#$actual_major)) -eq $((10#$pinned_major)) ] || return 1
            [ $((10#$actual_minor)) -eq $((10#$pinned_minor)) ] || return 1
            [ $((10#$actual_patch / 100)) -eq $((10#$pinned_patch / 100)) ] || return 1
            ;;
        feature|latestFeature)
            [ $((10#$actual_major)) -eq $((10#$pinned_major)) ] || return 1
            [ $((10#$actual_minor)) -eq $((10#$pinned_minor)) ] || return 1
            ;;
        minor|latestMinor)
            [ $((10#$actual_major)) -eq $((10#$pinned_major)) ] || return 1
            ;;
        major|latestMajor) ;;
        *) return 1 ;;
    esac
    return 0
}

_plutonium_probe() {
    local exe="$1" probe_directory="$2" pinned="$3" roll_forward="$4" allow_prerelease="$5"
    [ -x "$exe" ] || return 1
    local reported
    reported=$(cd "$probe_directory" && "$exe" --version 2>/dev/null | tr -d '\r' | head -1)
    [ -n "$reported" ] || return 1
    _plutonium_version_satisfies "$reported" "$pinned" "$roll_forward" "$allow_prerelease" || return 1
    printf '%s\n' "$reported"
}

_plutonium_export_toolchain() {
    local exe="$1"
    local root
    root=$(dirname "$exe")
    export DOTNET_ROOT="$root"
    unset DOTNET_HOST_PATH DOTNET_ROLL_FORWARD
    export DOTNET_MULTILEVEL_LOOKUP=0
    export DOTNET_CLI_TELEMETRY_OPTOUT=1
    export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
    export DOTNET_NOLOGO=1
    case ":$PATH:" in
        *":$root:"*) ;;
        *) export PATH="$root:$PATH" ;;
    esac
    PLUTONIUM_DOTNET_EXE="$exe"
    PLUTONIUM_DOTNET_ROOT="$root"
}

_plutonium_platform() {
    local system architecture os rid
    system=$(uname -s | tr '[:upper:]' '[:lower:]')
    architecture=$(uname -m)
    case "$architecture" in
        x86_64|amd64) architecture="x64" ;;
        aarch64|arm64) architecture="arm64" ;;
        armv7l|armv6l|arm) architecture="arm" ;;
        *) return 1 ;;
    esac

    case "$system" in
        linux*)
            os="linux"
            if { command -v ldd >/dev/null 2>&1 && (ldd --version 2>&1 || true) | grep -qi musl; } ||
               ls /lib/ld-musl-*.so.1 >/dev/null 2>&1; then
                os="linux-musl"
            fi
            rid="$os-$architecture"
            ;;
        darwin*)
            os="osx"
            rid="osx-$architecture"
            ;;
        *)
            return 1
            ;;
    esac

    PLUTONIUM_OS="$os"
    PLUTONIUM_ARCHITECTURE="$architecture"
    PLUTONIUM_RID="$rid"
}

plutonium_bootstrap() {
    local global_json="$1" cache_directory="$2" lock_file="$3"
    local pinned_version roll_forward allow_prerelease settings probe_directory candidate reported
    local installer_repo installer_commit installer_sha installer_url
    local cache_exe install_directory temporary_directory archive_url archive_path
    local expected_sha actual_sha extension

    _plutonium_sanitize_environment
    [ -f "$global_json" ] || { _plutonium_fail "global.json not found at $global_json"; return 1; }
    [ -f "$lock_file" ] || { _plutonium_fail "plutonium.lock not found at $lock_file"; return 1; }

    settings=$(_plutonium_read_sdk_settings "$global_json") || {
        _plutonium_fail "global.json must set a supported sdk.version, sdk.rollForward, and sdk.allowPrerelease; refusing to guess."
        return 1
    }
    pinned_version=$(printf '%s\n' "$settings" | sed -n '1p')
    roll_forward=$(printf '%s\n' "$settings" | sed -n '2p')
    allow_prerelease=$(printf '%s\n' "$settings" | sed -n '3p')

    probe_directory=$(cd "$(dirname "$global_json")" && pwd -P)
    cache_exe="$cache_directory/dotnet-unix/dotnet"
    install_directory="$cache_directory/dotnet-unix"

    if [ -e "$cache_exe" ]; then
        if reported=$(_plutonium_probe "$cache_exe" "$probe_directory" "$pinned_version" "$roll_forward" "$allow_prerelease"); then
            _plutonium_export_toolchain "$cache_exe"
            echo "PlutoniumBuild: using cached .NET SDK $reported ($cache_exe)" >&2
            return 0
        fi
        case "$install_directory" in
            "$cache_directory"/*) rm -rf -- "$install_directory" ;;
            *) _plutonium_fail "refusing to remove an SDK cache outside $cache_directory"; return 1 ;;
        esac
    fi

    candidate=$(command -v dotnet 2>/dev/null || true)
    if [ -n "$candidate" ]; then
        candidate=$(_plutonium_abspath "$candidate")
        if reported=$(_plutonium_probe "$candidate" "$probe_directory" "$pinned_version" "$roll_forward" "$allow_prerelease"); then
            if [ "${PLUTONIUM_REQUIRE_VERIFIED_DOTNET:-0}" = "1" ]; then
                echo "PlutoniumBuild: warning: ignoring version-matching dotnet on PATH ($candidate); PLUTONIUM_REQUIRE_VERIFIED_DOTNET requires the checksum-verified install." >&2
            else
                _plutonium_export_toolchain "$candidate"
                echo "PlutoniumBuild: warning: using version-verified .NET SDK $reported from PATH; the checksum-verified cache was not available ($candidate)" >&2
                return 0
            fi
        fi
    fi

    _plutonium_platform || { _plutonium_fail "unsupported platform for SDK $pinned_version."; return 1; }

    installer_repo=$(_plutonium_lock_get "$lock_file" installer_repo)
    installer_commit=$(_plutonium_lock_get "$lock_file" installer_commit)
    installer_sha=$(_plutonium_lock_get "$lock_file" installer_sh_sha256)
    if [ "$installer_repo" != "dotnet/install-scripts" ]; then
        _plutonium_fail "plutonium.lock must pin installer_repo=dotnet/install-scripts."
        return 1
    fi
    case "$installer_commit" in
        *[!0-9a-fA-F]*) _plutonium_fail "plutonium.lock must pin a full installer_commit SHA."; return 1 ;;
    esac
    if [ "${#installer_commit}" -ne 40 ]; then
        _plutonium_fail "plutonium.lock must pin a full installer_commit SHA."
        return 1
    fi
    if [ "${#installer_sha}" -ne 64 ]; then
        _plutonium_fail "plutonium.lock must pin installer_sh_sha256."
        return 1
    fi

    command -v curl >/dev/null 2>&1 || {
        _plutonium_fail "curl is required to download .NET SDK $pinned_version."
        return 1
    }

    mkdir -p "$cache_directory" || { _plutonium_fail "could not create $cache_directory"; return 1; }
    temporary_directory=$(mktemp -d "$cache_directory/.sdk-install.XXXXXXXX" 2>/dev/null) || {
        _plutonium_fail "could not create a private temporary directory under $cache_directory."
        return 1
    }
    chmod 700 "$temporary_directory" 2>/dev/null || true
    PLUTONIUM_BOOTSTRAP_TEMP="$temporary_directory"
    trap '_plutonium_cleanup' EXIT

    installer_url="https://raw.githubusercontent.com/$installer_repo/$installer_commit/src/dotnet-install.sh"
    if ! curl -f -s -S -L --retry 2 --connect-timeout 20 -o "$temporary_directory/dotnet-install.sh" "$installer_url"; then
        _plutonium_fail "failed to download the .NET installer for SDK $pinned_version (offline or network failure)."
        return 1
    fi

    actual_sha=$(_plutonium_sha256_file "$temporary_directory/dotnet-install.sh" 2>/dev/null || true)
    if [ -z "$actual_sha" ] || [ "$actual_sha" != "$installer_sha" ]; then
        _plutonium_fail "installer checksum mismatch (sha256) for SDK $pinned_version; refusing to execute it."
        return 1
    fi

    local installer_output=""
    if ! installer_output=$(bash "$temporary_directory/dotnet-install.sh" \
        --version "$pinned_version" \
        --install-dir "$install_directory" \
        --no-path \
        --dry-run \
        --architecture "$PLUTONIUM_ARCHITECTURE" \
        --os "$PLUTONIUM_OS" 2>&1); then
        _plutonium_fail "the verified .NET installer failed for SDK $pinned_version."
        printf '%s\n' "$installer_output" >&2
        return 1
    fi

    if [ -x "$install_directory/dotnet" ]; then
        if reported=$(_plutonium_probe "$install_directory/dotnet" "$probe_directory" "$pinned_version" "$roll_forward" "$allow_prerelease"); then
            _plutonium_cleanup
            _plutonium_export_toolchain "$install_directory/dotnet"
            echo "PlutoniumBuild: installed .NET SDK $reported ($install_directory/dotnet)" >&2
            return 0
        fi
        _plutonium_fail "the installer produced a dotnet that does not satisfy SDK $pinned_version."
        return 1
    fi

    archive_url=$(printf '%s\n' "$installer_output" \
        | sed -n 's/^dotnet-install: URL #0 - primary: //p' \
        | head -1 | tr -d '\r')
    if [ -z "$archive_url" ]; then
        _plutonium_fail "the verified installer did not report a download URL for SDK $pinned_version."
        return 1
    fi
    case "$archive_url" in
        "https://builds.dotnet.microsoft.com/dotnet/Sdk/$pinned_version/"*) ;;
        *) _plutonium_fail "installer reported an unexpected SDK URL: $archive_url"; return 1 ;;
    esac

    case "$archive_url" in
        *.tar.gz) extension="tar.gz" ;;
        *.zip) extension="zip" ;;
        *) _plutonium_fail "installer reported an unsupported archive: $archive_url"; return 1 ;;
    esac
    archive_path="$temporary_directory/sdk.$extension"
    if ! curl -f -s -S -L --retry 2 --connect-timeout 20 -o "$archive_path" "$archive_url"; then
        _plutonium_fail "failed to download the .NET SDK $pinned_version archive (offline or network failure)."
        return 1
    fi

    expected_sha=$(_plutonium_lock_get "$lock_file" "sdk_archive_${PLUTONIUM_RID}_sha512")
    case "$expected_sha" in
        *[!0-9a-fA-F]*) _plutonium_fail "plutonium.lock has no SHA-512 pin for $PLUTONIUM_RID (SDK $pinned_version)."; return 1 ;;
    esac
    if [ "${#expected_sha}" -ne 128 ]; then
        _plutonium_fail "plutonium.lock has no SHA-512 pin for $PLUTONIUM_RID (SDK $pinned_version)."
        return 1
    fi
    actual_sha=$(_plutonium_sha512_file "$archive_path" 2>/dev/null || true)
    if [ -z "$actual_sha" ] || [ "$actual_sha" != "$expected_sha" ]; then
        _plutonium_fail "SDK archive checksum mismatch (sha512) for $PLUTONIUM_RID; refusing to extract it."
        return 1
    fi

    mkdir -p "$temporary_directory/extract"
    case "$extension" in
        tar.gz)
            tar -xzf "$archive_path" -C "$temporary_directory/extract" || {
                _plutonium_fail "could not extract the .NET SDK archive."
                return 1
            }
            ;;
        zip)
            command -v unzip >/dev/null 2>&1 || {
                _plutonium_fail "unzip is required to extract the .NET SDK archive."
                return 1
            }
            unzip -q "$archive_path" -d "$temporary_directory/extract" || {
                _plutonium_fail "could not extract the .NET SDK archive."
                return 1
            }
            ;;
    esac

    if [ ! -x "$temporary_directory/extract/dotnet" ]; then
        _plutonium_fail "the verified .NET SDK archive did not contain a dotnet executable."
        return 1
    fi
    if ! reported=$(_plutonium_probe "$temporary_directory/extract/dotnet" "$probe_directory" "$pinned_version" "$roll_forward" "$allow_prerelease"); then
        _plutonium_fail "the verified .NET SDK archive does not report SDK $pinned_version."
        return 1
    fi

    case "$install_directory" in
        "$cache_directory"/*) ;;
        *) _plutonium_fail "refusing to install the SDK outside $cache_directory."; return 1 ;;
    esac
    rm -rf -- "$install_directory"
    mv "$temporary_directory/extract" "$install_directory" || {
        _plutonium_fail "could not move the .NET SDK into $install_directory."
        return 1
    }

    _plutonium_cleanup
    _plutonium_export_toolchain "$install_directory/dotnet"
    echo "PlutoniumBuild: installed .NET SDK $reported ($install_directory/dotnet)" >&2
}

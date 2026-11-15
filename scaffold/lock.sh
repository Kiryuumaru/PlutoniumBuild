#!/usr/bin/env bash
# Maintainer utility for scaffold/build/plutonium.lock. It maps installed paths
# back to the scaffold source and either verifies or refreshes the recorded
# SHA-256 hashes. CI runs the verify mode; release preparation runs write mode.

set -euo pipefail

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
ROOT=$(dirname "$SCRIPT_DIR")
LOCK_FILE="$SCRIPT_DIR/build/plutonium.lock"
LOCK_FORMAT=1

lock_get() {
    local key="$1"
    awk -v key="$key" '
        /^[[:space:]]*#/ { next }
        {
            line = $0
            sub(/\r$/, "", line)
            prefix = key "="
            if (index(line, prefix) == 1) {
                value = substr(line, length(prefix) + 1)
                sub(/[[:space:]]+$/, "", value)
                print value
                exit
            }
        }
    ' "$LOCK_FILE"
}

is_reserved_lock_key() {
    case "$1" in
        format|source|source_url|scaffold_commit|package_version|sdk_version|\
        installer_repo|installer_commit|installer_sh_sha256|installer_ps1_sha256|\
        sdk_archives_sha256)
            return 0
            ;;
        sdk_archive_*_sha512)
            return 0
            ;;
    esac
    return 1
}

validate_lock() {
    local file="$1" prefix="$2"
    local line trimmed key value seen=" "
    while IFS= read -r line; do
        line=${line%$'\r'}
        case "$line" in
            ''|'#'*) continue ;;
        esac
        trimmed=${line#"${line%%[![:space:]]*}"}
        case "$trimmed" in
            '#'*) continue ;;
        esac
        if [[ "$trimmed" =~ ^([A-Za-z0-9_-]+)= ]]; then
            key=${BASH_REMATCH[1]}
            if ! is_reserved_lock_key "$key"; then
                continue
            fi
            if [ "$trimmed" != "$line" ]; then
                echo "$prefix is invalid: reserved key $key must start at column 0." >&2
                return 1
            fi
            case "$seen" in
                *" $key "*)
                    echo "$prefix is invalid: duplicate reserved key $key." >&2
                    return 1
                    ;;
            esac
            seen="$seen$key "
            value=${trimmed#*=}
            if [ "$key" = "scaffold_commit" ] && ! [[ "$value" =~ ^[0-9a-fA-F]{40}$ ]]; then
                echo "$prefix is invalid: scaffold_commit must be a full 40-character commit SHA." >&2
                return 1
            fi
            continue
        fi
        if [[ "$trimmed" =~ ^([A-Za-z0-9_-]+)[[:space:]]+= ]]; then
            key=${BASH_REMATCH[1]}
            if is_reserved_lock_key "$key"; then
                echo "$prefix is invalid: reserved key $key has a non-canonical form; write $key=value at column 0." >&2
                return 1
            fi
        fi
    done < "$file"
    for key in format source source_url scaffold_commit package_version; do
        case "$seen" in
            *" $key "*) ;;
            *)
                echo "$prefix is invalid: missing required key $key." >&2
                return 1
                ;;
        esac
    done
    return 0
}

sha256_file() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | awk '{print $1}'
    elif command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$1" | awk '{print $1}'
    elif command -v openssl >/dev/null 2>&1; then
        openssl dgst -sha256 "$1" | sed 's/^.*= //'
    else
        return 1
    fi
}

sha256_stdin() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum | awk '{print $1}'
    elif command -v shasum >/dev/null 2>&1; then
        shasum -a 256 | awk '{print $1}'
    else
        openssl dgst -sha256 | sed 's/^.*= //'
    fi
}

source_path() {
    local installed="$1"
    case "$installed" in
        build/*) printf '%s\n' "$SCRIPT_DIR/build/${installed#build/}" ;;
        build.sh|build.ps1|build.cmd) printf '%s\n' "$SCRIPT_DIR/root/$installed" ;;
        *) printf '' ;;
    esac
}

lock_path_is_safe() {
    local relative="$1"
    case "$relative" in
        build.sh|build.ps1|build.cmd) return 0 ;;
        build/*) ;;
        *) return 1 ;;
    esac
    local rest=${relative#build/}
    [ -n "$rest" ] || return 1
    case "$rest" in
        *\\*) return 1 ;;
    esac
    case "/$rest/" in
        *"/../"*|*"/./"*|*"//"*) return 1 ;;
    esac
    return 0
}

lock_has_path() {
    local file="$1" relative="$2"
    awk -v path="$relative" '
        ($1 == "managed" || $1 == "preserve") && $2 == path { found = 1; exit }
        END { exit(found ? 0 : 1) }
    ' "$file"
}

lock_has_managed() {
    local file="$1" relative="$2"
    awk -v path="$relative" '
        $1 == "managed" && $2 == path { found = 1; exit }
        END { exit(found ? 0 : 1) }
    ' "$file"
}

package_pin() {
    local project="$1"
    [ -f "$project" ] || return 1
    awk '
        /<PackageReference/ && /Include="PlutoniumBuild"/ { inref = 1 }
        inref && match($0, /Version="[^"]*"/) {
            print substr($0, RSTART + 9, RLENGTH - 10)
            exit
        }
        inref && match($0, /<Version>[^<]*<\/Version>/) {
            print substr($0, RSTART + 9, RLENGTH - 19)
            exit
        }
        /<\/PackageReference>/ { inref = 0 }
    ' "$project"
}

props_version() {
    sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$ROOT/src/Directory.Build.props" | head -1
}

require_version_invariant() {
    local package_version="${1:-$(lock_get package_version)}"
    local pin props
    if [ -z "$package_version" ]; then
        echo "scaffold/lock.sh: lock has no package_version." >&2
        return 1
    fi
    props=$(props_version)
    if [ -z "$props" ]; then
        echo "scaffold/lock.sh: src/Directory.Build.props has no package version." >&2
        return 1
    fi
    if [ "$props" != "$package_version" ]; then
        echo "scaffold/lock.sh: src/Directory.Build.props version $props does not match package_version $package_version." >&2
        return 1
    fi
    pin=$(package_pin "$SCRIPT_DIR/build/build.csproj")
    if [ -z "$pin" ]; then
        echo "scaffold/lock.sh: build.csproj has no PlutoniumBuild package pin." >&2
        return 1
    fi
    if [ "$pin" != "$package_version" ]; then
        echo "scaffold/lock.sh: build.csproj pin $pin does not match package_version $package_version." >&2
        return 1
    fi
}

# Every installable file must be named by the manifest, and every manifest path
# must resolve to a regular source file. The lock itself is the only exception.
check_completeness() {
    local lock="$1" failures=0 file relative line tag expected fields source
    while IFS= read -r file; do
        relative="build/${file#"$SCRIPT_DIR/build/"}"
        [ "$relative" = "build/plutonium.lock" ] && continue
        if ! lock_has_path "$lock" "$relative"; then
            echo "scaffold/lock.sh: $relative is not managed or preserved by the lock." >&2
            failures=$((failures + 1))
        fi
    done < <(find "$SCRIPT_DIR/build" -type f)

    while IFS= read -r file; do
        relative=${file#"$SCRIPT_DIR/root/"}
        if ! lock_has_path "$lock" "$relative"; then
            echo "scaffold/lock.sh: root wrapper $relative is not managed or preserved by the lock." >&2
            failures=$((failures + 1))
        fi
    done < <(find "$SCRIPT_DIR/root" -type f)

    for wrapper in build.sh build.ps1 build.cmd; do
        if [ ! -f "$SCRIPT_DIR/root/$wrapper" ]; then
            echo "scaffold/lock.sh: root wrapper $wrapper is missing." >&2
            failures=$((failures + 1))
        fi
    done

    while IFS= read -r file; do
        echo "scaffold/lock.sh: scaffold payload contains a non-regular file: $file" >&2
        failures=$((failures + 1))
    done < <(find "$SCRIPT_DIR/build" "$SCRIPT_DIR/root" ! -type d ! -type f)

    while IFS= read -r line; do
        case "$line" in
            managed\ *|preserve\ *)
                tag=${line%% *}
                relative=$(printf '%s\n' "$line" | awk '{print $2}')
                fields=$(printf '%s\n' "$line" | awk '{print NF}')
                if [ "$relative" = "build/plutonium.lock" ]; then
                    echo "scaffold/lock.sh: the lock must not list itself." >&2
                    failures=$((failures + 1))
                    continue
                fi
                if ! lock_path_is_safe "$relative"; then
                    echo "scaffold/lock.sh: manifest contains an unsafe path: $relative" >&2
                    failures=$((failures + 1))
                    continue
                fi
                if [ "$tag" = "managed" ]; then
                    expected=$(printf '%s\n' "$line" | awk '{print $3}')
                    if [ "$fields" -ne 3 ]; then
                        echo "scaffold/lock.sh: malformed managed entry for $relative." >&2
                        failures=$((failures + 1))
                        continue
                    fi
                    case "$expected" in
                        *[!0-9a-fA-F]*)
                            echo "scaffold/lock.sh: invalid manifest hash for $relative." >&2
                            failures=$((failures + 1))
                            continue
                            ;;
                    esac
                    if [ "${#expected}" -ne 64 ]; then
                        echo "scaffold/lock.sh: invalid manifest hash for $relative." >&2
                        failures=$((failures + 1))
                        continue
                    fi
                elif [ "$fields" -ne 2 ]; then
                    echo "scaffold/lock.sh: malformed preserve entry for $relative." >&2
                    failures=$((failures + 1))
                    continue
                fi
                source=$(source_path "$relative")
                if [ -z "$source" ] || [ ! -f "$source" ]; then
                    echo "scaffold/lock.sh: manifest path is missing from the scaffold: $relative" >&2
                    failures=$((failures + 1))
                fi
                ;;
        esac
    done < "$lock"

    local duplicate
    duplicate=$(awk '$1 == "managed" { if (seen[$2]++) { print $2; exit } }' "$lock")
    if [ -n "$duplicate" ]; then
        echo "scaffold/lock.sh: manifest has duplicate managed entries for $duplicate." >&2
        failures=$((failures + 1))
    fi

    [ "$failures" -eq 0 ]
}

require_tools() {
    if ! command -v sha256sum >/dev/null 2>&1 &&
       ! command -v shasum >/dev/null 2>&1 &&
       ! command -v openssl >/dev/null 2>&1; then
        echo "scaffold/lock.sh: sha256sum, shasum, or openssl is required." >&2
        exit 1
    fi
}

write_lock() {
    local temporary="$LOCK_FILE.new"
    local line installed expected actual source
    [ -f "$LOCK_FILE" ] || {
        echo "scaffold/lock.sh: missing $LOCK_FILE" >&2
        exit 1
    }
    validate_lock "$LOCK_FILE" "scaffold/lock.sh: lock" || exit 1
    if ! check_completeness "$LOCK_FILE"; then
        echo "scaffold/lock.sh: add managed or preserve entries for every scaffold file before writing." >&2
        exit 1
    fi

    local package_version
    package_version=$(props_version)
    if [ -z "$package_version" ]; then
        echo "scaffold/lock.sh: src/Directory.Build.props has no package version." >&2
        exit 1
    fi
    if ! require_version_invariant "$package_version"; then
        exit 1
    fi

    : > "$temporary"

    while IFS= read -r line; do
        case "$line" in
            managed\ *)
                installed=$(printf '%s\n' "$line" | awk '{print $2}')
                source=$(source_path "$installed")
                if [ -z "$source" ] || [ ! -f "$source" ]; then
                    echo "scaffold/lock.sh: managed file is missing: $installed" >&2
                    rm -f "$temporary"
                    exit 1
                fi
                actual=$(sha256_file "$source")
                printf 'managed %s %s\n' "$installed" "$actual" >> "$temporary"
                ;;
            *)
                printf '%s\n' "$line" >> "$temporary"
                ;;
        esac
    done < "$LOCK_FILE"

    local archive_block
    archive_block=$(
        awk -F= '/^sdk_archive_.*_sha512=/ { print $1 "=" $2 }' "$temporary" | LC_ALL=C sort
    )
    if [ -z "$archive_block" ]; then
        echo "scaffold/lock.sh: lock has no sdk archive hashes." >&2
        rm -f "$temporary"
        exit 1
    fi
    local archives_digest
    archives_digest=$(printf '%s\n' "$archive_block" | sha256_stdin)

    if ! grep -q '^sdk_archives_sha256=' "$temporary"; then
        printf 'sdk_archives_sha256=%s\n' "$archives_digest" >> "$temporary"
    else
        awk -v digest="$archives_digest" '
            /^sdk_archives_sha256=/ { print "sdk_archives_sha256=" digest; next }
            { print }
        ' "$temporary" > "$temporary.digest"
        mv "$temporary.digest" "$temporary"
    fi

    awk -v version="$package_version" '
        /^package_version=/ { print "package_version=" version; next }
        { print }
    ' "$temporary" > "$temporary.version"
    mv "$temporary.version" "$temporary"

    mv "$temporary" "$LOCK_FILE"
    echo "scaffold/lock.sh: wrote $LOCK_FILE"
    verify_lock
}

verify_lock() {
    [ -f "$LOCK_FILE" ] || {
        echo "scaffold/lock.sh: missing $LOCK_FILE" >&2
        exit 1
    }
    validate_lock "$LOCK_FILE" "scaffold/lock.sh: lock" || exit 1

    local failures=0 line installed expected actual source

    if [ "$(lock_get format)" != "$LOCK_FORMAT" ]; then
        echo "scaffold/lock.sh: unsupported lock format (expected $LOCK_FORMAT)." >&2
        failures=$((failures + 1))
    fi

    if ! check_completeness "$LOCK_FILE"; then
        failures=$((failures + 1))
    fi

    while IFS= read -r line; do
        case "$line" in
            managed\ *)
                installed=$(printf '%s\n' "$line" | awk '{print $2}')
                expected=$(printf '%s\n' "$line" | awk '{print $3}')
                source=$(source_path "$installed")
                if [ -z "$source" ] || [ ! -f "$source" ]; then
                    echo "scaffold/lock.sh: managed file is missing: $installed" >&2
                    failures=$((failures + 1))
                    continue
                fi
                actual=$(sha256_file "$source")
                if [ "$actual" != "$expected" ]; then
                    echo "scaffold/lock.sh: hash mismatch for $installed" >&2
                    failures=$((failures + 1))
                fi
                ;;
        esac
    done < "$LOCK_FILE"

    if ! require_version_invariant; then
        failures=$((failures + 1))
    fi

    local sdk_version
    sdk_version=$(lock_get sdk_version)
    local global_version
    global_version=$(sed -n '/"sdk"[[:space:]]*:/,/^[[:space:]]*}/p' "$SCRIPT_DIR/build/global.json" \
        | tr -d '\n\r' \
        | sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')
    if [ "$sdk_version" != "$global_version" ]; then
        echo "scaffold/lock.sh: sdk_version does not match scaffold/build/global.json." >&2
        failures=$((failures + 1))
    fi

    local archives_digest
    archives_digest=$(
        awk -F= '/^sdk_archive_.*_sha512=/ { print $1 "=" $2 }' "$LOCK_FILE" \
            | LC_ALL=C sort \
            | sha256_stdin
    )
    if [ "$(lock_get sdk_archives_sha256)" != "$archives_digest" ]; then
        echo "scaffold/lock.sh: sdk_archives_sha256 does not match the archive pins." >&2
        failures=$((failures + 1))
    fi

    if [ "$failures" -ne 0 ]; then
        echo "scaffold/lock.sh: $failures verification failure(s)." >&2
        exit 1
    fi

    echo "scaffold/lock.sh: lock verified."
}

command=${1:-verify}
case "$command" in
    verify) require_tools; verify_lock ;;
    write) require_tools; write_lock ;;
    *)
        echo "usage: scaffold/lock.sh [verify|write]" >&2
        exit 2
        ;;
esac

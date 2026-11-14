#!/usr/bin/env bash
# Refresh an installed PlutoniumBuild scaffold from a resolved source commit.
# Managed files are replaced atomically, user-owned files are preserved, and a
# failed smoke test restores the previous tree from build/temp/update/<stamp>.

set -euo pipefail

DEFAULT_SOURCE="https://github.com/Kiryuumaru/PlutoniumBuild.git"
DEFAULT_REF="refs/heads/main"
SOURCE=""
SHA=""
REQUESTED_REF="${PLUTONIUM_REF:-}"
TARGET="$PWD"
NO_SMOKE=0
FORCE_SMOKE=0

usage() {
    cat <<'EOF'
usage: build/update.sh [--ref <branch|tag|commit> | --sha <40-character-commit>] [options]

options:
  --ref <ref>       source branch, tag, or full commit (default: main)
  --sha <sha>       source commit to install (exactly 40 characters)
  --source <url>    source repository (default: the lock's source)
  --target <dir>    consumer repository (default: current directory)
  --no-smoke        skip the post-update smoke test
  --smoke           force a restoring build smoke test
EOF
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --sha)
            [ "$#" -ge 2 ] || { echo "build/update.sh: --sha needs a value." >&2; exit 2; }
            SHA="$2"
            shift 2
            ;;
        --ref)
            [ "$#" -ge 2 ] || { echo "build/update.sh: --ref needs a value." >&2; exit 2; }
            REQUESTED_REF="$2"
            shift 2
            ;;
        --source)
            [ "$#" -ge 2 ] || { echo "build/update.sh: --source needs a value." >&2; exit 2; }
            SOURCE="$2"
            shift 2
            ;;
        --target)
            [ "$#" -ge 2 ] || { echo "build/update.sh: --target needs a value." >&2; exit 2; }
            TARGET="$2"
            shift 2
            ;;
        --no-smoke)
            NO_SMOKE=1
            shift
            ;;
        --smoke)
            FORCE_SMOKE=1
            shift
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            echo "build/update.sh: unknown argument: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

EXPLICIT_SOURCE="$SOURCE"

if [ -n "$SHA" ] && [ -n "$REQUESTED_REF" ]; then
    echo "build/update.sh: --sha and --ref are mutually exclusive." >&2
    exit 2
fi
if [ -n "$SHA" ]; then
    if ! [[ "$SHA" =~ ^[0-9a-fA-F]{40}$ ]]; then
        echo "build/update.sh: --sha must be a full 40-character commit SHA." >&2
        exit 2
    fi
    SHA=$(printf '%s' "$SHA" | tr 'A-F' 'a-f')
fi

validate_ref() {
    local value="$1"
    [ -n "$value" ] || return 1
    case "$value" in
        -*) return 1 ;;
        *[[:space:][:cntrl:]]*) return 1 ;;
        *:*|*+*|*^*|*~*|*'?'*|*'*'*|*'['*|*\\*) return 1 ;;
        *'@{'*) return 1 ;;
        *'..'*) return 1 ;;
        *.lock|*.lock/*) return 1 ;;
        *.) return 1 ;;
        */|/*) return 1 ;;
        refs/pull/*) return 1 ;;
        refs/heads/*|refs/tags/*) return 0 ;;
        refs/*) return 1 ;;
    esac
    return 0
}

if [ -n "$REQUESTED_REF" ] && ! validate_ref "$REQUESTED_REF"; then
    echo "build/update.sh: --ref must be a branch or tag name, a refs/heads/... branch, a refs/tags/... tag, or a full 40-character commit SHA." >&2
    exit 2
fi

if [ -n "$REQUESTED_REF" ]; then
    REF_LABEL="$REQUESTED_REF"
elif [ -n "$SHA" ]; then
    REF_LABEL="$SHA"
else
    REF_LABEL="$DEFAULT_REF (default)"
fi

case "$SOURCE" in
    -*) echo "build/update.sh: --source must not start with '-'." >&2; exit 2 ;;
esac

[ -d "$TARGET" ] || { echo "build/update.sh: target directory not found: $TARGET" >&2; exit 1; }
TARGET=$(cd "$TARGET" && pwd -P)
BUILD_DIR="$TARGET/build"
LOCK_FILE="$BUILD_DIR/plutonium.lock"

[ -f "$LOCK_FILE" ] || {
    echo "build/update.sh: $BUILD_DIR/plutonium.lock not found; run init first." >&2
    exit 1
}

lock_get() {
    local file="$1" key="$2"
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
    ' "$file"
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

validate_lock "$LOCK_FILE" "build/update.sh: installed lock" || exit 1

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

installed_path() {
    local relative="$1"
    case "$relative" in
        build/*) printf '%s\n' "$BUILD_DIR/${relative#build/}" ;;
        build.sh|build.ps1|build.cmd) printf '%s\n' "$TARGET/$relative" ;;
        *) printf '' ;;
    esac
}

source_path() {
    local root="$1" relative="$2"
    case "$relative" in
        build/*) printf '%s\n' "$root/scaffold/build/${relative#build/}" ;;
        build.sh|build.ps1|build.cmd) printf '%s\n' "$root/scaffold/root/$relative" ;;
        *) printf '' ;;
    esac
}

reject_unsafe_source_paths() {
    local root="$1" file relative
    while IFS= read -r -d '' file; do
        relative=${file#"$root/"}
        case "$relative" in
            *[[:cntrl:]]*)
                printf 'build/update.sh: source scaffold contains a path with control characters: %q; refusing the update.\n' "$relative" >&2
                return 1
                ;;
        esac
    done < <(find "$root" -print0)
    return 0
}

normalize_line_endings() {
    local root="$1" file temporary
    while IFS= read -r -d '' file; do
        temporary=$(mktemp "$(dirname "$file")/.plutonium-lf.XXXXXXXX") || return 1
        if ! cp -p "$file" "$temporary" || ! tr -d '\r' < "$file" > "$temporary"; then
            rm -f -- "$temporary"
            return 1
        fi
        if ! mv "$temporary" "$file"; then
            rm -f -- "$temporary"
            return 1
        fi
    done < <(find "$root" -type f -print0)
}

is_safe_manifest_path() {
    local relative="$1"
    case "$relative" in
        *[[:cntrl:]]*) return 1 ;;
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

validate_source_url() {
    local value="$1"
    [ -n "$value" ] || return 1
    case "$value" in
        *://*@*) return 1 ;;
        *\?*) return 1 ;;
        *#*) return 1 ;;
        *[[:space:][:cntrl:]]*) return 1 ;;
    esac
    return 0
}

resolve_ref_sha() {
    local source="$1" requested="$2"
    local ref="$requested" output line sha="" peeled="" name candidate
    local branch_found=0 tag_found=0

    if [ -z "$ref" ]; then
        ref="$DEFAULT_REF"
    fi

    if [[ "$ref" =~ ^[0-9a-fA-F]{40}$ ]]; then
        printf '%s\n' "$(printf '%s' "$ref" | tr 'A-F' 'a-f')"
        return 0
    fi

    case "$ref" in
        refs/heads/*|refs/tags/*) ;;
        *)
            if ! output=$(GIT_TERMINAL_PROMPT=0 git ls-remote -- "$source" \
                "refs/heads/$ref" "refs/tags/$ref" "refs/tags/$ref^{}" 2>/dev/null); then
                echo "build/update.sh: could not query $ref from $source." >&2
                return 1
            fi
            while IFS=$'\t' read -r candidate name; do
                if [ "$name" = "refs/heads/$ref" ]; then
                    branch_found=1
                fi
                if [ "$name" = "refs/tags/$ref" ]; then
                    tag_found=1
                fi
            done <<< "$output"
            if [ "$branch_found" -eq 1 ] && [ "$tag_found" -eq 1 ]; then
                echo "build/update.sh: ref $ref is ambiguous: refs/heads/$ref and refs/tags/$ref both exist; pass refs/heads/$ref or refs/tags/$ref." >&2
                return 2
            fi
            if [ "$branch_found" -eq 1 ]; then
                ref="refs/heads/$ref"
            elif [ "$tag_found" -eq 1 ]; then
                ref="refs/tags/$ref"
            else
                echo "build/update.sh: ref $ref was not found in $source." >&2
                return 1
            fi
            ;;
    esac

    if ! output=$(GIT_TERMINAL_PROMPT=0 git ls-remote -- "$source" "$ref" "$ref^{}" 2>/dev/null); then
        echo "build/update.sh: could not query $ref from $source." >&2
        return 1
    fi
    while IFS=$'\t' read -r candidate name; do
        if [ -n "$name" ] && [ "$name" = "$ref" ]; then
            sha="$candidate"
        fi
        if [ -n "$name" ] && [ "$name" = "$ref^{}" ]; then
            peeled="$candidate"
        fi
    done <<< "$output"
    if [ -n "$peeled" ]; then
        sha="$peeled"
    fi
    if [ -z "$sha" ]; then
        echo "build/update.sh: ref $ref was not found in $source." >&2
        return 1
    fi
    printf '%s\n' "$sha"
}

lock_has_path() {
    local file="$1" relative="$2"
    awk -v path="$relative" '
        { sub(/\r$/, "") }
        ($1 == "managed" || $1 == "preserve") && $2 == path { found = 1; exit }
        END { exit(found ? 0 : 1) }
    ' "$file"
}

lock_has_managed() {
    local file="$1" relative="$2"
    awk -v path="$relative" '
        { sub(/\r$/, "") }
        $1 == "managed" && $2 == path { found = 1; exit }
        END { exit(found ? 0 : 1) }
    ' "$file"
}

is_preserved_in() {
    local lock="$1" relative="$2"
    awk -v path="$relative" '
        { sub(/\r$/, "") }
        $1 == "preserve" && $2 == path { found = 1; exit }
        END { exit(found ? 0 : 1) }
    ' "$lock"
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

require_version_invariant() {
    local root="$1" lock="$2" package_version pin props
    package_version=$(lock_get "$lock" package_version)
    if [ -z "$package_version" ]; then
        echo "build/update.sh: source lock has no package_version." >&2
        return 1
    fi
    props=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$root/src/Directory.Build.props" | head -1)
    if [ -z "$props" ]; then
        echo "build/update.sh: source src/Directory.Build.props has no package version." >&2
        return 1
    fi
    if [ "$props" != "$package_version" ]; then
        echo "build/update.sh: source src/Directory.Build.props version $props does not match lock package_version $package_version." >&2
        return 1
    fi
    pin=$(package_pin "$root/scaffold/build/build.csproj")
    if [ -z "$pin" ]; then
        echo "build/update.sh: source build.csproj has no PlutoniumBuild package pin." >&2
        return 1
    fi
    if [ "$pin" != "$package_version" ]; then
        echo "build/update.sh: source package pin $pin does not match lock package_version $package_version." >&2
        return 1
    fi
}

check_source_contract() {
    local root="$1" lock="$2" failures=0 file relative line source
    while IFS= read -r -d '' file; do
        relative=${file#"$root/"}
        case "$relative" in
            scaffold/build/plutonium.lock) continue ;;
            scaffold/build/*) relative="build/${relative#scaffold/build/}" ;;
            scaffold/root/*) relative="${relative#scaffold/root/}" ;;
            *) continue ;;
        esac
        if ! lock_has_path "$lock" "$relative"; then
            echo "build/update.sh: source scaffold contains an unlisted file: $relative" >&2
            failures=$((failures + 1))
        fi
    done < <(find "$root/scaffold/build" -type f -print0
        find "$root/scaffold/root" -type f -print0)

    while IFS= read -r line; do
        line=${line%$'\r'}
        case "$line" in
            preserve\ *) ;;
            *) continue ;;
        esac
        relative=$(printf '%s\n' "$line" | awk '{print $2}')
        if ! is_safe_manifest_path "$relative"; then
            echo "build/update.sh: manifest contains an unsafe preserve path: $relative" >&2
            failures=$((failures + 1))
            continue
        fi
        source=$(source_path "$root" "$relative")
        if [ -z "$source" ] || [ ! -f "$source" ]; then
            echo "build/update.sh: source scaffold is missing preserved file $relative." >&2
            failures=$((failures + 1))
        fi
    done < "$lock"

    [ "$failures" -eq 0 ]
}

rewrite_managed_hash() {
    local lock="$1" relative="$2" hash="$3" temporary="$1.hash"
    awk -v path="$relative" -v hash="$hash" '
        { sub(/\r$/, "") }
        $1 == "managed" && $2 == path { print "managed " path " " hash; next }
        { print }
    ' "$lock" > "$temporary"
    mv "$temporary" "$lock"
}

verify_manifest() {
    local root="$1" lock="$2"
    local line tag relative expected source actual
    while IFS= read -r line; do
        line=${line%$'\r'}
        case "$line" in
            managed\ *) ;;
            *) continue ;;
        esac
        tag=$(printf '%s\n' "$line" | awk '{print $1}')
        relative=$(printf '%s\n' "$line" | awk '{print $2}')
        expected=$(printf '%s\n' "$line" | awk '{print $3}')
        if ! is_safe_manifest_path "$relative"; then
            echo "build/update.sh: manifest contains an unsafe path: $relative" >&2
            return 1
        fi
        source=$(source_path "$root" "$relative")
        if [ -z "$source" ] || [ ! -f "$source" ]; then
            echo "build/update.sh: source scaffold is missing $relative." >&2
            return 1
        fi
        case "$expected" in
            *[!0-9a-fA-F]*)
                echo "build/update.sh: invalid manifest hash for $relative." >&2
                return 1
                ;;
        esac
        if [ "${#expected}" -ne 64 ]; then
            echo "build/update.sh: invalid manifest hash for $relative." >&2
            return 1
        fi
        actual=$(sha256_file "$source")
        if [ "$actual" != "$expected" ]; then
            echo "build/update.sh: manifest hash mismatch for $relative; refusing the update." >&2
            return 1
        fi
    done < "$lock"
}

CURRENT_SOURCE=$(lock_get "$LOCK_FILE" source)
CURRENT_SOURCE_URL=$(lock_get "$LOCK_FILE" source_url)
if [ -z "$CURRENT_SOURCE" ]; then
    CURRENT_SOURCE="github.com/Kiryuumaru/PlutoniumBuild"
fi
if ! validate_source_url "$CURRENT_SOURCE"; then
    echo "build/update.sh: installed lock source contains credentials (userinfo, query, or fragment); refusing to persist it." >&2
    exit 1
fi
if [ -n "$CURRENT_SOURCE_URL" ] && ! validate_source_url "$CURRENT_SOURCE_URL"; then
    echo "build/update.sh: installed lock source_url contains credentials (userinfo, query, or fragment); refusing to persist it." >&2
    exit 1
fi

if [ -z "$SOURCE" ]; then
    SOURCE="$CURRENT_SOURCE"
fi
if [ -z "$SOURCE" ] || [ "$SOURCE" = "github.com/Kiryuumaru/PlutoniumBuild" ]; then
    SOURCE="$DEFAULT_SOURCE"
fi
if ! validate_source_url "$SOURCE"; then
    echo "build/update.sh: --source must not contain credentials (userinfo, query, or fragment)." >&2
    exit 2
fi
case "$SOURCE" in
    -*) echo "build/update.sh: source must not start with '-'." >&2; exit 2 ;;
esac

LOCK_SOURCE="$CURRENT_SOURCE"
LOCK_SOURCE_URL="$CURRENT_SOURCE_URL"
if [ -n "$EXPLICIT_SOURCE" ]; then
    LOCK_SOURCE="$SOURCE"
    if [ "$SOURCE" = "$DEFAULT_SOURCE" ]; then
        LOCK_SOURCE="github.com/Kiryuumaru/PlutoniumBuild"
    fi
    LOCK_SOURCE_URL="$SOURCE"
fi

command -v git >/dev/null 2>&1 || {
    echo "build/update.sh: git is required to fetch the scaffold source." >&2
    exit 1
}

if [ -z "$SHA" ]; then
    resolved=""
    if resolved=$(resolve_ref_sha "$SOURCE" "$REQUESTED_REF"); then
        SHA="$resolved"
    else
        exit $?
    fi
fi
echo "PlutoniumBuild: source $SOURCE"
echo "PlutoniumBuild: ref $REF_LABEL"
echo "PlutoniumBuild: commit $SHA"

FETCH_DIR=$(mktemp -d "${TMPDIR:-/tmp}/plutonium-update.XXXXXXXX") || {
    echo "build/update.sh: could not create a temporary directory." >&2
    exit 1
}
chmod 700 "$FETCH_DIR"
STAGE=""
BACKUP=""
cleanup() {
    [ -n "${STAGE:-}" ] && rm -rf -- "$STAGE"
    rm -rf -- "$FETCH_DIR"
}
trap cleanup EXIT HUP INT TERM

git init -q "$FETCH_DIR"
# Git for Windows defaults to core.autocrlf=true; the lock records LF hashes.
git -C "$FETCH_DIR" config core.autocrlf false
git -C "$FETCH_DIR" config core.eol lf
git -C "$FETCH_DIR" remote add origin "$SOURCE"
if ! GIT_TERMINAL_PROMPT=0 git -C "$FETCH_DIR" fetch --depth 1 --no-tags origin -- "$SHA" >/dev/null 2>&1; then
    echo "build/update.sh: could not fetch commit $SHA from $SOURCE." >&2
    exit 1
fi
git -C "$FETCH_DIR" checkout -q --detach "$SHA" 2>/dev/null || git -C "$FETCH_DIR" checkout -q --detach FETCH_HEAD
FETCHED_SHA=$(git -C "$FETCH_DIR" rev-parse HEAD | tr -d '\r')
if [ "$FETCHED_SHA" != "$SHA" ]; then
    echo "build/update.sh: fetched $FETCHED_SHA but expected $SHA." >&2
    exit 1
fi

SOURCE_BUILD="$FETCH_DIR/scaffold/build"
SOURCE_ROOT="$FETCH_DIR/scaffold/root"
SOURCE_LOCK="$SOURCE_BUILD/plutonium.lock"
if [ ! -d "$SOURCE_BUILD" ] || [ ! -d "$SOURCE_ROOT" ] || [ ! -f "$SOURCE_LOCK" ]; then
    echo "build/update.sh: commit $SHA does not contain the scaffold payload." >&2
    exit 1
fi
validate_lock "$SOURCE_LOCK" "build/update.sh: source lock" || exit 1
if find "$FETCH_DIR/scaffold" -type l -print -quit | grep -q .; then
    echo "build/update.sh: scaffold payload contains a symbolic link; refusing to install it." >&2
    exit 1
fi
if find "$FETCH_DIR/scaffold" ! -type f ! -type d -print -quit | grep -q .; then
    echo "build/update.sh: scaffold payload contains a non-regular file; refusing to install it." >&2
    exit 1
fi
reject_unsafe_source_paths "$FETCH_DIR/scaffold" || exit 1
normalize_line_endings "$FETCH_DIR/scaffold"
if [ "$(lock_get "$SOURCE_LOCK" format)" != "1" ]; then
    echo "build/update.sh: source lock has an unsupported format." >&2
    exit 1
fi
require_version_invariant "$FETCH_DIR" "$SOURCE_LOCK" || exit 1
check_source_contract "$FETCH_DIR" "$SOURCE_LOCK" || exit 1
verify_manifest "$FETCH_DIR" "$SOURCE_LOCK"

NEW_PACKAGE_VERSION=$(lock_get "$SOURCE_LOCK" package_version)
if [ -z "$NEW_PACKAGE_VERSION" ]; then
    echo "build/update.sh: source lock has no package_version." >&2
    exit 1
fi

CURRENT_COMMIT=$(lock_get "$LOCK_FILE" scaffold_commit)
if [ "$CURRENT_COMMIT" = "$SHA" ]; then
    up_to_date=1
    while IFS= read -r line; do
        line=${line%$'\r'}
        case "$line" in
            managed\ *) ;;
            *) continue ;;
        esac
        relative=$(printf '%s\n' "$line" | awk '{print $2}')
        expected=$(printf '%s\n' "$line" | awk '{print $3}')
        if is_preserved_in "$LOCK_FILE" "$relative"; then
            continue
        fi
        target_file=$(installed_path "$relative")
        if [ ! -f "$target_file" ] || [ "$(sha256_file "$target_file")" != "$expected" ]; then
            up_to_date=0
            break
        fi
    done < "$LOCK_FILE"
    if [ "$up_to_date" -eq 1 ]; then
        echo "PlutoniumBuild: already at $SHA; nothing to update."
        exit 0
    fi
fi

STAMP=$(date -u +%Y%m%dT%H%M%SZ)
BACKUP="$BUILD_DIR/temp/update/$STAMP"
if [ -e "$BACKUP" ]; then
    BACKUP="$BACKUP-$$"
fi
mkdir -p "$BACKUP/files"
ACTIONS_FILE="$BACKUP/actions.tsv"
: > "$ACTIONS_FILE"

backup_one() {
    local relative="$1"
    local source
    source=$(installed_path "$relative")
    if [ -e "$source" ] || [ -L "$source" ]; then
        if [ ! -f "$source" ]; then
            echo "build/update.sh: installed path is not a regular file: $relative" >&2
            return 1
        fi
        mkdir -p "$BACKUP/files/$(dirname "$relative")"
        cp -p "$source" "$BACKUP/files/$relative"
        printf '%s\t1\n' "$relative" >> "$ACTIONS_FILE"
    else
        printf '%s\t0\n' "$relative" >> "$ACTIONS_FILE"
    fi
}

backup_lock_entries() {
    local lock="$1" line relative
    while IFS= read -r line; do
        line=${line%$'\r'}
        case "$line" in
            managed\ *|preserve\ *)
                relative=$(printf '%s\n' "$line" | awk '{print $2}')
                backup_one "$relative"
                ;;
        esac
    done < "$lock"
}

backup_lock_entries "$LOCK_FILE"
backup_lock_entries "$SOURCE_LOCK"
backup_one "build/plutonium.lock"

STAGE=$(mktemp -d "$BUILD_DIR/temp/.update.XXXXXXXX") || {
    echo "build/update.sh: could not create a staging directory under build/temp." >&2
    exit 1
}
chmod 700 "$STAGE"
mkdir -p "$STAGE/build" "$STAGE/root"

while IFS= read -r line; do
    line=${line%$'\r'}
    case "$line" in
        managed\ *)
            relative=$(printf '%s\n' "$line" | awk '{print $2}')
            if ! is_safe_manifest_path "$relative"; then
                echo "build/update.sh: manifest contains an unsafe path: $relative" >&2
                exit 1
            fi
            source=$(source_path "$FETCH_DIR" "$relative")
            if [ -z "$source" ] || [ ! -f "$source" ]; then
                echo "build/update.sh: source scaffold is missing $relative." >&2
                exit 1
            fi
            if is_preserved_in "$SOURCE_LOCK" "$relative"; then
                installed_file=$(installed_path "$relative")
                if [ -e "$installed_file" ] || [ -L "$installed_file" ]; then
                    if [ ! -f "$installed_file" ]; then
                        echo "build/update.sh: preserved path is not a regular file: $relative" >&2
                        exit 1
                    fi
                    continue
                fi
            fi
            mkdir -p "$STAGE/$(dirname "$relative")"
            cp -p "$source" "$STAGE/$relative"
            ;;
        preserve\ *)
            relative=$(printf '%s\n' "$line" | awk '{print $2}')
            if ! is_safe_manifest_path "$relative"; then
                echo "build/update.sh: manifest contains an unsafe path: $relative" >&2
                exit 1
            fi
            installed_file=$(installed_path "$relative")
            if [ -e "$installed_file" ] || [ -L "$installed_file" ]; then
                if [ ! -f "$installed_file" ]; then
                    echo "build/update.sh: preserved path is not a regular file: $relative" >&2
                    exit 1
                fi
                continue
            fi
            source=$(source_path "$FETCH_DIR" "$relative")
            if [ -f "$source" ]; then
                mkdir -p "$STAGE/$(dirname "$relative")"
                cp -p "$source" "$STAGE/$relative"
            fi
            ;;
    esac
done < "$SOURCE_LOCK"

cp -p "$SOURCE_LOCK" "$STAGE/build/plutonium.lock"

# Keep the consumer's source identity for a no-arg update and re-home it for an
# explicit --source, move the commit to the requested one, and make the lock
# describe the files that will actually be on disk. Any path carrying both
# managed and preserve tags keeps the on-disk copy, so its hash must describe
# that copy rather than the source copy.
awk -v sha="$SHA" \
    -v source="$LOCK_SOURCE" \
    -v source_url="$LOCK_SOURCE_URL" '
    /^source=/ { print "source=" source; next }
    /^source_url=/ { print "source_url=" source_url; next }
    /^scaffold_commit=/ { print "scaffold_commit=" sha; next }
    { print }
' "$SOURCE_LOCK" > "$STAGE/build/plutonium.lock.new"
mv "$STAGE/build/plutonium.lock.new" "$STAGE/build/plutonium.lock"

while IFS= read -r preserve_relative; do
    preserve_relative=${preserve_relative%$'\r'}
    [ -n "$preserve_relative" ] || continue
    if ! lock_has_managed "$SOURCE_LOCK" "$preserve_relative"; then
        continue
    fi
    staged_file="$STAGE/$preserve_relative"
    installed_file=$(installed_path "$preserve_relative")
    if [ -f "$staged_file" ]; then
        preserved_hash=$(sha256_file "$staged_file")
    elif [ -f "$installed_file" ]; then
        preserved_hash=$(sha256_file "$installed_file")
    else
        continue
    fi
    rewrite_managed_hash "$STAGE/build/plutonium.lock" "$preserve_relative" "$preserved_hash"
done < <(awk '{ sub(/\r$/, "") } $1 == "preserve" { print $2 }' "$SOURCE_LOCK")

verify_staged_manifest() {
    local lock="$1" line relative expected staged_file installed_file actual
    while IFS= read -r line; do
        line=${line%$'\r'}
        case "$line" in
            managed\ *) ;;
            *) continue ;;
        esac
        relative=$(printf '%s\n' "$line" | awk '{print $2}')
        expected=$(printf '%s\n' "$line" | awk '{print $3}')
        staged_file="$STAGE/$relative"
        installed_file=$(installed_path "$relative")
        if [ -f "$staged_file" ]; then
            actual=$(sha256_file "$staged_file")
        elif [ -f "$installed_file" ]; then
            actual=$(sha256_file "$installed_file")
        else
            echo "build/update.sh: staged lock names a file that is neither staged nor installed: $relative" >&2
            return 1
        fi
        if [ "$actual" != "$expected" ]; then
            echo "build/update.sh: staged hash for $relative does not match the new lock." >&2
            return 1
        fi
    done < "$lock"
    while IFS= read -r -d '' staged_file; do
        relative=${staged_file#"$STAGE/"}
        [ "$relative" = "build/plutonium.lock" ] && continue
        if ! lock_has_path "$lock" "$relative"; then
            echo "build/update.sh: staged file is not listed by the new lock: $relative" >&2
            return 1
        fi
    done < <(find "$STAGE" -type f -print0)
}

if [ -f "$STAGE/build/build.csproj" ]; then
    sed -E \
        "s#(<PackageReference[^>]*Include=\"PlutoniumBuild\"[^>]*Version=\")[^\"]*(\")#\\1${NEW_PACKAGE_VERSION}\\2#" \
        "$STAGE/build/build.csproj" > "$STAGE/build/build.csproj.attr"
    mv "$STAGE/build/build.csproj.attr" "$STAGE/build/build.csproj"
    sed -E \
        "/Include=\"PlutoniumBuild\"/,/<\/PackageReference>/ s#(<Version>)[^<]*(</Version>)#\\1${NEW_PACKAGE_VERSION}\\2#" \
        "$STAGE/build/build.csproj" > "$STAGE/build/build.csproj.child"
    mv "$STAGE/build/build.csproj.child" "$STAGE/build/build.csproj"
    if ! grep -q "Include=\"PlutoniumBuild\"" "$STAGE/build/build.csproj" ||
       ! grep -q "${NEW_PACKAGE_VERSION}" "$STAGE/build/build.csproj"; then
        echo "build/update.sh: could not bump the PlutoniumBuild package pin to $NEW_PACKAGE_VERSION." >&2
        exit 1
    fi
    csproj_hash=$(sha256_file "$STAGE/build/build.csproj")
    awk -v hash="$csproj_hash" '
        /^managed build\/build\.csproj / { print "managed build/build.csproj " hash; next }
        { print }
    ' "$STAGE/build/plutonium.lock" > "$STAGE/build/plutonium.lock.csproj"
    mv "$STAGE/build/plutonium.lock.csproj" "$STAGE/build/plutonium.lock"
fi

verify_staged_manifest "$STAGE/build/plutonium.lock" || exit 1

APPLIED_FILE="$BACKUP/applied.tsv"
: > "$APPLIED_FILE"

action_had_backup() {
    local relative="$1"
    [ -f "$ACTIONS_FILE" ] || return 1
    awk -F '\t' -v path="$relative" '
        $1 == path && $2 == 1 { found = 1; exit }
        END { exit(found ? 0 : 1) }
    ' "$ACTIONS_FILE"
}

prune_empty_parents() {
    local directory="$1"
    while [ "$directory" != "$BUILD_DIR" ] && [ "$directory" != "$TARGET" ] && [ -d "$directory" ]; do
        rmdir -- "$directory" 2>/dev/null || break
        directory=$(dirname "$directory")
    done
}

rollback() {
    local relative had_backup destination
    if [ -f "$APPLIED_FILE" ]; then
        while IFS= read -r relative; do
            [ -n "$relative" ] || continue
            if ! action_had_backup "$relative"; then
                destination=$(installed_path "$relative")
                if [ -n "$destination" ] && { [ -e "$destination" ] || [ -L "$destination" ]; }; then
                    rm -f -- "$destination"
                    prune_empty_parents "$(dirname "$destination")"
                fi
            fi
        done < "$APPLIED_FILE"
    fi
    if [ -f "$ACTIONS_FILE" ]; then
        while IFS=$'\t' read -r relative had_backup; do
            [ -n "$relative" ] || continue
            destination=$(installed_path "$relative")
            if [ "$had_backup" = "1" ] && [ -f "$BACKUP/files/$relative" ]; then
                mkdir -p "$(dirname "$destination")"
                cp -p "$BACKUP/files/$relative" "$destination"
            elif [ "$had_backup" = "0" ]; then
                rm -f -- "$destination"
                prune_empty_parents "$(dirname "$destination")"
            fi
        done < "$ACTIONS_FILE"
    fi
    echo "PlutoniumBuild: update rolled back; the previous scaffold is restored." >&2
}

UPDATE_STARTED=0
UPDATE_COMMITTED=0
_plutonium_finish() {
    local status=$?
    trap - EXIT HUP INT TERM
    if [ "$UPDATE_STARTED" -eq 1 ] && [ "$UPDATE_COMMITTED" -eq 0 ]; then
        rollback || true
    fi
    cleanup || true
    exit "$status"
}

_install_finish_trap() {
    trap '_plutonium_finish' EXIT
    trap 'exit 130' HUP INT TERM
}

_install_finish_trap

apply_staged() {
    while IFS= read -r -d '' staged; do
        relative=${staged#"$STAGE/"}
        destination=$(installed_path "$relative")
        if [ -z "$destination" ]; then
            echo "build/update.sh: staged file outside the scaffold layout: $relative" >&2
            return 1
        fi
        mkdir -p "$(dirname "$destination")" || return 1
        mv -f "$staged" "$destination" || return 1
        printf '%s\n' "$relative" >> "$APPLIED_FILE"
    done < <(find "$STAGE" -type f -print0 | sort -z)
}

UPDATE_STARTED=1
if ! apply_staged; then
    exit 1
fi

find "$BUILD_DIR" -name '*.sh' -type f -exec chmod 755 {} + 2>/dev/null || true
chmod 755 "$TARGET/build.sh" 2>/dev/null || true

run_smoke() {
    [ "$NO_SMOKE" -eq 1 ] && return 0

    local assets="$BUILD_DIR/obj/project.assets.json"
    local project="$BUILD_DIR/build.csproj"
    [ -f "$project" ] || return 0

    if [ "$FORCE_SMOKE" -eq 1 ] || [ -f "$assets" ]; then
        # shellcheck source=bootstrap.sh
        . "$BUILD_DIR/bootstrap.sh"
        if ! plutonium_bootstrap "$BUILD_DIR/global.json" "$BUILD_DIR/temp" "$LOCK_FILE"; then
            return 1
        fi
        # The bootstrap installs its own EXIT cleanup trap; restore the
        # rollback trap before the smoke build can fail or be interrupted.
        _install_finish_trap
        "$PLUTONIUM_DOTNET_EXE" build "$project" \
            -nodeReuse:false -p:UseSharedCompilation=false -nologo -clp:NoSummary --verbosity quiet
        return $?
    fi

    if command -v dotnet >/dev/null 2>&1; then
        dotnet msbuild "$project" -getProperty:TargetFramework -nologo >/dev/null || return 1
        return 0
    fi

    echo "PlutoniumBuild: smoke test skipped (no restored assets and no dotnet on PATH)." >&2
}

smoke_ok=1
if ! run_smoke; then
    smoke_ok=0
fi
_install_finish_trap
if [ "$smoke_ok" -ne 1 ]; then
    exit 1
fi

{
    echo "updated_at=$STAMP"
    echo "from_commit=$CURRENT_COMMIT"
    echo "to_commit=$SHA"
    echo "source=$SOURCE"
    echo "package_version=$NEW_PACKAGE_VERSION"
    echo "status=ok"
} > "$BACKUP/journal.txt"

UPDATE_COMMITTED=1
echo "PlutoniumBuild: updated scaffold from $CURRENT_COMMIT to $SHA."
echo "PlutoniumBuild: previous files are journaled under build/temp/update/$STAMP."

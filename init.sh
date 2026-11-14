#!/usr/bin/env bash
# One-line scaffold installer. Resolves a source ref to a commit, verifies
# every managed file against the lock, refuses to overwrite an existing
# scaffold, and installs the payload atomically.

set -euo pipefail

DEFAULT_SOURCE="https://github.com/Kiryuumaru/PlutoniumBuild.git"
DEFAULT_REF="refs/heads/main"
SOURCE="${PLUTONIUM_SOURCE:-$DEFAULT_SOURCE}"
SHA="${PLUTONIUM_SHA:-}"
REQUESTED_REF="${PLUTONIUM_REF:-}"
TARGET="${PLUTONIUM_TARGET:-$PWD}"

usage() {
    cat <<'EOF'
usage: ./init.sh [--ref <branch|tag|commit> | --sha <40-character-commit>] [options]

options:
  --source <url>    source repository (default: the PlutoniumBuild repository)
  --ref <ref>       source branch, tag, or full commit (default: main)
  --sha <sha>       source commit to install (exactly 40 characters)
  --target <dir>    consumer repository (default: current directory)
EOF
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --source)
            [ "$#" -ge 2 ] || { echo "init.sh: --source needs a value." >&2; exit 2; }
            SOURCE="$2"
            shift 2
            ;;
        --sha)
            [ "$#" -ge 2 ] || { echo "init.sh: --sha needs a value." >&2; exit 2; }
            SHA="$2"
            shift 2
            ;;
        --ref)
            [ "$#" -ge 2 ] || { echo "init.sh: --ref needs a value." >&2; exit 2; }
            REQUESTED_REF="$2"
            shift 2
            ;;
        --target)
            [ "$#" -ge 2 ] || { echo "init.sh: --target needs a value." >&2; exit 2; }
            TARGET="$2"
            shift 2
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            echo "init.sh: unknown argument: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

if [ -n "$SHA" ] && [ -n "$REQUESTED_REF" ]; then
    echo "init.sh: --sha and --ref are mutually exclusive." >&2
    exit 2
fi
if [ -n "$SHA" ]; then
    if ! [[ "$SHA" =~ ^[0-9a-fA-F]{40}$ ]]; then
        echo "init.sh: --sha must be the full 40-character source commit SHA." >&2
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
    echo "init.sh: --ref must be a branch or tag name, a refs/heads/... branch, a refs/tags/... tag, or a full 40-character commit SHA." >&2
    exit 2
fi

if [ -n "$REQUESTED_REF" ]; then
    REF_LABEL="$REQUESTED_REF"
elif [ -n "$SHA" ]; then
    REF_LABEL="$SHA"
else
    REF_LABEL="$DEFAULT_REF (default)"
fi

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

case "$SOURCE" in
    -*) echo "init.sh: --source must not start with '-'." >&2; exit 2 ;;
esac
if ! validate_source_url "$SOURCE"; then
    echo "init.sh: --source must not contain credentials (userinfo, query, or fragment)." >&2
    exit 2
fi

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
                echo "init.sh: could not query $ref from $source." >&2
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
                echo "init.sh: ref $ref is ambiguous: refs/heads/$ref and refs/tags/$ref both exist; pass refs/heads/$ref or refs/tags/$ref." >&2
                return 2
            fi
            if [ "$branch_found" -eq 1 ]; then
                ref="refs/heads/$ref"
            elif [ "$tag_found" -eq 1 ]; then
                ref="refs/tags/$ref"
            else
                echo "init.sh: ref $ref was not found in $source." >&2
                return 1
            fi
            ;;
    esac

    if ! output=$(GIT_TERMINAL_PROMPT=0 git ls-remote -- "$source" "$ref" "$ref^{}" 2>/dev/null); then
        echo "init.sh: could not query $ref from $source." >&2
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
        echo "init.sh: ref $ref was not found in $source." >&2
        return 1
    fi
    printf '%s\n' "$sha"
}

[ -d "$TARGET" ] || { echo "init.sh: target directory not found: $TARGET" >&2; exit 1; }
TARGET=$(cd "$TARGET" && pwd -P)

refuse() {
    echo "PlutoniumBuild init: refusing to install: $*" >&2
    exit 1
}

has_path() {
    [ -e "$1" ] || [ -L "$1" ]
}

has_path "$TARGET/build" && refuse "build/ already exists (run build/update.sh to update an initialized scaffold)."
for wrapper in build.sh build.ps1 build.cmd; do
    has_path "$TARGET/$wrapper" && refuse "root $wrapper already exists; move it aside before initializing."
done

command -v git >/dev/null 2>&1 || {
    echo "init.sh: git is required to fetch the scaffold source." >&2
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
echo "PlutoniumBuild: source $SOURCE" >&2
echo "PlutoniumBuild: ref $REF_LABEL" >&2
echo "PlutoniumBuild: commit $SHA" >&2

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
                printf 'init.sh: source scaffold contains a path with control characters: %q; refusing to install it.\n' "$relative" >&2
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

lock_has_path() {
    local file="$1" relative="$2"
    awk -v path="$relative" '
        { sub(/\r$/, "") }
        ($1 == "managed" || $1 == "preserve") && $2 == path { found = 1; exit }
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

require_version_invariant() {
    local root="$1" lock="$2" package_version pin props
    package_version=$(lock_get "$lock" package_version)
    if [ -z "$package_version" ]; then
        echo "init.sh: source lock has no package_version." >&2
        return 1
    fi
    props=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$root/src/Directory.Build.props" | head -1)
    if [ -z "$props" ]; then
        echo "init.sh: source src/Directory.Build.props has no package version." >&2
        return 1
    fi
    if [ "$props" != "$package_version" ]; then
        echo "init.sh: source src/Directory.Build.props version $props does not match lock package_version $package_version." >&2
        return 1
    fi
    pin=$(package_pin "$root/scaffold/build/build.csproj")
    if [ -z "$pin" ]; then
        echo "init.sh: source build.csproj has no PlutoniumBuild package pin." >&2
        return 1
    fi
    if [ "$pin" != "$package_version" ]; then
        echo "init.sh: source package pin $pin does not match lock package_version $package_version." >&2
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
            echo "init.sh: source scaffold contains an unlisted file: $relative" >&2
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
            echo "init.sh: manifest contains an unsafe preserve path: $relative" >&2
            failures=$((failures + 1))
            continue
        fi
        source=$(source_path "$root" "$relative")
        if [ -z "$source" ] || [ ! -f "$source" ]; then
            echo "init.sh: source scaffold is missing preserved file $relative." >&2
            failures=$((failures + 1))
        fi
    done < "$lock"

    [ "$failures" -eq 0 ]
}

FETCH_DIR=$(mktemp -d "${TMPDIR:-/tmp}/plutonium-init.XXXXXXXX") || {
    echo "init.sh: could not create a temporary directory." >&2
    exit 1
}
chmod 700 "$FETCH_DIR"
STAGE=""
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
    echo "init.sh: could not fetch commit $SHA from $SOURCE." >&2
    exit 1
fi
git -C "$FETCH_DIR" checkout -q --detach "$SHA" 2>/dev/null || git -C "$FETCH_DIR" checkout -q --detach FETCH_HEAD

HEAD_SHA=$(git -C "$FETCH_DIR" rev-parse HEAD | tr -d '\r')
if [ "$HEAD_SHA" != "$SHA" ]; then
    echo "init.sh: fetched $HEAD_SHA but expected $SHA." >&2
    exit 1
fi

SOURCE_BUILD="$FETCH_DIR/scaffold/build"
SOURCE_ROOT="$FETCH_DIR/scaffold/root"
SOURCE_LOCK="$SOURCE_BUILD/plutonium.lock"
if [ ! -d "$SOURCE_BUILD" ] || [ ! -d "$SOURCE_ROOT" ] || [ ! -f "$SOURCE_LOCK" ]; then
    echo "init.sh: commit $SHA does not contain the scaffold payload." >&2
    exit 1
fi
validate_lock "$SOURCE_LOCK" "init.sh: source lock" || exit 1
if find "$FETCH_DIR/scaffold" -type l -print -quit | grep -q .; then
    echo "init.sh: scaffold payload contains a symbolic link; refusing to install it." >&2
    exit 1
fi
if find "$FETCH_DIR/scaffold" ! -type f ! -type d -print -quit | grep -q .; then
    echo "init.sh: scaffold payload contains a non-regular file; refusing to install it." >&2
    exit 1
fi
reject_unsafe_source_paths "$FETCH_DIR/scaffold" || exit 1
normalize_line_endings "$FETCH_DIR/scaffold"
if [ "$(lock_get "$SOURCE_LOCK" format)" != "1" ]; then
    echo "init.sh: source lock has an unsupported format." >&2
    exit 1
fi
require_version_invariant "$FETCH_DIR" "$SOURCE_LOCK" || exit 1
check_source_contract "$FETCH_DIR" "$SOURCE_LOCK" || exit 1

while IFS= read -r line; do
    line=${line%$'\r'}
    case "$line" in
        managed\ *) ;;
        *) continue ;;
    esac
    relative=$(printf '%s\n' "$line" | awk '{print $2}')
    expected=$(printf '%s\n' "$line" | awk '{print $3}')
    if ! is_safe_manifest_path "$relative"; then
        echo "init.sh: manifest contains an unsafe path: $relative" >&2
        exit 1
    fi
    source=$(source_path "$FETCH_DIR" "$relative")
    if [ -z "$source" ] || [ ! -f "$source" ]; then
        echo "init.sh: source scaffold is missing $relative." >&2
        exit 1
    fi
    case "$expected" in
        *[!0-9a-fA-F]*)
            echo "init.sh: invalid manifest hash for $relative." >&2
            exit 1
            ;;
    esac
    if [ "${#expected}" -ne 64 ]; then
        echo "init.sh: invalid manifest hash for $relative." >&2
        exit 1
    fi
    actual=$(sha256_file "$source") || {
        echo "init.sh: no SHA-256 tool is available." >&2
        exit 1
    }
    if [ "$actual" != "$expected" ]; then
        echo "init.sh: manifest hash mismatch for $relative; refusing to install the scaffold." >&2
        exit 1
    fi
done < "$SOURCE_LOCK"

for wrapper in build.sh build.ps1 build.cmd; do
    if [ ! -f "$SOURCE_ROOT/$wrapper" ]; then
        echo "init.sh: source scaffold is missing root $wrapper." >&2
        exit 1
    fi
done

STAGE=$(mktemp -d "$TARGET/.plutonium-init.XXXXXXXX") || {
    echo "init.sh: could not create a staging directory in $TARGET." >&2
    exit 1
}
chmod 700 "$STAGE"
mkdir -p "$STAGE/build"
while IFS= read -r line; do
    line=${line%$'\r'}
    case "$line" in
        managed\ *|preserve\ *) ;;
        *) continue ;;
    esac
    relative=$(printf '%s\n' "$line" | awk '{print $2}')
    source=$(source_path "$FETCH_DIR" "$relative")
    if [ -z "$source" ] || [ ! -f "$source" ]; then
        echo "init.sh: source scaffold is missing $relative." >&2
        exit 1
    fi
    destination="$STAGE/$relative"
    mkdir -p "$(dirname "$destination")"
    cp -p "$source" "$destination"
done < "$SOURCE_LOCK"
cp -p "$SOURCE_LOCK" "$STAGE/build/plutonium.lock"

while IFS= read -r -d '' staged; do
    relative=${staged#"$STAGE/"}
    [ "$relative" = "build/plutonium.lock" ] && continue
    if ! lock_has_path "$SOURCE_LOCK" "$relative"; then
        echo "init.sh: staged file is not listed by the source lock: $relative" >&2
        exit 1
    fi
done < <(find "$STAGE" -type f -print0)

manifest_source="$SOURCE"
if [ "$SOURCE" = "$DEFAULT_SOURCE" ]; then
    manifest_source="github.com/Kiryuumaru/PlutoniumBuild"
fi
awk -v source="$manifest_source" -v source_url="$SOURCE" -v sha="$SHA" '
    BEGIN { wrote_source = 0; wrote_url = 0; wrote_sha = 0 }
    /^source=/ { print "source=" source; wrote_source = 1; next }
    /^source_url=/ { print "source_url=" source_url; wrote_url = 1; next }
    /^scaffold_commit=/ { print "scaffold_commit=" sha; wrote_sha = 1; next }
    { print }
    END {
        if (!wrote_source) print "source=" source
        if (!wrote_url) print "source_url=" source_url
        if (!wrote_sha) print "scaffold_commit=" sha
    }
' "$STAGE/build/plutonium.lock" > "$STAGE/build/plutonium.lock.new"
mv "$STAGE/build/plutonium.lock.new" "$STAGE/build/plutonium.lock"

has_path "$TARGET/build" && refuse "build/ was created while init was running."
for wrapper in build.sh build.ps1 build.cmd; do
    has_path "$TARGET/$wrapper" && refuse "root $wrapper was created while init was running."
done

INSTALLED_BUILD=0
MOVED_WRAPPERS=""
rollback_install() {
    if [ "$INSTALLED_BUILD" -eq 1 ]; then
        rm -rf -- "$TARGET/build"
    fi
    for installed_wrapper in $MOVED_WRAPPERS; do
        rm -f -- "$TARGET/$installed_wrapper"
    done
}

if ! mv "$STAGE/build" "$TARGET/build"; then
    rollback_install
    echo "init.sh: could not install build/." >&2
    exit 1
fi
INSTALLED_BUILD=1
for wrapper in build.sh build.ps1 build.cmd; do
    if has_path "$TARGET/$wrapper"; then
        rollback_install
        refuse "root $wrapper was created while init was running."
    fi
    if ! mv "$STAGE/$wrapper" "$TARGET/$wrapper"; then
        rollback_install
        echo "init.sh: could not install root $wrapper." >&2
        exit 1
    fi
    MOVED_WRAPPERS="$MOVED_WRAPPERS $wrapper"
done

find "$TARGET/build" -name '*.sh' -type f -exec chmod 755 {} + 2>/dev/null || true
chmod 755 "$TARGET/build.sh" 2>/dev/null || true
chmod 644 "$TARGET/build.ps1" "$TARGET/build.cmd" 2>/dev/null || true

cat >&2 <<EOF
PlutoniumBuild: installed scaffold at commit $SHA.
Next steps:
  1. Review build/build.cs and replace the sample terminal calls.
  2. Run ./build.sh (or build\\build.cmd on Windows) to build and test.
  3. Run ./build.sh Publish when your own publish entry is ready.
EOF

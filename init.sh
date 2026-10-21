#!/usr/bin/env bash
# One-line scaffold installer. Downloads a pinned source commit, verifies every
# managed file against the lock, refuses to overwrite an existing scaffold, and
# installs the payload atomically.

set -euo pipefail

DEFAULT_SOURCE="https://github.com/Kiryuumaru/PlutoniumBuild.git"
SOURCE="${PLUTONIUM_SOURCE:-$DEFAULT_SOURCE}"
SHA="${PLUTONIUM_SHA:-}"
TARGET="${PLUTONIUM_TARGET:-$PWD}"

usage() {
    cat <<'EOF'
usage: ./init.sh --sha <40-character-commit> [options]

options:
  --source <url>    source repository (default: the PlutoniumBuild repository)
  --sha <sha>       source commit to install (required)
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

if ! [[ "$SHA" =~ ^[0-9a-fA-F]{40}$ ]]; then
    echo "init.sh: --sha must be the full 40-character source commit SHA." >&2
    exit 2
fi
SHA=$(printf '%s' "$SHA" | tr 'A-F' 'a-f')

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

if ! validate_source_url "$SOURCE"; then
    echo "init.sh: --source must not contain credentials (userinfo, query, or fragment)." >&2
    exit 2
fi

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

is_safe_manifest_path() {
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

lock_get() {
    local file="$1" key="$2"
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

lock_has_path() {
    local file="$1" relative="$2"
    awk -v path="$relative" '
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
    while IFS= read -r file; do
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
    done < <(find "$root/scaffold/build" -type f
        find "$root/scaffold/root" -type f)

    while IFS= read -r line; do
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
git -C "$FETCH_DIR" remote add origin "$SOURCE"
if ! GIT_TERMINAL_PROMPT=0 git -C "$FETCH_DIR" fetch --depth 1 --no-tags origin "$SHA" >/dev/null 2>&1; then
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
if find "$FETCH_DIR/scaffold" -type l -print -quit | grep -q .; then
    echo "init.sh: scaffold payload contains a symbolic link; refusing to install it." >&2
    exit 1
fi
if find "$FETCH_DIR/scaffold" ! -type f ! -type d -print -quit | grep -q .; then
    echo "init.sh: scaffold payload contains a non-regular file; refusing to install it." >&2
    exit 1
fi
if [ "$(lock_get "$SOURCE_LOCK" format)" != "1" ]; then
    echo "init.sh: source lock has an unsupported format." >&2
    exit 1
fi
require_version_invariant "$FETCH_DIR" "$SOURCE_LOCK" || exit 1
check_source_contract "$FETCH_DIR" "$SOURCE_LOCK" || exit 1

while IFS= read -r line; do
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

while IFS= read -r staged; do
    relative=${staged#"$STAGE/"}
    [ "$relative" = "build/plutonium.lock" ] && continue
    if ! lock_has_path "$SOURCE_LOCK" "$relative"; then
        echo "init.sh: staged file is not listed by the source lock: $relative" >&2
        exit 1
    fi
done < <(find "$STAGE" -type f)

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

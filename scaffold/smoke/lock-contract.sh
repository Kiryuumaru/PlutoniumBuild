#!/usr/bin/env bash
# Regression smoke: the lock must cover every scaffold payload file, and
# init/update must reject a source tree with an unlisted file.

set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
WORK=$(mktemp -d "${TMPDIR:-/tmp}/plutonium-lock-contract.XXXXXXXX")
OUT="$WORK/lock-contract.out"
trap 'rm -rf -- "$WORK"' EXIT

failures=0
fail() {
    echo "lock-contract: $*" >&2
    failures=$((failures + 1))
}

copy_scaffold() {
    local destination="$1"
    mkdir -p "$destination/src"
    cp -a "$ROOT/scaffold" "$destination/scaffold"
    cp "$ROOT/src/Directory.Build.props" "$destination/src/Directory.Build.props"
}

poison_lock() {
    local file="$1" poison="$2"
    case "$poison" in
        prefixed-source) sed -i 's/^source=/ source=/' "$file" ;;
        prefixed-scaffold-commit) sed -i 's/^scaffold_commit=/ scaffold_commit=/' "$file" ;;
        non-canonical-source) sed -i 's/^source=/source =/' "$file" ;;
        duplicate-format) printf 'format=1\n' >> "$file" ;;
        short-scaffold-commit) sed -i 's/^scaffold_commit=.*/scaffold_commit=abc1234/' "$file" ;;
        missing-format) sed -i '/^format=/d' "$file" ;;
        *) fail "unknown lock poison '$poison'."; return 1 ;;
    esac
}

poison_message() {
    case "$1" in
        prefixed-source) echo 'reserved key source must start at column 0' ;;
        prefixed-scaffold-commit) echo 'reserved key scaffold_commit must start at column 0' ;;
        non-canonical-source) echo 'reserved key source has a non-canonical form' ;;
        duplicate-format) echo 'duplicate reserved key format' ;;
        short-scaffold-commit) echo 'scaffold_commit must be a full 40-character commit SHA' ;;
        missing-format) echo 'missing required key format' ;;
    esac
}

# Lock verify must reject an unlisted build payload file.
copy_scaffold "$WORK/unlisted-build"
echo 'unlisted' > "$WORK/unlisted-build/scaffold/build/extra.txt"
if bash "$WORK/unlisted-build/scaffold/lock.sh" verify >"$OUT" 2>&1; then
    fail "lock verify accepted an unlisted build file."
elif ! grep -q 'not managed or preserved' "$OUT"; then
    fail "lock verify failed without naming the unlisted build file."
fi
rm -f "$OUT"

# Lock verify must reject an unlisted root wrapper.
copy_scaffold "$WORK/unlisted-root"
echo '# unlisted' > "$WORK/unlisted-root/scaffold/root/extra.sh"
if bash "$WORK/unlisted-root/scaffold/lock.sh" verify >"$OUT" 2>&1; then
    fail "lock verify accepted an unlisted root wrapper."
elif ! grep -q 'root wrapper extra.sh' "$OUT"; then
    fail "lock verify failed without naming the unlisted root wrapper."
fi
rm -f "$OUT"

# Generated-looking directories are not an exemption from the contract.
copy_scaffold "$WORK/unlisted-bin"
mkdir -p "$WORK/unlisted-bin/scaffold/build/bin"
echo 'unlisted' > "$WORK/unlisted-bin/scaffold/build/bin/extra.txt"
if bash "$WORK/unlisted-bin/scaffold/lock.sh" verify >"$OUT" 2>&1; then
    fail "lock verify accepted an unlisted file under build/bin."
elif ! grep -q 'build/bin/extra.txt is not managed or preserved' "$OUT"; then
    fail "lock verify failed without naming the unlisted build/bin file."
fi
rm -f "$OUT"

# Duplicate managed entries make the hash ambiguous.
copy_scaffold "$WORK/duplicate"
duplicate_line=$(grep '^managed build/build.sh ' "$WORK/duplicate/scaffold/build/plutonium.lock")
printf '%s\n' "$duplicate_line" >> "$WORK/duplicate/scaffold/build/plutonium.lock"
if bash "$WORK/duplicate/scaffold/lock.sh" verify >"$OUT" 2>&1; then
    fail "lock verify accepted duplicate managed entries."
elif ! grep -q 'duplicate managed entries' "$OUT"; then
    fail "lock verify did not report duplicate managed entries."
fi
rm -f "$OUT"

# Reserved keys must be canonical, unique, and present; verify and write must
# reject a poisoned lock without rewriting it.
for poison in \
    prefixed-source \
    prefixed-scaffold-commit \
    non-canonical-source \
    duplicate-format \
    short-scaffold-commit \
    missing-format; do
    poisoned="$WORK/poison-$poison"
    copy_scaffold "$poisoned"
    poisoned_lock="$poisoned/scaffold/build/plutonium.lock"
    poison_lock "$poisoned_lock" "$poison"
    poisoned_hash=$(sha256sum "$poisoned_lock" | awk '{print $1}')
    expected=$(poison_message "$poison")
    if bash "$poisoned/scaffold/lock.sh" verify >"$OUT" 2>&1; then
        fail "lock verify accepted $poison."
    elif ! grep -Fq "$expected" "$OUT"; then
        fail "lock verify did not report $poison."
    fi
    if bash "$poisoned/scaffold/lock.sh" write >"$OUT" 2>&1; then
        fail "lock write accepted $poison."
    elif ! grep -Fq "$expected" "$OUT"; then
        fail "lock write did not report $poison."
    fi
    if [ "$(sha256sum "$poisoned_lock" | awk '{print $1}')" != "$poisoned_hash" ]; then
        fail "lock write changed the lock for $poison."
    fi
done
rm -f "$OUT"

# src/Directory.Build.props participates in the package-version invariant.
copy_scaffold "$WORK/props"
sed -i 's/<Version>0.1.0</<Version>9.9.9</' "$WORK/props/src/Directory.Build.props"
if bash "$WORK/props/scaffold/lock.sh" verify >"$OUT" 2>&1; then
    fail "lock verify accepted a mismatched src/Directory.Build.props version."
elif ! grep -q 'src/Directory.Build.props version 9.9.9 does not match' "$OUT"; then
    fail "lock verify did not report the props version mismatch."
fi
if bash "$WORK/props/scaffold/lock.sh" write >"$OUT" 2>&1; then
    fail "lock write accepted a mismatched src/Directory.Build.props version."
fi
rm -f "$OUT"

# Both write and verify must enforce the package-version invariant.
copy_scaffold "$WORK/version"
sed -i 's/Version="0.1.0"/Version="9.9.9"/' "$WORK/version/scaffold/build/build.csproj"
if bash "$WORK/version/scaffold/lock.sh" verify >"$OUT" 2>&1; then
    fail "lock verify accepted a mismatched package pin."
elif ! grep -q 'does not match package_version' "$OUT"; then
    fail "lock verify did not report the package pin mismatch."
fi
if bash "$WORK/version/scaffold/lock.sh" write >"$OUT" 2>&1; then
    fail "lock write accepted a mismatched package pin."
fi
rm -f "$OUT"

# init and update must reject a source tree with an unlisted payload file.
SOURCE="$WORK/source"
CONSUMER="$WORK/consumer"
copy_scaffold "$SOURCE"
cp "$ROOT/init.sh" "$SOURCE/init.sh"
git -C "$SOURCE" init -q
git -C "$SOURCE" config user.email smoke@example.test
git -C "$SOURCE" config user.name Smoke
git -C "$SOURCE" add -A
git -C "$SOURCE" commit -qm initial
SHA1=$(git -C "$SOURCE" rev-parse HEAD)

mkdir -p "$CONSUMER"
bash "$ROOT/init.sh" --source "$SOURCE" --sha "$SHA1" --target "$CONSUMER" >/dev/null 2>&1
BEFORE=$(sha256sum "$CONSUMER/build/plutonium.lock" | awk '{print $1}')

echo 'unlisted' > "$SOURCE/scaffold/build/extra.txt"
git -C "$SOURCE" add -A
git -C "$SOURCE" commit -qm unlisted
SHA2=$(git -C "$SOURCE" rev-parse HEAD)

mkdir -p "$WORK/rejected-consumer"
if bash "$ROOT/init.sh" --source "$SOURCE" --sha "$SHA2" --target "$WORK/rejected-consumer" >"$OUT" 2>&1; then
    fail "init accepted an unlisted source file."
elif ! grep -q 'unlisted file: build/extra.txt' "$OUT"; then
    fail "init did not report the unlisted source file."
fi
if [ -e "$WORK/rejected-consumer/build" ]; then
    fail "init installed a rejected source tree."
fi

if bash "$CONSUMER/build/update.sh" --source "$SOURCE" --sha "$SHA2" --target "$CONSUMER" --no-smoke >"$OUT" 2>&1; then
    fail "update accepted an unlisted source file."
elif ! grep -q 'unlisted file: build/extra.txt' "$OUT"; then
    fail "update did not report the unlisted source file."
fi
if [ "$(sha256sum "$CONSUMER/build/plutonium.lock" | awk '{print $1}')" != "$BEFORE" ]; then
    fail "update changed the lock after rejecting the source tree."
fi
rm -f "$OUT"

# init must reject a source whose props version disagrees with the lock.
PROPS_SOURCE="$WORK/props-source"
copy_scaffold "$PROPS_SOURCE"
cp "$ROOT/init.sh" "$PROPS_SOURCE/init.sh"
sed -i 's/<Version>0.1.0</<Version>9.9.9</' "$PROPS_SOURCE/src/Directory.Build.props"
git -C "$PROPS_SOURCE" init -q
git -C "$PROPS_SOURCE" config user.email smoke@example.test
git -C "$PROPS_SOURCE" config user.name Smoke
git -C "$PROPS_SOURCE" add -A
git -C "$PROPS_SOURCE" commit -qm props-mismatch
PROPS_SHA=$(git -C "$PROPS_SOURCE" rev-parse HEAD)
mkdir -p "$WORK/props-consumer"
if bash "$ROOT/init.sh" --source "$PROPS_SOURCE" --sha "$PROPS_SHA" --target "$WORK/props-consumer" >"$OUT" 2>&1; then
    fail "init accepted a source with a mismatched props version."
elif ! grep -q 'Directory.Build.props version 9.9.9 does not match' "$OUT"; then
    fail "init did not report the source props version mismatch."
fi
rm -f "$OUT"

if [ "$failures" -ne 0 ]; then
    echo "lock-contract: $failures failure(s)." >&2
    exit 1
fi

echo "lock-contract: ok"

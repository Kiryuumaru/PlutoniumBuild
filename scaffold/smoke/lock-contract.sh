#!/usr/bin/env bash
# Regression smoke: the lock must cover every scaffold payload file, and
# init/update must reject a source tree with an unlisted file.

set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
TEMP=$(mktemp -d "${TMPDIR:-/tmp}/plutonium-lock-contract.XXXXXXXX")
trap 'rm -rf -- "$TEMP"' EXIT

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

# Lock verify must reject an unlisted build payload file.
copy_scaffold "$TEMP/unlisted-build"
echo 'unlisted' > "$TEMP/unlisted-build/scaffold/build/extra.txt"
if bash "$TEMP/unlisted-build/scaffold/lock.sh" verify >/tmp/plutonium-lock-contract.out 2>&1; then
    fail "lock verify accepted an unlisted build file."
elif ! grep -q 'not managed or preserved' /tmp/plutonium-lock-contract.out; then
    fail "lock verify failed without naming the unlisted build file."
fi
rm -f /tmp/plutonium-lock-contract.out

# Lock verify must reject an unlisted root wrapper.
copy_scaffold "$TEMP/unlisted-root"
echo '# unlisted' > "$TEMP/unlisted-root/scaffold/root/extra.sh"
if bash "$TEMP/unlisted-root/scaffold/lock.sh" verify >/tmp/plutonium-lock-contract.out 2>&1; then
    fail "lock verify accepted an unlisted root wrapper."
elif ! grep -q 'root wrapper extra.sh' /tmp/plutonium-lock-contract.out; then
    fail "lock verify failed without naming the unlisted root wrapper."
fi
rm -f /tmp/plutonium-lock-contract.out

# Generated-looking directories are not an exemption from the contract.
copy_scaffold "$TEMP/unlisted-bin"
mkdir -p "$TEMP/unlisted-bin/scaffold/build/bin"
echo 'unlisted' > "$TEMP/unlisted-bin/scaffold/build/bin/extra.txt"
if bash "$TEMP/unlisted-bin/scaffold/lock.sh" verify >/tmp/plutonium-lock-contract.out 2>&1; then
    fail "lock verify accepted an unlisted file under build/bin."
elif ! grep -q 'build/bin/extra.txt is not managed or preserved' /tmp/plutonium-lock-contract.out; then
    fail "lock verify failed without naming the unlisted build/bin file."
fi
rm -f /tmp/plutonium-lock-contract.out

# Duplicate managed entries make the hash ambiguous.
copy_scaffold "$TEMP/duplicate"
duplicate_line=$(grep '^managed build/build.sh ' "$TEMP/duplicate/scaffold/build/plutonium.lock")
printf '%s\n' "$duplicate_line" >> "$TEMP/duplicate/scaffold/build/plutonium.lock"
if bash "$TEMP/duplicate/scaffold/lock.sh" verify >/tmp/plutonium-lock-contract.out 2>&1; then
    fail "lock verify accepted duplicate managed entries."
elif ! grep -q 'duplicate managed entries' /tmp/plutonium-lock-contract.out; then
    fail "lock verify did not report duplicate managed entries."
fi
rm -f /tmp/plutonium-lock-contract.out

# src/Directory.Build.props participates in the package-version invariant.
copy_scaffold "$TEMP/props"
sed -i 's/<Version>0.1.0</<Version>9.9.9</' "$TEMP/props/src/Directory.Build.props"
if bash "$TEMP/props/scaffold/lock.sh" verify >/tmp/plutonium-lock-contract.out 2>&1; then
    fail "lock verify accepted a mismatched src/Directory.Build.props version."
elif ! grep -q 'src/Directory.Build.props version 9.9.9 does not match' /tmp/plutonium-lock-contract.out; then
    fail "lock verify did not report the props version mismatch."
fi
if bash "$TEMP/props/scaffold/lock.sh" write >/tmp/plutonium-lock-contract.out 2>&1; then
    fail "lock write accepted a mismatched src/Directory.Build.props version."
fi
rm -f /tmp/plutonium-lock-contract.out

# Both write and verify must enforce the package-version invariant.
copy_scaffold "$TEMP/version"
sed -i 's/Version="0.1.0"/Version="9.9.9"/' "$TEMP/version/scaffold/build/build.csproj"
if bash "$TEMP/version/scaffold/lock.sh" verify >/tmp/plutonium-lock-contract.out 2>&1; then
    fail "lock verify accepted a mismatched package pin."
elif ! grep -q 'does not match package_version' /tmp/plutonium-lock-contract.out; then
    fail "lock verify did not report the package pin mismatch."
fi
if bash "$TEMP/version/scaffold/lock.sh" write >/tmp/plutonium-lock-contract.out 2>&1; then
    fail "lock write accepted a mismatched package pin."
fi
rm -f /tmp/plutonium-lock-contract.out

# init and update must reject a source tree with an unlisted payload file.
SOURCE="$TEMP/source"
CONSUMER="$TEMP/consumer"
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

mkdir -p "$TEMP/rejected-consumer"
if bash "$ROOT/init.sh" --source "$SOURCE" --sha "$SHA2" --target "$TEMP/rejected-consumer" >/tmp/plutonium-lock-contract.out 2>&1; then
    fail "init accepted an unlisted source file."
elif ! grep -q 'unlisted file: build/extra.txt' /tmp/plutonium-lock-contract.out; then
    fail "init did not report the unlisted source file."
fi
if [ -e "$TEMP/rejected-consumer/build" ]; then
    fail "init installed a rejected source tree."
fi

if bash "$CONSUMER/build/update.sh" --source "$SOURCE" --sha "$SHA2" --target "$CONSUMER" --no-smoke >/tmp/plutonium-lock-contract.out 2>&1; then
    fail "update accepted an unlisted source file."
elif ! grep -q 'unlisted file: build/extra.txt' /tmp/plutonium-lock-contract.out; then
    fail "update did not report the unlisted source file."
fi
if [ "$(sha256sum "$CONSUMER/build/plutonium.lock" | awk '{print $1}')" != "$BEFORE" ]; then
    fail "update changed the lock after rejecting the source tree."
fi
rm -f /tmp/plutonium-lock-contract.out

# init must reject a source whose props version disagrees with the lock.
PROPS_SOURCE="$TEMP/props-source"
copy_scaffold "$PROPS_SOURCE"
cp "$ROOT/init.sh" "$PROPS_SOURCE/init.sh"
sed -i 's/<Version>0.1.0</<Version>9.9.9</' "$PROPS_SOURCE/src/Directory.Build.props"
git -C "$PROPS_SOURCE" init -q
git -C "$PROPS_SOURCE" config user.email smoke@example.test
git -C "$PROPS_SOURCE" config user.name Smoke
git -C "$PROPS_SOURCE" add -A
git -C "$PROPS_SOURCE" commit -qm props-mismatch
PROPS_SHA=$(git -C "$PROPS_SOURCE" rev-parse HEAD)
mkdir -p "$TEMP/props-consumer"
if bash "$ROOT/init.sh" --source "$PROPS_SOURCE" --sha "$PROPS_SHA" --target "$TEMP/props-consumer" >/tmp/plutonium-lock-contract.out 2>&1; then
    fail "init accepted a source with a mismatched props version."
elif ! grep -q 'Directory.Build.props version 9.9.9 does not match' /tmp/plutonium-lock-contract.out; then
    fail "init did not report the source props version mismatch."
fi
rm -f /tmp/plutonium-lock-contract.out

if [ "$failures" -ne 0 ]; then
    echo "lock-contract: $failures failure(s)." >&2
    exit 1
fi

echo "lock-contract: ok"

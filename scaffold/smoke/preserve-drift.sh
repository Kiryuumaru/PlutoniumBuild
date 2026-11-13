#!/usr/bin/env bash
# Regression smoke: preserve entries come from the lock, and a file carrying
# both managed and preserve tags keeps the installed copy while its managed
# hash is rewritten to describe that copy.

set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
TEMP=$(mktemp -d "${TMPDIR:-/tmp}/plutonium-preserve-drift.XXXXXXXX")
trap 'rm -rf -- "$TEMP"' EXIT

failures=0
fail() {
    echo "preserve-drift: $*" >&2
    failures=$((failures + 1))
}

source_root="$TEMP/source"
consumer="$TEMP/consumer"
mkdir -p "$source_root/src" "$consumer"
cp -a "$ROOT/scaffold" "$source_root/scaffold"
cp "$ROOT/init.sh" "$source_root/init.sh"
cp "$ROOT/src/Directory.Build.props" "$source_root/src/Directory.Build.props"

git -C "$source_root" init -q
git -C "$source_root" config user.email smoke@example.test
git -C "$source_root" config user.name Smoke
git -C "$source_root" add -A
git -C "$source_root" commit -qm initial
sha1=$(git -C "$source_root" rev-parse HEAD)

git -C "$consumer" init -q
git -C "$consumer" config user.email smoke@example.test
git -C "$consumer" config user.name Smoke
bash "$source_root/init.sh" --source "$source_root" --sha "$sha1" --target "$consumer" >/dev/null

# build.slnx is managed+preserve in the source lock; build.cs is preserve-only.
printf '\nconsumer slnx\n' >> "$consumer/build/build.slnx"
printf '\nconsumer build definition\n' >> "$consumer/build/build.cs"
consumer_slnx_hash=$(sha256sum "$consumer/build/build.slnx" | awk '{print $1}')

printf '\nsource slnx\n' >> "$source_root/scaffold/build/build.slnx"
printf '\nsource build definition\n' >> "$source_root/scaffold/build/build.cs"
printf '\n# source managed marker\n' >> "$source_root/scaffold/build/build.sh"
bash "$source_root/scaffold/lock.sh" write >/dev/null
git -C "$source_root" add -A
git -C "$source_root" commit -qm drift
sha2=$(git -C "$source_root" rev-parse HEAD)

if ! bash "$consumer/build/update.sh" --source "$source_root" --sha "$sha2" --target "$consumer" --no-smoke >"$TEMP/update.out" 2>&1; then
    cat "$TEMP/update.out" >&2
    fail "update failed during preserve drift."
fi

if ! grep -q 'consumer slnx' "$consumer/build/build.slnx"; then
    fail "update overwrote a preserve+managed file."
fi
if ! grep -q 'consumer build definition' "$consumer/build/build.cs"; then
    fail "update overwrote a preserve-only file."
fi
if ! grep -q 'source managed marker' "$consumer/build/build.sh"; then
    fail "update did not install a managed file."
fi

lock_value() {
    awk -v path="$1" '$1 == "managed" && $2 == path { print $3; exit }' "$consumer/build/plutonium.lock"
}

if [ "$(lock_value build/build.slnx)" != "$consumer_slnx_hash" ]; then
    fail "installed lock does not hash the preserved build.slnx copy."
fi
if [ "$(lock_value build/build.sh)" != "$(sha256sum "$consumer/build/build.sh" | awk '{print $1}')" ]; then
    fail "installed lock does not hash the updated build.sh copy."
fi

if [ "$failures" -ne 0 ]; then
    echo "preserve-drift: $failures failure(s)." >&2
    exit 1
fi

echo "preserve-drift: ok"

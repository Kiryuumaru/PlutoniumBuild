#!/usr/bin/env bash
# Regression smoke: a failed update restores backups and removes every file the
# new lock introduced, whether the failure happens during apply or during the
# smoke build.

set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
TEMP=$(mktemp -d "${TMPDIR:-/tmp}/plutonium-update-rollback.XXXXXXXX")
trap 'rm -rf -- "$TEMP"' EXIT

failures=0
fail() {
    echo "update-rollback: $*" >&2
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

consumer_lock_before=$(sha256sum "$consumer/build/plutonium.lock" | awk '{print $1}')
consumer_build_before=$(sha256sum "$consumer/build/build.sh" | awk '{print $1}')

add_managed() {
    local relative="$1"
    local source_file="$source_root/scaffold/$relative"
    local hash
    hash=$(sha256sum "$source_file" | awk '{print $1}')
    printf 'managed %s %s\n' "$relative" "$hash" >> "$source_root/scaffold/build/plutonium.lock"
}

# A cached verified-looking toolchain makes run_smoke fail without a network.
mkdir -p "$consumer/build/temp/dotnet-unix"
cat > "$consumer/build/temp/dotnet-unix/dotnet" <<'EOF'
#!/usr/bin/env bash
if [ "${1:-}" = "--version" ]; then
    echo "10.0.402"
    exit 0
fi
exit 1
EOF
chmod 755 "$consumer/build/temp/dotnet-unix/dotnet"

printf 'probe\n' > "$source_root/scaffold/build/rollback-probe.txt"
printf '\n# smoke rollback marker\n' >> "$source_root/scaffold/build/build.sh"
add_managed build/rollback-probe.txt
bash "$source_root/scaffold/lock.sh" write >/dev/null
git -C "$source_root" add -A
git -C "$source_root" commit -qm smoke-trigger
sha2=$(git -C "$source_root" rev-parse HEAD)

if bash "$consumer/build/update.sh" --source "$source_root" --sha "$sha2" --target "$consumer" --smoke >"$TEMP/smoke.out" 2>&1; then
    fail "update reported success while the smoke build failed."
fi
if [ -e "$consumer/build/rollback-probe.txt" ]; then
    fail "smoke failure left a file introduced by the new lock."
fi
if [ "$(sha256sum "$consumer/build/build.sh" | awk '{print $1}')" != "$consumer_build_before" ]; then
    fail "smoke failure did not restore a managed file."
fi
if [ "$(sha256sum "$consumer/build/plutonium.lock" | awk '{print $1}')" != "$consumer_lock_before" ]; then
    fail "smoke failure did not restore the previous lock."
fi

# An apply failure after an earlier file has moved must prune that new file too.
printf 'keep\n' > "$consumer/build/zzz"
printf 'probe\n' > "$source_root/scaffold/build/aaa-new.txt"
mkdir -p "$source_root/scaffold/build/zzz"
printf 'nested\n' > "$source_root/scaffold/build/zzz/nested.txt"
printf '\n# apply rollback marker\n' >> "$source_root/scaffold/build/build.sh"
add_managed build/aaa-new.txt
add_managed build/zzz/nested.txt
bash "$source_root/scaffold/lock.sh" write >/dev/null
git -C "$source_root" add -A
git -C "$source_root" commit -qm apply-trigger
sha3=$(git -C "$source_root" rev-parse HEAD)

if bash "$consumer/build/update.sh" --source "$source_root" --sha "$sha3" --target "$consumer" --no-smoke >"$TEMP/apply.out" 2>&1; then
    fail "update reported success while apply could not create build/zzz/nested.txt."
fi
if [ -e "$consumer/build/aaa-new.txt" ] || [ -e "$consumer/build/zzz/nested.txt" ]; then
    fail "apply failure left a file introduced before the failing move."
fi
if [ "$(cat "$consumer/build/zzz")" != "keep" ]; then
    fail "apply failure changed the conflicting build/zzz file."
fi
if [ "$(sha256sum "$consumer/build/build.sh" | awk '{print $1}')" != "$consumer_build_before" ]; then
    fail "apply failure did not restore a managed file."
fi
if [ "$(sha256sum "$consumer/build/plutonium.lock" | awk '{print $1}')" != "$consumer_lock_before" ]; then
    fail "apply failure did not restore the previous lock."
fi

if [ "$failures" -ne 0 ]; then
    echo "update-rollback: $failures failure(s)." >&2
    exit 1
fi

echo "update-rollback: ok"

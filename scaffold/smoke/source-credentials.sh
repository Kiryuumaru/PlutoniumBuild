#!/usr/bin/env bash
# Regression smoke: init/update must reject credential-bearing source URLs
# before fetching, logging them, or writing them into the installed lock.

set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
WORK=$(mktemp -d "${TMPDIR:-/tmp}/plutonium-source-credentials.XXXXXXXX")
trap 'rm -rf -- "$WORK"' EXIT

failures=0

reject_init() {
    local label="$1" source="$2" target="$WORK/init-$3"
    mkdir -p "$target"
    local output rc
    set +e
    output=$(bash "$ROOT/init.sh" --source "$source" --sha 0000000000000000000000000000000000000000 --target "$target" 2>&1)
    rc=$?
    set -e
    if [ "$rc" -eq 0 ]; then
        echo "source-credentials: init accepted a credential-bearing source ($label)." >&2
        failures=$((failures + 1))
    fi
    if printf '%s\n' "$output" | grep -q 'secret-token'; then
        echo "source-credentials: init echoed a credential ($label)." >&2
        failures=$((failures + 1))
    fi
    if [ -e "$target/build" ]; then
        echo "source-credentials: init wrote a scaffold for $label." >&2
        failures=$((failures + 1))
    fi
}

reject_update() {
    local label="$1" source="$2" target="$WORK/update-$3"
    mkdir -p "$target/build"
    cat > "$target/build/plutonium.lock" <<EOF
source=$source
source_url=$source
scaffold_commit=0000000000000000000000000000000000000000
package_version=0.1.0
EOF
    local before output rc
    before=$(sha256sum "$target/build/plutonium.lock" | awk '{print $1}')
    set +e
    output=$(bash "$ROOT/scaffold/build/update.sh" --sha 0000000000000000000000000000000000000000 --target "$target" --no-smoke 2>&1)
    rc=$?
    set -e
    if [ "$rc" -eq 0 ]; then
        echo "source-credentials: update accepted a credential-bearing source ($label)." >&2
        failures=$((failures + 1))
    fi
    if printf '%s\n' "$output" | grep -q 'secret-token'; then
        echo "source-credentials: update echoed a credential ($label)." >&2
        failures=$((failures + 1))
    fi
    if [ "$(sha256sum "$target/build/plutonium.lock" | awk '{print $1}')" != "$before" ]; then
        echo "source-credentials: update changed the lock for $label." >&2
        failures=$((failures + 1))
    fi
}

reject_update_url() {
    local label="$1" source_url="$2" target="$WORK/update-url-$3"
    mkdir -p "$target/build"
    cat > "$target/build/plutonium.lock" <<EOF
source=https://example.test/repo.git
source_url=$source_url
scaffold_commit=0000000000000000000000000000000000000000
package_version=0.1.0
EOF
    local before output rc
    before=$(sha256sum "$target/build/plutonium.lock" | awk '{print $1}')
    set +e
    output=$(bash "$ROOT/scaffold/build/update.sh" --sha 0000000000000000000000000000000000000000 --target "$target" --no-smoke 2>&1)
    rc=$?
    set -e
    if [ "$rc" -eq 0 ]; then
        echo "source-credentials: update accepted a credential-bearing source_url ($label)." >&2
        failures=$((failures + 1))
    fi
    if printf '%s\n' "$output" | grep -q 'secret-token'; then
        echo "source-credentials: update echoed a source_url credential ($label)." >&2
        failures=$((failures + 1))
    fi
    if [ "$(sha256sum "$target/build/plutonium.lock" | awk '{print $1}')" != "$before" ]; then
        echo "source-credentials: update changed the lock for source_url $label." >&2
        failures=$((failures + 1))
    fi
}

reject_init "userinfo" 'https://user:secret-token@example.test/repo.git' userinfo
reject_init "query" 'https://example.test/repo.git?access_token=secret-token' query
reject_init "fragment" 'https://example.test/repo.git#access_token=secret-token' fragment
reject_init "whitespace" 'https://example.test/repo.git token' whitespace
reject_update "userinfo" 'https://user:secret-token@example.test/repo.git' userinfo
reject_update "query" 'https://example.test/repo.git?access_token=secret-token' query
reject_update_url "userinfo" 'https://user:secret-token@example.test/repo.git' userinfo
reject_update_url "query" 'https://example.test/repo.git?access_token=secret-token' query
reject_update_url "fragment" 'https://example.test/repo.git#access_token=secret-token' fragment

if [ "$failures" -ne 0 ]; then
    echo "source-credentials: $failures failure(s)." >&2
    exit 1
fi

echo "source-credentials: ok"

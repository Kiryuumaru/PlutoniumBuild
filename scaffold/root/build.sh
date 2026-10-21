#!/usr/bin/env bash
# Thin repository-root wrapper. Generated CI invokes ./build.sh, so this file
# must stay at the root and only forward into build/build.sh.

set -euo pipefail

REPO_ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
exec "$REPO_ROOT/build/build.sh" "$@"

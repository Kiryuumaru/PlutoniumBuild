#!/usr/bin/env bash
# Regression smoke: init and update resolve the default branch, bare and
# qualified branch/tag names, and full commit SHAs to a full commit; reject
# ambiguous, unknown, malformed, and option-shaped refs; re-home the installed
# source identity on an explicit --source; follow the lock source on a no-arg
# update; and reject a poisoned source lock before installing or writing.

set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
WORK=$(mktemp -d "${TMPDIR:-/tmp}/plutonium-ref-resolution.XXXXXXXX")
trap 'rm -rf -- "$WORK"' EXIT

failures=0
fail() {
    echo "ref-resolution: $*" >&2
    failures=$((failures + 1))
}

lock_value() {
    local target="$1" key="$2"
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
    ' "$target/build/plutonium.lock"
}

lock_commit() {
    lock_value "$1" scaffold_commit
}

assert_commit() {
    local target="$1" expected="$2" label="$3" actual
    actual=$(lock_commit "$target")
    if [ "${#actual}" -ne 40 ]; then
        fail "$label: lock scaffold_commit is not 40 characters ('$actual')."
        return
    fi
    case "$actual" in
        *[!0-9a-f]*) fail "$label: lock scaffold_commit is not lowercase hex ('$actual')." ;;
    esac
    if [ "$actual" != "$expected" ]; then
        fail "$label: lock scaffold_commit is '$actual', expected '$expected'."
    fi
}

assert_marker() {
    local target="$1" marker="$2" label="$3"
    if ! grep -q -- "$marker" "$target/build/build.sh"; then
        fail "$label: installed build.sh is missing marker '$marker'."
    fi
}

journal_value() {
    local target="$1" key="$2" journal="${3:-}"
    if [ -z "$journal" ]; then
        journal=$(find "$target/build/temp/update" -type f -name journal.txt 2>/dev/null | sort | tail -1)
    fi
    [ -n "$journal" ] || return 1
    awk -v key="$key" '
        {
            prefix = key "="
            if (index($0, prefix) == 1) {
                print substr($0, length(prefix) + 1)
                exit
            }
        }
    ' "$journal"
}

journal_path_from_output() {
    local output="$1" target="$2" stamp
    stamp=$(sed -n 's|.*journaled under build/temp/update/\([^ ]*\)\.$|\1|p' "$output" | tail -1)
    [ -n "$stamp" ] || return 1
    printf '%s\n' "$target/build/temp/update/$stamp/journal.txt"
}

journal_count() {
    local target="$1"
    find "$target/build/temp/update" -type f -name journal.txt 2>/dev/null | wc -l | tr -d ' '
}

lock_hash() {
    sha256sum "$1/build/plutonium.lock" | awk '{print $1}'
}

expect_success() {
    local label="$1" output="$2"
    shift 2
    if ! "$@" >"$output" 2>&1; then
        fail "$label: command failed."
        sed 's/^/    /' "$output" >&2
    fi
}

expect_failure() {
    local label="$1" output="$2"
    shift 2
    local rc
    set +e
    "$@" >"$output" 2>&1
    rc=$?
    set -e
    if [ "$rc" -eq 0 ]; then
        fail "$label: command unexpectedly succeeded."
    fi
}

expect_exit() {
    local expected="$1" label="$2" output="$3"
    shift 3
    local rc
    set +e
    "$@" >"$output" 2>&1
    rc=$?
    set -e
    if [ "$rc" -ne "$expected" ]; then
        fail "$label: expected exit $expected, got $rc."
    fi
}

new_source() {
    local destination="$1" branch="$2"
    mkdir -p "$destination/src"
    cp -a "$ROOT/scaffold" "$destination/scaffold"
    cp "$ROOT/init.sh" "$destination/init.sh"
    cp "$ROOT/src/Directory.Build.props" "$destination/src/Directory.Build.props"
    git -C "$destination" init -q -b "$branch"
    git -C "$destination" config user.email smoke@example.test
    git -C "$destination" config user.name Smoke
    git -C "$destination" add -A
    git -C "$destination" -c commit.gpgsign=false commit -qm initial
}

commit_marker() {
    local source="$1" marker="$2"
    printf '\n# %s\n' "$marker" >> "$source/scaffold/build/build.sh"
    bash "$source/scaffold/lock.sh" write >/dev/null
    git -C "$source" add -A
    git -C "$source" -c commit.gpgsign=false commit -qm "marker $marker"
    git -C "$source" rev-parse HEAD
}

commit_poisoned_lock() {
    local source="$1" poison="$2" file
    file="$source/scaffold/build/plutonium.lock"
    case "$poison" in
        prefixed-source) sed -i 's/^source=/ source=/' "$file" ;;
        duplicate-format) printf 'format=1\n' >> "$file" ;;
        short-scaffold-commit) sed -i 's/^scaffold_commit=.*/scaffold_commit=abc1234/' "$file" ;;
        missing-format) sed -i '/^format=/d' "$file" ;;
        *) fail "unknown lock poison '$poison'."; return 1 ;;
    esac
    git -C "$source" add -A
    git -C "$source" -c commit.gpgsign=false commit -qm "poison $poison"
    git -C "$source" rev-parse HEAD
}

new_consumer() {
    local destination="$1"
    mkdir -p "$destination"
    git -C "$destination" init -q -b main
    git -C "$destination" config user.email smoke@example.test
    git -C "$destination" config user.name Smoke
    printf 'consumer\n' > "$destination/sentinel.txt"
    git -C "$destination" add -A
    git -C "$destination" -c commit.gpgsign=false commit -qm baseline
}

# Default ref: init without a ref installs the tip of main.
main_source="$WORK/source-main"
new_source "$main_source" main
main_commit=$(commit_marker "$main_source" "default-main-marker")

default_consumer="$WORK/consumer-default-main"
new_consumer "$default_consumer"
expect_success "init without a ref" "$WORK/default-main.out" \
    bash "$main_source/init.sh" --source "$main_source" --target "$default_consumer"
assert_commit "$default_consumer" "$main_commit" "init without a ref"
assert_marker "$default_consumer" "default-main-marker" "init without a ref"
if ! grep -q 'ref refs/heads/main (default)' "$WORK/default-main.out"; then
    fail "init without a ref did not report the default main ref."
fi

# A source whose default branch is not main still resolves refs/heads/main by
# default, so callers must name the branch; a bare branch name resolves it.
trunk_source="$WORK/source-trunk"
new_source "$trunk_source" trunk
trunk_commit=$(commit_marker "$trunk_source" "trunk-marker")

trunk_default_consumer="$WORK/consumer-trunk-default"
new_consumer "$trunk_default_consumer"
expect_failure "init without a ref against a trunk-default source" "$WORK/trunk-default.out" \
    bash "$trunk_source/init.sh" --source "$trunk_source" --target "$trunk_default_consumer"
if [ -e "$trunk_default_consumer/build" ]; then
    fail "init without a ref wrote a scaffold against a trunk-default source."
fi

trunk_consumer="$WORK/consumer-trunk-ref"
new_consumer "$trunk_consumer"
expect_success "init with --ref refs/heads/trunk" "$WORK/trunk-ref.out" \
    bash "$trunk_source/init.sh" --source "$trunk_source" \
    --ref refs/heads/trunk --target "$trunk_consumer"
assert_commit "$trunk_consumer" "$trunk_commit" "init with a non-main branch ref"
assert_marker "$trunk_consumer" "trunk-marker" "init with a non-main branch ref"

trunk_bare_consumer="$WORK/consumer-trunk-bare"
new_consumer "$trunk_bare_consumer"
expect_success "init with a bare branch ref" "$WORK/trunk-bare.out" \
    bash "$trunk_source/init.sh" --source "$trunk_source" \
    --ref trunk --target "$trunk_bare_consumer"
assert_commit "$trunk_bare_consumer" "$trunk_commit" "init with a bare branch ref"
assert_marker "$trunk_bare_consumer" "trunk-marker" "init with a bare branch ref"

# A bare branch name resolves to the branch tip for update too.
git -C "$main_source" checkout -q -b feature
feature_commit=$(commit_marker "$main_source" "bare-feature-marker")
git -C "$main_source" checkout -q main

feature_consumer="$WORK/consumer-bare-feature"
new_consumer "$feature_consumer"
expect_success "init before a bare branch update" "$WORK/bare-feature-init.out" \
    bash "$main_source/init.sh" --source "$main_source" --target "$feature_consumer"
assert_commit "$feature_consumer" "$main_commit" "init before a bare branch update"

expect_success "update with a bare branch ref" "$WORK/bare-feature.out" \
    bash "$feature_consumer/build/update.sh" --target "$feature_consumer" \
    --ref feature --no-smoke
assert_commit "$feature_consumer" "$feature_commit" "update with a bare branch ref"
assert_marker "$feature_consumer" "bare-feature-marker" "update with a bare branch ref"
if [ "$(journal_value "$feature_consumer" from_commit)" != "$main_commit" ]; then
    fail "bare branch update journal from_commit is not the installed commit."
fi
if [ "$(journal_value "$feature_consumer" to_commit)" != "$feature_commit" ]; then
    fail "bare branch update journal to_commit is not the branch tip."
fi

# Lightweight and annotated tags both resolve to the tagged commit, and an
# annotated tag object is peeled. Qualified and bare names are both accepted.
git -C "$main_source" tag v-light
git -C "$main_source" -c tag.gpgsign=false tag -a v-annotated -m "annotated release"
light_commit=$(git -C "$main_source" rev-parse 'refs/tags/v-light^{commit}')
annotated_object=$(git -C "$main_source" rev-parse 'refs/tags/v-annotated')
annotated_commit=$(git -C "$main_source" rev-parse 'refs/tags/v-annotated^{commit}')
if [ "$annotated_object" = "$annotated_commit" ]; then
    fail "the annotated tag fixture is not a tag object."
fi

light_consumer="$WORK/consumer-tag-light"
new_consumer "$light_consumer"
expect_success "init with a lightweight tag" "$WORK/tag-light.out" \
    bash "$main_source/init.sh" --source "$main_source" \
    --ref refs/tags/v-light --target "$light_consumer"
assert_commit "$light_consumer" "$light_commit" "init with a lightweight tag"

annotated_consumer="$WORK/consumer-tag-annotated"
new_consumer "$annotated_consumer"
expect_success "init with an annotated tag" "$WORK/tag-annotated.out" \
    bash "$main_source/init.sh" --source "$main_source" \
    --ref refs/tags/v-annotated --target "$annotated_consumer"
assert_commit "$annotated_consumer" "$annotated_commit" "init with an annotated tag"
if [ "$(lock_commit "$annotated_consumer")" = "$annotated_object" ]; then
    fail "init recorded the annotated tag object instead of the tagged commit."
fi

bare_light_consumer="$WORK/consumer-tag-light-bare"
new_consumer "$bare_light_consumer"
expect_success "init with a bare lightweight tag name" "$WORK/tag-light-bare.out" \
    bash "$main_source/init.sh" --source "$main_source" \
    --ref v-light --target "$bare_light_consumer"
assert_commit "$bare_light_consumer" "$light_commit" "init with a bare lightweight tag name"

bare_annotated_consumer="$WORK/consumer-tag-annotated-bare"
new_consumer "$bare_annotated_consumer"
expect_success "init with a bare annotated tag name" "$WORK/tag-annotated-bare.out" \
    bash "$main_source/init.sh" --source "$main_source" \
    --ref v-annotated --target "$bare_annotated_consumer"
assert_commit "$bare_annotated_consumer" "$annotated_commit" "init with a bare annotated tag name"
if [ "$(lock_commit "$bare_annotated_consumer")" = "$annotated_object" ]; then
    fail "init recorded the annotated tag object for a bare tag name."
fi

# Bare tag names resolve for update and peel annotated tag objects.
git -C "$main_source" checkout -q -b tagline
tag_light_commit=$(commit_marker "$main_source" "bare-update-light-marker")
git -C "$main_source" tag v-update-light
tag_annotated_commit=$(commit_marker "$main_source" "bare-update-annotated-marker")
git -C "$main_source" -c tag.gpgsign=false tag -a v-update-annotated -m "bare annotated update release"
tag_annotated_object=$(git -C "$main_source" rev-parse 'refs/tags/v-update-annotated')
git -C "$main_source" checkout -q main
if [ "$tag_annotated_object" = "$tag_annotated_commit" ]; then
    fail "the annotated update tag fixture is not a tag object."
fi

tag_update_light_consumer="$WORK/consumer-tag-update-light"
new_consumer "$tag_update_light_consumer"
expect_success "init before a bare lightweight tag update" "$WORK/tag-update-light-init.out" \
    bash "$main_source/init.sh" --source "$main_source" --target "$tag_update_light_consumer"
assert_commit "$tag_update_light_consumer" "$main_commit" "init before a bare lightweight tag update"
expect_success "update with a bare lightweight tag name" "$WORK/tag-update-light.out" \
    bash "$tag_update_light_consumer/build/update.sh" --target "$tag_update_light_consumer" \
    --ref v-update-light --no-smoke
assert_commit "$tag_update_light_consumer" "$tag_light_commit" "update with a bare lightweight tag name"
assert_marker "$tag_update_light_consumer" "bare-update-light-marker" "update with a bare lightweight tag name"

tag_update_annotated_consumer="$WORK/consumer-tag-update-annotated"
new_consumer "$tag_update_annotated_consumer"
expect_success "init before a bare annotated tag update" "$WORK/tag-update-annotated-init.out" \
    bash "$main_source/init.sh" --source "$main_source" --target "$tag_update_annotated_consumer"
assert_commit "$tag_update_annotated_consumer" "$main_commit" "init before a bare annotated tag update"
expect_success "update with a bare annotated tag name" "$WORK/tag-update-annotated.out" \
    bash "$tag_update_annotated_consumer/build/update.sh" --target "$tag_update_annotated_consumer" \
    --ref v-update-annotated --no-smoke
assert_commit "$tag_update_annotated_consumer" "$tag_annotated_commit" "update with a bare annotated tag name"
assert_marker "$tag_update_annotated_consumer" "bare-update-annotated-marker" "update with a bare annotated tag name"
if [ "$(lock_commit "$tag_update_annotated_consumer")" = "$tag_annotated_object" ]; then
    fail "update recorded the annotated tag object for a bare tag name."
fi

# A full commit SHA is accepted through --sha and --ref, and normalizes case.
sha_consumer="$WORK/consumer-sha"
new_consumer "$sha_consumer"
expect_success "init with --sha" "$WORK/sha.out" \
    bash "$main_source/init.sh" --source "$main_source" \
    --sha "$main_commit" --target "$sha_consumer"
assert_commit "$sha_consumer" "$main_commit" "init with --sha"

upper_commit=$(printf '%s' "$main_commit" | tr 'a-f' 'A-F')
sha_ref_consumer="$WORK/consumer-sha-ref"
new_consumer "$sha_ref_consumer"
expect_success "init with --ref <sha>" "$WORK/sha-ref.out" \
    bash "$main_source/init.sh" --source "$main_source" \
    --ref "$upper_commit" --target "$sha_ref_consumer"
assert_commit "$sha_ref_consumer" "$main_commit" "init with --ref <sha>"

# Update without a ref follows the moved main tip, records the resolved commit
# in the journal, and a repeated update at the same tip is a no-op.
update_source="$WORK/source-update"
new_source "$update_source" main
initial_commit=$(commit_marker "$update_source" "update-initial-marker")

update_consumer="$WORK/consumer-update"
new_consumer "$update_consumer"
expect_success "init before a default-ref update" "$WORK/update-init.out" \
    bash "$update_source/init.sh" --source "$update_source" --target "$update_consumer"
assert_commit "$update_consumer" "$initial_commit" "init before a default-ref update"

updated_commit=$(commit_marker "$update_source" "default-update-marker")
expect_success "update without a ref" "$WORK/update.out" \
    bash "$update_consumer/build/update.sh" --source "$update_source" \
    --target "$update_consumer" --no-smoke
assert_commit "$update_consumer" "$updated_commit" "update without a ref"
assert_marker "$update_consumer" "default-update-marker" "update without a ref"
if ! grep -q 'ref refs/heads/main (default)' "$WORK/update.out"; then
    fail "update without a ref did not report the default main ref."
fi
if [ "$(journal_value "$update_consumer" from_commit)" != "$initial_commit" ]; then
    fail "update journal from_commit is not the installed commit."
fi
if [ "$(journal_value "$update_consumer" to_commit)" != "$updated_commit" ]; then
    fail "update journal to_commit is not the resolved commit."
fi

lock_before=$(lock_hash "$update_consumer")
journal_count_before=$(journal_count "$update_consumer")
expect_success "repeated update without a ref" "$WORK/update-repeat.out" \
    bash "$update_consumer/build/update.sh" --source "$update_source" \
    --target "$update_consumer" --no-smoke
if ! grep -qi 'nothing to update' "$WORK/update-repeat.out"; then
    fail "repeated update did not report that it was already current."
fi
if [ "$(lock_hash "$update_consumer")" != "$lock_before" ]; then
    fail "repeated update changed the lock."
fi
if [ "$(journal_count "$update_consumer")" != "$journal_count_before" ]; then
    fail "repeated update wrote a new journal."
fi

# A no-arg update follows the installed lock source; an explicit --source
# re-homes both source and source_url, and a later no-arg update follows it.
fork_source="$WORK/source-fork"
new_source "$fork_source" trunk
fork_commit=$(commit_marker "$fork_source" "fork-marker")

identity_consumer="$WORK/consumer-source-identity"
new_consumer "$identity_consumer"
expect_success "init before a source identity update" "$WORK/identity-init.out" \
    bash "$update_source/init.sh" --source "$update_source" --target "$identity_consumer"
assert_commit "$identity_consumer" "$updated_commit" "init before a source identity update"
if [ "$(lock_value "$identity_consumer" source)" != "$update_source" ]; then
    fail "init did not record the update source."
fi
if [ "$(lock_value "$identity_consumer" source_url)" != "$update_source" ]; then
    fail "init did not record the update source_url."
fi

follow_commit=$(commit_marker "$update_source" "follow-lock-source-marker")
expect_success "no-arg update follows the lock source" "$WORK/identity-follow.out" \
    bash "$identity_consumer/build/update.sh" --target "$identity_consumer" --no-smoke
assert_commit "$identity_consumer" "$follow_commit" "no-arg update follows the lock source"
assert_marker "$identity_consumer" "follow-lock-source-marker" "no-arg update follows the lock source"
if [ "$(lock_value "$identity_consumer" source)" != "$update_source" ]; then
    fail "no-arg update replaced the installed lock source."
fi
if [ "$(lock_value "$identity_consumer" source_url)" != "$update_source" ]; then
    fail "no-arg update replaced the installed lock source_url."
fi

expect_success "update with an explicit source" "$WORK/identity-rehome.out" \
    bash "$identity_consumer/build/update.sh" --source "$fork_source" \
    --ref trunk --target "$identity_consumer" --no-smoke
assert_commit "$identity_consumer" "$fork_commit" "update with an explicit source"
assert_marker "$identity_consumer" "fork-marker" "update with an explicit source"
if [ "$(lock_value "$identity_consumer" source)" != "$fork_source" ]; then
    fail "explicit-source update did not re-home the lock source."
fi
if [ "$(lock_value "$identity_consumer" source_url)" != "$fork_source" ]; then
    fail "explicit-source update did not re-home the lock source_url."
fi
explicit_journal=$(journal_path_from_output "$WORK/identity-rehome.out" "$identity_consumer")
if [ -z "$explicit_journal" ] || [ ! -f "$explicit_journal" ]; then
    fail "explicit-source update did not report its journal path."
elif [ "$(journal_value "$identity_consumer" from_commit "$explicit_journal")" != "$follow_commit" ]; then
    fail "explicit-source update journal from_commit is not the installed commit."
elif [ "$(journal_value "$identity_consumer" to_commit "$explicit_journal")" != "$fork_commit" ]; then
    fail "explicit-source update journal to_commit is not the fork commit."
fi

fork_follow_commit=$(commit_marker "$fork_source" "fork-follow-marker")
expect_success "no-arg update follows the re-homed source" "$WORK/identity-fork-follow.out" \
    bash "$identity_consumer/build/update.sh" --target "$identity_consumer" \
    --ref trunk --no-smoke
assert_commit "$identity_consumer" "$fork_follow_commit" "no-arg update follows the re-homed source"
assert_marker "$identity_consumer" "fork-follow-marker" "no-arg update follows the re-homed source"
if [ "$(lock_value "$identity_consumer" source)" != "$fork_source" ]; then
    fail "no-arg update did not keep the re-homed lock source."
fi
if [ "$(lock_value "$identity_consumer" source_url)" != "$fork_source" ]; then
    fail "no-arg update did not keep the re-homed lock source_url."
fi

# A same-named branch and tag are ambiguous for --ref main; the default ref
# prefers the branch. A bare name matching neither is not found.
ambiguous_source="$WORK/source-ambiguous"
new_source "$ambiguous_source" main
shadow_commit=$(commit_marker "$ambiguous_source" "shadow-tag-marker")
git -C "$ambiguous_source" tag main
branch_commit=$(commit_marker "$ambiguous_source" "branch-main-marker")
if [ "$shadow_commit" = "$branch_commit" ]; then
    fail "the ambiguous ref fixture did not move the branch past the tag."
fi

ambiguous_consumer="$WORK/consumer-ambiguous"
new_consumer "$ambiguous_consumer"
expect_exit 2 "init with an ambiguous --ref main" "$WORK/ambiguous.out" \
    bash "$ambiguous_source/init.sh" --source "$ambiguous_source" \
    --ref main --target "$ambiguous_consumer"
if ! grep -q 'ambiguous' "$WORK/ambiguous.out"; then
    fail "init did not explain the ambiguous main ref."
fi
if ! grep -q 'refs/heads/main' "$WORK/ambiguous.out" ||
   ! grep -q 'refs/tags/main' "$WORK/ambiguous.out"; then
    fail "init did not name both refs for the ambiguous main ref."
fi
if ! grep -q 'pass refs/heads/main or refs/tags/main' "$WORK/ambiguous.out"; then
    fail "init did not tell the caller how to disambiguate the main ref."
fi
if [ -e "$ambiguous_consumer/build" ]; then
    fail "init wrote a scaffold for an ambiguous main ref."
fi

branch_consumer="$WORK/consumer-branch-over-tag"
new_consumer "$branch_consumer"
expect_success "init without a ref with a shadow main tag" "$WORK/branch-over-tag.out" \
    bash "$ambiguous_source/init.sh" --source "$ambiguous_source" --target "$branch_consumer"
assert_commit "$branch_consumer" "$branch_commit" "init without a ref with a shadow main tag"
assert_marker "$branch_consumer" "branch-main-marker" "init without a ref with a shadow main tag"

ambiguous_update_consumer="$WORK/consumer-ambiguous-update"
new_consumer "$ambiguous_update_consumer"
expect_success "init before an ambiguous update ref" "$WORK/ambiguous-update-init.out" \
    bash "$ambiguous_source/init.sh" --source "$ambiguous_source" --target "$ambiguous_update_consumer"
ambiguous_update_lock=$(lock_hash "$ambiguous_update_consumer")
expect_exit 2 "update with an ambiguous --ref main" "$WORK/ambiguous-update.out" \
    bash "$ambiguous_update_consumer/build/update.sh" --target "$ambiguous_update_consumer" \
    --ref main --no-smoke
if ! grep -q 'ambiguous' "$WORK/ambiguous-update.out"; then
    fail "update did not explain the ambiguous main ref."
fi
if ! grep -q 'refs/heads/main' "$WORK/ambiguous-update.out" ||
   ! grep -q 'refs/tags/main' "$WORK/ambiguous-update.out"; then
    fail "update did not name both refs for the ambiguous main ref."
fi
if ! grep -q 'pass refs/heads/main or refs/tags/main' "$WORK/ambiguous-update.out"; then
    fail "update did not tell the caller how to disambiguate the main ref."
fi
if [ "$(lock_hash "$ambiguous_update_consumer")" != "$ambiguous_update_lock" ]; then
    fail "update changed the lock for an ambiguous main ref."
fi

unknown_consumer="$WORK/consumer-unknown"
new_consumer "$unknown_consumer"
expect_exit 1 "init with an unknown bare ref" "$WORK/unknown.out" \
    bash "$ambiguous_source/init.sh" --source "$ambiguous_source" \
    --ref does-not-exist --target "$unknown_consumer"
if ! grep -q 'was not found' "$WORK/unknown.out"; then
    fail "init did not report the unknown bare ref."
fi
if [ -e "$unknown_consumer/build" ]; then
    fail "init wrote a scaffold for an unknown bare ref."
fi

unknown_update_lock=$(lock_hash "$update_consumer")
unknown_update_journals=$(journal_count "$update_consumer")
expect_exit 1 "update with an unknown bare ref" "$WORK/unknown-update.out" \
    bash "$update_consumer/build/update.sh" --target "$update_consumer" \
    --ref does-not-exist --no-smoke
if ! grep -q 'was not found' "$WORK/unknown-update.out"; then
    fail "update did not report the unknown bare ref."
fi
if [ "$(lock_hash "$update_consumer")" != "$unknown_update_lock" ]; then
    fail "update changed the lock for an unknown bare ref."
fi
if [ "$(journal_count "$update_consumer")" != "$unknown_update_journals" ]; then
    fail "update wrote a journal for an unknown bare ref."
fi

# Fully qualified refs disambiguate a name that is both a branch and a tag.
disambiguate_source="$WORK/source-disambiguate"
new_source "$disambiguate_source" main
disambiguate_initial=$(commit_marker "$disambiguate_source" "disambiguate-initial-marker")
update_branch_consumer="$WORK/consumer-disambiguate-update-branch"
new_consumer "$update_branch_consumer"
expect_success "init before a qualified branch update" "$WORK/disambiguate-update-init.out" \
    bash "$disambiguate_source/init.sh" --source "$disambiguate_source" --target "$update_branch_consumer"
assert_commit "$update_branch_consumer" "$disambiguate_initial" "init before a qualified branch update"
update_tag_consumer="$WORK/consumer-disambiguate-update-tag"
new_consumer "$update_tag_consumer"
expect_success "init before a qualified tag update" "$WORK/disambiguate-tag-init.out" \
    bash "$disambiguate_source/init.sh" --source "$disambiguate_source" --target "$update_tag_consumer"
assert_commit "$update_tag_consumer" "$disambiguate_initial" "init before a qualified tag update"

tag_shared_commit=$(commit_marker "$disambiguate_source" "qualified-tag-marker")
git -C "$disambiguate_source" tag shared
branch_shared_commit=$(commit_marker "$disambiguate_source" "qualified-branch-marker")
git -C "$disambiguate_source" checkout -q -b shared
git -C "$disambiguate_source" checkout -q main
if [ "$tag_shared_commit" = "$branch_shared_commit" ]; then
    fail "the qualified disambiguation fixture did not split the branch and tag."
fi

init_branch_consumer="$WORK/consumer-disambiguate-init-branch"
new_consumer "$init_branch_consumer"
expect_success "init with a qualified branch ref" "$WORK/qualified-branch-init.out" \
    bash "$disambiguate_source/init.sh" --source "$disambiguate_source" \
    --ref refs/heads/shared --target "$init_branch_consumer"
assert_commit "$init_branch_consumer" "$branch_shared_commit" "init with a qualified branch ref"
assert_marker "$init_branch_consumer" "qualified-branch-marker" "init with a qualified branch ref"

init_tag_consumer="$WORK/consumer-disambiguate-init-tag"
new_consumer "$init_tag_consumer"
expect_success "init with a qualified tag ref" "$WORK/qualified-tag-init.out" \
    bash "$disambiguate_source/init.sh" --source "$disambiguate_source" \
    --ref refs/tags/shared --target "$init_tag_consumer"
assert_commit "$init_tag_consumer" "$tag_shared_commit" "init with a qualified tag ref"
assert_marker "$init_tag_consumer" "qualified-tag-marker" "init with a qualified tag ref"

expect_success "update with a qualified branch ref" "$WORK/qualified-branch-update.out" \
    bash "$update_branch_consumer/build/update.sh" --target "$update_branch_consumer" \
    --ref refs/heads/shared --no-smoke
assert_commit "$update_branch_consumer" "$branch_shared_commit" "update with a qualified branch ref"
assert_marker "$update_branch_consumer" "qualified-branch-marker" "update with a qualified branch ref"

expect_success "update with a qualified tag ref" "$WORK/qualified-tag-update.out" \
    bash "$update_tag_consumer/build/update.sh" --target "$update_tag_consumer" \
    --ref refs/tags/shared --no-smoke
assert_commit "$update_tag_consumer" "$tag_shared_commit" "update with a qualified tag ref"
assert_marker "$update_tag_consumer" "qualified-tag-marker" "update with a qualified tag ref"

qualified_ambiguous_consumer="$WORK/consumer-qualified-ambiguous"
new_consumer "$qualified_ambiguous_consumer"
expect_exit 2 "init with a bare ambiguous ref" "$WORK/qualified-ambiguous.out" \
    bash "$disambiguate_source/init.sh" --source "$disambiguate_source" \
    --ref shared --target "$qualified_ambiguous_consumer"
if ! grep -q 'ambiguous' "$WORK/qualified-ambiguous.out"; then
    fail "init did not explain the ambiguous shared ref."
fi
if [ -e "$qualified_ambiguous_consumer/build" ]; then
    fail "init wrote a scaffold for an ambiguous shared ref."
fi

# An option-shaped ref is rejected before git runs, for init and update.
shim="$WORK/shim"
marker="$WORK/git-invoked"
mkdir -p "$shim"
cat > "$shim/git" <<'EOF'
#!/usr/bin/env bash
printf 'invoked\n' > "$GIT_MARKER"
exit 1
EOF
chmod 755 "$shim/git"

injection_consumer="$WORK/consumer-injection"
new_consumer "$injection_consumer"
expect_exit 2 "init with an option-shaped ref" "$WORK/injection-init.out" \
    env PATH="$shim:$PATH" GIT_MARKER="$marker" \
    bash "$main_source/init.sh" --source "$main_source" \
    --ref "--upload-pack=$marker" --target "$injection_consumer"
if ! grep -q -- '--ref must be' "$WORK/injection-init.out"; then
    fail "init did not explain the rejected option-shaped ref."
fi
if [ -e "$marker" ]; then
    fail "init invoked git for an option-shaped ref."
fi
if [ -e "$injection_consumer/build" ]; then
    fail "init wrote a scaffold for an option-shaped ref."
fi

injection_update_consumer="$WORK/consumer-injection-update"
new_consumer "$injection_update_consumer"
expect_success "init before an option-shaped update ref" "$WORK/injection-update-init.out" \
    bash "$main_source/init.sh" --source "$main_source" --target "$injection_update_consumer"
injection_lock_before=$(lock_hash "$injection_update_consumer")
expect_exit 2 "update with an option-shaped ref" "$WORK/injection-update.out" \
    env PATH="$shim:$PATH" GIT_MARKER="$marker" \
    bash "$injection_update_consumer/build/update.sh" --source "$main_source" \
    --ref "--upload-pack=$marker" --target "$injection_update_consumer" --no-smoke
if ! grep -q -- '--ref must be' "$WORK/injection-update.out"; then
    fail "update did not explain the rejected option-shaped ref."
fi
if [ -e "$marker" ]; then
    fail "update invoked git for an option-shaped ref."
fi
if [ "$(lock_hash "$injection_update_consumer")" != "$injection_lock_before" ]; then
    fail "update changed the lock for an option-shaped ref."
fi

# A poisoned source lock is rejected before init installs or update writes.
poison_source="$WORK/source-poison"
new_source "$poison_source" main
poison_commit=$(commit_poisoned_lock "$poison_source" "prefixed-source")

poison_init_consumer="$WORK/consumer-poison-init"
new_consumer "$poison_init_consumer"
expect_exit 1 "init with a poisoned source lock" "$WORK/poison-init.out" \
    bash "$poison_source/init.sh" --source "$poison_source" \
    --sha "$poison_commit" --target "$poison_init_consumer"
if ! grep -q 'reserved key source must start at column 0' "$WORK/poison-init.out"; then
    fail "init did not report the poisoned source lock key."
fi
if [ -e "$poison_init_consumer/build" ]; then
    fail "init installed a scaffold from a poisoned source lock."
fi

poison_installed_consumer="$WORK/consumer-poison-installed"
new_consumer "$poison_installed_consumer"
expect_success "init before a poisoned installed lock update" "$WORK/poison-installed-init.out" \
    bash "$main_source/init.sh" --source "$main_source" --sha "$main_commit" \
    --target "$poison_installed_consumer"
sed -i 's/^source=/ source=/' "$poison_installed_consumer/build/plutonium.lock"
poison_installed_lock=$(lock_hash "$poison_installed_consumer")
rm -f "$marker"
expect_exit 1 "update with a poisoned installed lock" "$WORK/poison-installed.out" \
    env PATH="$shim:$PATH" GIT_MARKER="$marker" \
    bash "$poison_installed_consumer/build/update.sh" \
    --target "$poison_installed_consumer" --no-smoke
if ! grep -q 'reserved key source must start at column 0' "$WORK/poison-installed.out"; then
    fail "update did not report the poisoned installed lock key."
fi
if [ -e "$marker" ]; then
    fail "update invoked git before rejecting a poisoned installed lock."
fi
if [ "$(lock_hash "$poison_installed_consumer")" != "$poison_installed_lock" ]; then
    fail "update changed the lock for a poisoned installed lock."
fi

poison_update_consumer="$WORK/consumer-poison-update"
new_consumer "$poison_update_consumer"
expect_success "init before a poisoned source lock update" "$WORK/poison-update-init.out" \
    bash "$main_source/init.sh" --source "$main_source" --sha "$main_commit" \
    --target "$poison_update_consumer"
poison_update_lock=$(lock_hash "$poison_update_consumer")
expect_exit 1 "update with a poisoned source lock" "$WORK/poison-update.out" \
    bash "$poison_update_consumer/build/update.sh" --source "$poison_source" \
    --sha "$poison_commit" --target "$poison_update_consumer" --no-smoke
if ! grep -q 'reserved key source must start at column 0' "$WORK/poison-update.out"; then
    fail "update did not report the poisoned source lock key."
fi
if [ "$(lock_hash "$poison_update_consumer")" != "$poison_update_lock" ]; then
    fail "update changed the lock for a poisoned source lock."
fi

if [ "$failures" -ne 0 ]; then
    echo "ref-resolution: $failures failure(s)." >&2
    exit 1
fi

echo "ref-resolution: ok"

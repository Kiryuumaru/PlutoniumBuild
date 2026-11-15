# 0003 — Resolve scaffold installs and updates to a commit SHA

- **Status:** Accepted
- **Date:** 2026-11-14

## Context

The scaffold installer and updater required a full 40-character release commit
SHA for every install and update. Every scaffold change — including a
documentation or status fix — needed a new pin commit and a matching docs
update, and consumers could not pick up fixes without a new SHA. The pin
created a maintenance loop and made the default path needlessly manual.

`PlutoniumBuild` 0.1.0 is published on nuget.org and the repository is public.
The raw installer and the scaffold payload are fetched from the repository, so
the default branch is a usable source. The repository default branch is `main`,
not `master`.

## Decision

Resolve every install and update revision to a full commit SHA before fetching.

- The default revision is `main`, resolved as `refs/heads/main`.
- `--ref` accepts a bare branch or tag name (for example `v1.0` or
  `release/1.0`), a fully qualified `refs/heads/...` or `refs/tags/...`, or a
  full 40-hex commit SHA. A bare name that matches both a branch and a tag is
  rejected as ambiguous; pass the fully qualified form.
- `--sha` accepts only a full commit SHA. `--ref` and `--sha` are mutually
  exclusive.
- The lock records only the resolved commit as `scaffold_commit`. It stores no
  `scaffold_ref`.
- The lock's reserved keys are canonical and unique. A lock with a
  non-canonical or duplicate reserved key is rejected.
- The lock's `source` is security-sensitive. A no-arg update fetches from that
  source; an explicit `--source` re-homes the lock to the fetched source.
- The primary install command is download-then-run: fetch `init.sh` from
  `.../main/init.sh` to a file, then run it. A failed download stops before
  execution.
- Update executes the fetched bootstrap before committing the update.

## Consequences

- Primary installs and no-arg updates follow the default branch tip.
- `--ref` resolves a branch or tag to a commit once at install time. Branches
  and tags can move, so `--ref` alone is not an immutable pin. Use `--sha` for
  an exact immutable pin. A tag install does not stay on the tag: later no-arg
  updates follow the lock's source at `main`.
- Branch-latest is trust-on-first-use. A compromised default branch runs code
  on the consumer's machine.
- The mutable raw installer and the same-payload lock hashes are
  integrity-after-fetch, not supply-chain authentication. The lock travels with
  the payload, so it detects a corrupted or mismatched payload, not a
  compromised upstream source.
- The resolved commit in the lock is traceability, not authentication.
- Existing installs created by the 0.1.0 installer still require `--sha`
  because the old updater has no `--ref`. Those consumers must bootstrap once
  with a full commit SHA; after that, no-arg updates follow the lock's source.
- Renaming the default branch requires updating the default ref in the
  installer and updater.

## References

- [README](../../README.md) — install and update commands.
- [INIT.md](../../INIT.md) — agent install path.
- [Workflows](../workflows.md) — scaffold runner and source contract.
- `init.sh`, `init.ps1`, `scaffold/build/update.sh`, and
  `scaffold/build/update.ps1` — the install and update entry points.

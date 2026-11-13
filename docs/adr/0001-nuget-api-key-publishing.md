# 0001 — Publish to nuget.org with a step-scoped API key

- **Status:** Accepted
- **Date:** 2026-10-21

## Context

The repository is private at the 0.1.0 release. Earlier docs described
nuget.org publishing as OIDC trusted publishing through a `nuget-publish`
environment and `NuGet/login`, and repeated the raw-installer requirement that
the repository be public. Those are two different gates: the raw installer URL
needs a public repository and a release commit, while pushing a package to
nuget.org does not.

The owner's convention is an API key stored as a repository secret, not an OIDC
identity or a deployment environment. The workflow therefore needs to work from
a private repository. The credential boundary is repository write access: any
workflow pushed by an actor with write access can read repository secrets, while
fork pull requests cannot. Step-scoping `NUGET_AUTH_TOKEN` limits where the
publish workflow exposes it; it does not narrow who can reach repository
secrets.

## Decision

`.github/workflows/publish.yml` stays manual:

- `workflow_dispatch` with a required boolean `confirm` input, default `false`.
- The job runs only when `inputs.confirm == true` and
  `github.ref == 'refs/heads/main'`.
- Checkout pins `refs/heads/main` and sets `persist-credentials: false`.
- Concurrency is the fixed group `publish-nuget` with
  `cancel-in-progress: false`.
- Top-level `permissions: contents: read`.
- Before packing, the workflow re-verifies the scaffold lock with
  `bash scaffold/lock.sh verify` and runs the full build and tests with
  `./build.sh`.
- A flat-container probe refuses a package version that already exists on
  nuget.org before the push runs.
- The `NUGET_AUTH_TOKEN` repository secret is exposed only on the `Push` step
  and passed to `dotnet nuget push` as `--api-key`.

The workflow uses no OIDC (`id-token`), `NuGet/login`, `NUGET_USER`,
`nuget-publish` environment, GitHub Packages feed, tags, or GitHub releases.

## Consequences

- No OIDC token exchange and no environment approval gate.
- A long-lived, repository-scoped credential exists in GitHub. Any actor with
  repository write access can read it through a workflow, and anyone who can
  dispatch the publish workflow from `main` with `confirm` can publish the
  current version.
- A dispatch from any other ref is skipped by the job condition rather than
  failing the run.
- The version guard prevents overwriting an already-published version. The
  concurrency group serializes publishes and protects an in-progress run from
  cancellation, but a newer queued run can still cancel an older pending run.
- There is no tag or release binding between the dispatched `main` tip and the
  published version: the version is whatever `PackageVersion` resolves to at
  that tip, and the guard only checks whether it already exists.
- Checkout uses a read-only token with no persisted credentials.

## Residual risk and follow-ups

- The nuget.org key is not yet confirmed to be package-scoped. Until it is
  restricted, a leaked key may reach other packages or versions the account can
  publish. Required follow-up: scope it to `PlutoniumBuild` and, after the first
  publish, to push-new-versions only.
- Set an expiry for the key and rotate it before it lapses; update the
  `NUGET_AUTH_TOKEN` repository secret when rotating.
- `GH_PAT` is currently present at repository scope and no workflow uses it.
  Remove it unless a documented consumer exists.
- Revisit this decision if the repository becomes public and OIDC trusted
  publishing becomes available for this account; that would supersede this ADR.

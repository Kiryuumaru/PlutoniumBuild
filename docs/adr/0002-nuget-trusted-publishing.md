# 0002 — Publish to nuget.org with NuGet Trusted Publishing

- **Status:** Accepted
- **Date:** 2026-11-13
- **Supersedes:** [ADR-0001](0001-nuget-api-key-publishing.md)

## Context

The 0.1.0 publish failed with HTTP 403 because the long-lived
`NUGET_AUTH_TOKEN` API key had expired. Long-lived repository secrets are a
standing risk: they need rotation, anyone with repository write access can read
them through a workflow, and publishing breaks silently when they lapse.
ADR-0001 chose the API key because the repository was private and OIDC trusted
publishing was assumed unavailable. The failed publish made the cost concrete.

## Decision

Publish with NuGet Trusted Publishing through GitHub OIDC.

`.github/workflows/publish.yml` keeps the manual `workflow_dispatch` with a
required boolean `confirm` input defaulting to `false`, the
`inputs.confirm == true && github.ref == 'refs/heads/main'` gate, the fixed
`publish-nuget` concurrency group, the version-exists guard, and `--no-symbols`.
It adds:

- Job-level `permissions: contents: read` and `id-token: write`. The
  `id-token: write` permission exists only on the confirmed main dispatch; the
  workflow default stays `contents: read`.
- A `NuGet login (OIDC)` step with `id: nuget_login`, pinned to
  `NuGet/login@8d196754b4036150537f80ac539e15c2f1028841` (`v1.2.0`), passing
  `user: Kiryuumaru`.
- A push that reads `steps.nuget_login.outputs.NUGET_API_KEY` and passes it to
  `dotnet nuget push --api-key`. No `secrets.*` value is used.

`NuGet/login` exchanges a single-use GitHub OIDC token for a short-lived
nuget.org API key (about one hour). No NuGet credential is stored in GitHub.
`NUGET_API_KEY` is this per-run OIDC output, not the consumer scaffold's
`NUGET_KEY` secret declared with `PlutoniumSecretVariable`.

### nuget.org Trusted Publishing policy

Create the policy on nuget.org with these exact fields (values are
case-insensitive):

| Field | Value |
| ----- | ----- |
| Repository Owner | `Kiryuumaru` |
| Repository | `PlutoniumBuild` |
| Workflow File | `publish.yml` |
| Environment | *(blank)* |
| Policy owner | `Kiryuumaru`, or the organization that owns the package |
| Policy scopes | Push new packages and new versions for `PlutoniumBuild`; after the first publish, restrict to new versions of the existing package |

`Workflow File` is the file name only, not the `.github/workflows/` path.
`Environment` stays blank because the workflow uses no GitHub Actions
environment. `Policy owner` is the nuget.org account or organization that owns
the package; the `user:` input in `NuGet/login` must be that account's nuget.org
username, not the repository owner if the two differ. Renaming or moving
`publish.yml` requires updating the policy to the new file name before the next
publish.

When the repository was private, a new policy could start temporarily active
for seven days. Under NuGet's documented behavior, a successful publish in that
window makes the policy permanently active; if no publish happens within seven
days, the policy becomes inactive and the window must be restarted. The
repository is now public.

## Consequences

- No long-lived NuGet credential is stored. A leaked repository secret cannot
  publish, and there is no NuGet key to rotate.
- Publishing depends on the OIDC exchange and a policy that matches the
  repository and workflow file. A mismatch fails the login step; there is no
  fallback to a stored key.
- The OIDC token is single-use and the minted API key is short-lived (about one
  hour), so the login step must run shortly before the push.
- The credential boundary is the job, not the step. GitHub Actions grants
  `id-token: write` only at job level, so every step in the publish job —
  build, test, and pack included — can request an OIDC token; the minted key is
  used only by the `Push` step. This is broader than ADR-0001's step-scoped
  `NUGET_AUTH_TOKEN`. The main-only `confirm` gate and the `refs/heads/main`
  checkout keep the exposure small. Splitting build and publish into separate
  jobs is the narrowing path, but the policy test currently asserts the
  `publish` job has no `needs`, so that split is a tracked follow-up, not part
  of this change.
- The old API key is already expired and is not a fallback; it is revoked after
  the workflow merge, and the `NUGET_AUTH_TOKEN` repository secret is deleted
  after the first successful OIDC publish.
- The workflow-policy tests pin the OIDC block: job-level `id-token: write`,
  the pinned `NuGet/login` action, and the `NUGET_API_KEY` push.

## Cutover

1. Create the Trusted Publishing policy on nuget.org with the fields above.
2. Merge the workflow change.
3. Revoke the old API key on nuget.org. It is already expired and is not a
   fallback, so revoking it after the merge cannot break publishing.
4. Dispatch `publish.yml` from `main` with `confirm` set to true and confirm the
   package publishes.
5. Delete the `NUGET_AUTH_TOKEN` repository secret after the first successful
   OIDC publish.

## References

- [ADR-0001](0001-nuget-api-key-publishing.md) — the superseded API-key
  decision.
- [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
  — upstream policy fields and key lifetime.
- `.github/workflows/publish.yml` — the publishing workflow.

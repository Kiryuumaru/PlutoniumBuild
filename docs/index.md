# Docs — PlutoniumBuild

Start with the [README](../README.md), then use these guides.

## Install and scaffold

The [README](../README.md) shows what PlutoniumBuild is, how to install the `PlutoniumBuild` package, and how to initialize the consumer scaffold with one command. The scaffold installs `build/` plus thin root `build.sh`, `build.ps1`, and `build.cmd` wrappers; `build/update.*` refreshes it later.

AI agents: start with [INIT.md](../INIT.md) for the install, run, CI, and update steps.

## Running your build

After the scaffold is installed, run `./build.sh Test`, `./build.sh Build`, or `./build.sh Publish`. The launcher installs the pinned SDK under `build/temp` when needed, then builds and runs `build/build.csproj`. See [Runner scripts and scaffold](workflows.md#runner-scripts-and-scaffold).

## Running any tool

[Terminal calls](terminal-calls.md) — run any external tool from a step: request shape, no shell, output capture and forwarding, timeout, and failure handling.

## Secrets

[Secrets](secrets.md) — declare a secret, scope it to entries, create it in your repository or pipeline, and understand the `PLUTONIUM_<NAME>` environment key, reserved names, and masking.

## CLI reference

[CLI reference](cli.md) — every command, its aliases, the shared options, and exit codes.

## Versioning and releases

[Versioning](versioning.md) — separate builder and app versions, release with tags or `versions.json`, and understand outcome stamps and watch behavior.

## Generating CI workflows

[Workflows](workflows.md) — generate the GitHub Actions and Azure Pipelines files from your steps.

## Developer documentation

For maintainers, not end users. Design, scope, and package internals:

- [Scope (authoritative for 0.1.0)](concepts/scope.md) — what is and is not included, including the package and scaffold.
- [ADR 0001: NuGet API-key publishing](adr/0001-nuget-api-key-publishing.md) — the superseded API-key decision.
- [ADR 0002: NuGet Trusted Publishing](adr/0002-nuget-trusted-publishing.md) — why publishing uses GitHub OIDC instead of a long-lived API key.
- [ADR 0003: Scaffold ref resolution](adr/0003-scaffold-ref-resolution.md) — why installs and updates resolve a revision to a commit SHA, and how `--ref` and `--sha` select and pin one.
- [Packages](dev/packages.md) — the single `PlutoniumBuild` package, its contents, and the dependency rule.
- [Architecture](dev/architecture.md) — package, scaffold, and library boundaries.
- [Recreate intent](../src/CONCEPT.md) — 1 page.

`samples/` stays planned — no samples yet.

## Project tooling

Agent workflow config lives here and is not part of the user path:

- [Issue tracker](agents/issue-tracker.md) — GitHub issues via `gh`.
- [Triage labels](agents/triage-labels.md) — default five-role vocabulary.
- [Domain docs](agents/domain.md) — single-context consumer rules.

The audit maps below are local, untracked audit material — read-only references used during the recreate. They are not shipped with the repo and will 404 on a fresh clone until the owner decides commit-vs-drop.

- `nuke_build_helpers/` (17 pages) — entries DSL, contexts, versioning, workflows, runner, cache/artifacts, secrets, CLI, console, deps + overlap/drop-keep/gaps.
- `nuke_build/` (12 pages) — targets, CLI/params, execution, logging/host, CI adapters, tooling, components, global tool, partitioning, solution/sourcegen, utilities.

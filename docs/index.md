# Docs — PlutoniumBuild

Start with the [README](../README.md), then use these guides.

## Getting started

The [README](../README.md) shows what PlutoniumBuild is, why to use it, and how to describe your build and wire it to the command line.

## Running your build

Copy the runner templates — [`build.sh`](../templates/build.sh), `build.ps1`, `build.cmd` — into your repository root so your build runs on a machine without the SDK; edit one line to point them at your build project. See [Runner scripts](workflows.md#runner-scripts).

## Running any tool

[Terminal calls](terminal-calls.md) — run any external tool from a step: request shape, no shell, output capture and forwarding, timeout, and failure handling.

## Secrets

[Secrets](secrets.md) — declare a secret, create it in your repository or pipeline, and understand the `PLUTONIUM_<NAME>` environment key, reserved names, and masking.

## CLI reference

[CLI reference](cli.md) — every command, its aliases, the shared options, and exit codes.

## Versioning and releases

[Versioning](versioning.md) — name your apps, check versions, and release new ones.

## Generating CI workflows

[Workflows](workflows.md) — generate the GitHub Actions and Azure Pipelines files from your steps.

## Developer documentation

For maintainers, not end users. Design, scope, and package internals:

- [Scope (authoritative for 0.1.0)](concepts/scope.md) — what is and is not included.
- [Packages](dev/packages.md) — what each library covers.
- [Architecture](dev/architecture.md) — how the three libraries fit together.
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

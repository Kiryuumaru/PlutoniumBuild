# PlutoniumBuild

In-house Helpers-style build-automation UX **without** a `Nuke.Common` dependency.

> Status: **Phase-1 landed.** `src/Plutonium.Core/` and
> `src/Plutonium.Helpers/` exist with Phase-1 code (see below).
> `tests/` and `samples/` remain **planned** and do not exist yet.
> Pins: `global.json` SDK `10.0.100` (`rollForward latestMajor`),
> `src/Directory.Build.props` version `0.1.0`.

## Purpose

Audited **recreation** (not copy-paste) of the `NukeBuild` + `NukeBuildHelpers`
user experience as `PlutoniumBuild`, facing build authors who today write
`BaseNukeBuildHelpers`-style builds:

- Target entries (`BuildEntry` / `TestEntry` / `PublishEntry`-style)
- `AppId`-keyed multi-app builds
- Versioning (`version` / `bump` / status-watching UX)
- GitHub Actions / Azure Pipelines workflow generation

PlutoniumBuild will provide that UX on its own runner, with **no
`Nuke.Common` dependency**. Phase-1 code has landed and is source of truth;
docs follow it.

## Inspirations (read-only, do not copy)

- `./.inspirations/NukeBuildHelpers` — `main @ 5c17b96`
  (Helpers UX: entries, `AppId`, versioning, workflow generation)
- `./.inspirations/NukeBuild` — `develop @ 27e8077`
  (runner / target / CI-adapter concepts)

Treat both as audit references only. No code is to be lifted verbatim;
recreate behavior behind a clean PlutoniumBuild API.

## Packages (Phase-1 status)

| Package | Intent |
| ------- | ------ |
| `Plutonium.Core` (`src/Plutonium.Core`, landed) | Target runner, plan-then-execute, CLI parsing, logging + secret masking, process result, path. Workflow emitters landed: `PlutoniumGitHubEmitter.cs` / `PlutoniumAzureEmitter.cs` (`Render` / `WriteWorkflow`, `PlutoniumHost.ExportJobOutput` output wiring) |
| `Plutonium.Helpers` (`src/Plutonium.Helpers`, landed) | Full entries (`TestEntry` / `BuildEntry` / `PublishEntry` / `WorkflowConfigEntry` definitions, discovery, extensions, runner), `AppId`, run-context shapes. Versioning UX landed (`PlutoniumBuildVerbs` `Version` / `Bump` / `BumpAndForget` / `StatusWatch`). Pipelines landed (`PlutoniumGithubPipeline` / `PlutoniumAzurePipeline` / `PlutoniumLocalPipeline`): CONDITION-only live outputs plus payload / `BUILD_ID` / `HAS_RELEASE` via `ExportJobOutput`; legacy full-matrix `BuildEntryExports` is `[Obsolete]` |
| Future transports / pipelines | Reserved; see `docs/packages.md` — not started |

Landed detail: Core — `Cli.cs` (`--skip` / `--list` / `--help`, unknown-arg
suggestions), `PlutoniumTargetDefinition.cs` + `Builder` (dependency DSL),
`PlutoniumRunner.cs` (topological plan, skip replanning, timing, cycle
detection), `Logging.cs` + `IPlutoniumLogSink.cs` + `PlutoniumLogLevel.cs`,
`PlutoniumPath.cs`, `PlutoniumProcessResult.cs` (TFMs `net8.0;net9.0;net10.0`),
`PlutoniumGitHubEmitter.cs` / `PlutoniumAzureEmitter.cs` (workflow emitters;
`Render` / `WriteWorkflow`).
Helpers — `PlutoniumAppId.cs`, `RunContext.cs` (`IRunContext` /
`AppRunContext`), `PlutoniumEntryDelegates.cs`,
`PlutoniumEntryDefinitions.cs` / `PlutoniumEntryDefinitionTypes.cs` /
`PlutoniumEntryDiscovery.cs` / `PlutoniumEntryExtensions.cs` /
`PlutoniumEntryRunner.cs` (full entries), `PlutoniumBuildVerbs.cs`
(`Version` / `Bump` / `BumpAndForget` / `StatusWatch` versioning UX),
`PlutoniumGithubPipeline.cs` / `PlutoniumAzurePipeline.cs` /
`PlutoniumLocalPipeline.cs` (pipelines; CONDITION-only live outputs plus
payload / `BUILD_ID` / `HAS_RELEASE`).
`Plutonium.Helpers` depends only on `Plutonium.Core`. Authoritative split:
`docs/concepts/scope.md`.

## Layout

```text
/
├── AGENTS.md               # agent working agreements (this workspace's rules)
├── README.md               # this file
├── PlutoniumBuild.sln      # solution (Phase-1 landed)
├── global.json             # SDK pin: 10.0.100, rollForward latestMajor (pin only)
├── src/
│   ├── CONCEPT.md          # 1-page recreate intent
│   ├── Directory.Build.props # version 0.1.0 / LangVersion / TFM settings
│   ├── Plutonium.Core/     # LANDED Phase-1 — runner, CLI, logging, path, process
│   └── Plutonium.Helpers/  # LANDED Phase-1 — entry stubs, AppId, run contexts
├── tests/                  # PLANNED — no code yet
├── samples/                # PLANNED — no code yet
└── docs/
    ├── index.md            # docs entry point
    ├── packages.md         # Phase-1 package status
    ├── agents/             # agent workflow config (issue-tracker, triage-labels, domain)
    └── concepts/
        └── scope.md        # authoritative in/out scope for 0.1.0
```

## Entry points

- Start at `docs/index.md`.
- Authoritative scope: `docs/concepts/scope.md` (authoritative for 0.1.0).
- Recreate intent: `src/CONCEPT.md`.
- Agent rules: `AGENTS.md`.

## Build

- `dotnet build PlutoniumBuild.sln` — build landed Phase-1 projects
  (`src/Plutonium.Core`, `src/Plutonium.Helpers`).
- SDK note: `global.json` pins SDK `10.0.100` with
  `rollForward latestMajor`.

## Contributing

Docs follow code: Phase-1 code is source of truth.
Keep docs internally consistent: every referenced path must exist or be
explicitly marked **planned**.

# Architecture — PlutoniumBuild

> Developer documentation — for maintainers, not end users.
>
> Status: **Phase-B landed.** `src/Plutonium.Core/`,
> `src/Plutonium.Helpers/`, and `src/Plutonium.Shell/` exist with landed
> code. `tests/` landed; `samples/` remains **planned**.
> Pins: `global.json` SDK `10.0.401` (`rollForward latestMajor`),
> `src/Directory.Build.props` version `0.1.0`.

Docs follow code: landed code is source of truth. Authoritative split:
`concepts/scope.md`.

## Projects and roles

| Project | Path | Role |
| ------- | ---- | ---- |
| `Plutonium.Core` | `src/Plutonium.Core/` | Standalone engine: target runner with dependency DSL, plan-then-execute, logging with secret masking, process runner (`dotnet` / `git` / `gh` surface), path utilities, and the GitHub + Azure workflow emitter pair. Owns planning and execution; owns no CLI parsing. |
| `Plutonium.Helpers` | `src/Plutonium.Helpers/` | Author-facing layer: entries DSL (`TestEntry` / `BuildEntry` / `PublishEntry` / `WorkflowConfigEntry` definitions, discovery, extensions, runner), `AppId`, run-context shapes, versioning UX, and the GitHub / Azure / local pipelines. Owns the verb set and `--args` conventions. |
| `Plutonium.Shell` | `src/Plutonium.Shell/` | CLI/app shell on `ApplicationBuilderHelpers` (`4.1.160`): 16 verbs plus kebab/legacy aliases, shared `--skip` / `--args` / `--list` / `--all`, per-invocation state, exit mapping `0` / `2` / `1` / `130`. Entry point: `AddPlutoniumShell`. Routes to Helpers verbs; adds no build semantics. |

## Dependency rules

Dependency direction: Shell → Helpers → Core.

- `Plutonium.Core` must not depend on `Nuke.Common`. Core and Helpers stay dependency-free (BCL only); the single third-party package, `ApplicationBuilderHelpers` (`4.1.160`), lives only in `Plutonium.Shell`.
- Verified in the project files: `Plutonium.Helpers.csproj` carries a single `ProjectReference` to Core; `Plutonium.Shell.csproj` carries the one `PackageReference` plus `ProjectReference`s to Core and Helpers.

## Layout

```text
src/
├── CONCEPT.md              # 1-page recreate intent
├── Directory.Build.props   # version 0.1.0 / LangVersion / TFM settings
├── Plutonium.Core/         # Runner/ Logging/ FileSystem/ Process/ Hosting/ Workflows/
├── Plutonium.Helpers/      # App/ Entries/ Runtime/ Pipelines/ Versioning/
└── Plutonium.Shell/        # PlutoniumShellExtensions.cs (AddPlutoniumShell),
                            # PlutoniumShellState.cs, PlutoniumShellCommand.cs,
                            # PlutoniumVerbCommands.cs, PlutoniumInteractiveCommand.cs,
                            # PlutoniumBumpArg.cs
tests/                      # Plutonium.Core.Tests, Plutonium.Helpers.Tests, Plutonium.Shell.Tests
samples/                    # PLANNED — no code yet
```

## Folder notes

- Core `Runner/` (`PlutoniumTargetDefinition.cs` + builder, `PlutoniumRunner.cs`): dependency DSL and the topological plan-then-execute runner. `Workflows/Emitters/` (`PlutoniumGitHubEmitter.cs`, `PlutoniumAzureEmitter.cs`): `Render` / `WriteWorkflow`, `PlutoniumHost.ExportJobOutput` output wiring.
- Helpers `Entries/` (`Definitions/` interfaces + entry definitions, `Discovery/` with `PlutoniumBuildBase` / `PlutoniumEntryDiscovery`, `Extensions/` fluent configuration, `PlutoniumEntryRunner.cs`), `App/` (`PlutoniumAppId.cs`, `PlutoniumBuildVerbs.cs` `Version` / `Bump` / `BumpAndForget` / `StatusWatch`), `Pipelines/` (`PlutoniumGitHubPipeline.cs` / `PlutoniumAzurePipeline.cs` / `PlutoniumLocalPipeline.cs`; `PlutoniumPipelineInvocation.cs` composes generated `shell pipeline-pre-setup` / `shell pipeline-post-setup` / `shell pipeline-entry-run` lines, entry jobs select one entry via `PLUTONIUM_ENTRY`, and the unlisted `PipelineEntryRun` verb rebuilds that entry from the pre-setup payload; CONDITION-only live outputs plus payload / `BUILD_ID` / `HAS_RELEASE` via `ExportJobOutput`; legacy full-matrix `BuildEntryExports` is `[Obsolete]`), `Runtime/` (`IRunContext` / `AppRunContext`), `Versioning/` (`PlutoniumBumpArgs` mini-language, `PlutoniumVersionTags` `{appId}/{SemVersion}` scheme).
- Shell: `PlutoniumShellCommand.cs` shared `--skip` / `--args` / `--list` / `--all` on the `shell` group node, `PlutoniumShellState.cs` per-invocation state, `PlutoniumBumpArg.cs` `--args` parser.
- Inspirations under `.inspirations/` (`NukeBuildHelpers` `main @ 5c17b96`, `NukeBuild` `develop @ 27e8077`) are read-only audit references. Audited recreation only — no verbatim copy.

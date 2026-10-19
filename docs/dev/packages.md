# Packages — PlutoniumBuild (Phase-B status)

> Developer documentation — for maintainers, not end users.
>
> Status: **Phase-B landed.** `src/Plutonium.Core/`,
> `src/Plutonium.Helpers/`, and `src/Plutonium.Shell/` exist with landed
> code: workflow emitters, pipelines (CONDITION-only live outputs plus
> payload / `BUILD_ID` / `HAS_RELEASE`), full entries, versioning UX, and
> the `ApplicationBuilderHelpers`-based CLI shell are built —
> code is source of truth.

| Package | Path | Intent |
| ------- | ---- | ------ |
| Plutonium.Core | `src/Plutonium.Core/` (landed) | Target runner, plan-then-execute, logging + secret masking, process result, path. Workflow emitters landed: `Workflows/Emitters/PlutoniumGitHubEmitter.cs` / `Workflows/Emitters/PlutoniumAzureEmitter.cs` (`Render` / `WriteWorkflow`, `PlutoniumHost.ExportJobOutput` output wiring). CLI ownership moved to `Plutonium.Shell`; Core carries no CLI parsing |
| Plutonium.Helpers | `src/Plutonium.Helpers/` (landed) | Full entries (`TestEntry` / `BuildEntry` / `PublishEntry` / `WorkflowConfigEntry` definitions, discovery, extensions, runner), `AppId`, run-context shapes (`IRunContext` / `AppRunContext`). Versioning UX landed (`App/PlutoniumBuildVerbs.cs` `Version` / `Bump` / `BumpAndForget` / `StatusWatch`). Pipelines landed (`Pipelines/PlutoniumGitHubPipeline.cs` / `Pipelines/PlutoniumAzurePipeline.cs` / `Pipelines/PlutoniumLocalPipeline.cs`), including `Pipelines/PlutoniumPipelineInvocation.cs` (composes generated `shell pipeline-pre-setup` / `shell pipeline-post-setup` / `shell pipeline-entry-run` lines) and the unlisted `PipelineEntryRun` verb (runs one entry selected by `PLUTONIUM_ENTRY`). `BuildPreSetupOutputs` emits CONDITION-only plus payload / `BUILD_ID` / `HAS_RELEASE` live outputs, `FinalizePreSetupAsync` wires via `ExportJobOutput`; legacy full-matrix `BuildEntryExports` is `[Obsolete]` |
| Plutonium.Shell | `src/Plutonium.Shell/` (landed) | CLI/app shell on `ApplicationBuilderHelpers` (`4.1.160`): 16 verbs plus kebab/legacy aliases, shared `--skip` / `--args` / `--list` / `--all`, per-invocation `PlutoniumShellState`, exit mapping `0` / `2` / `1` / `130`. Registered via `AddPlutoniumShell`. Depends on `Plutonium.Helpers` + `Plutonium.Core` |
| Future transports / pipelines | TBD | Reserved (e.g. other CI systems) — not started |

`Plutonium.Core` must not depend on `Nuke.Common`. `Plutonium.Helpers`
depends only on `Plutonium.Core`. `Plutonium.Shell` depends on
`Plutonium.Helpers` + `Plutonium.Core` plus the `ApplicationBuilderHelpers`
`4.1.160` package; Core and Helpers stay dependency-free. See authoritative
scope in `docs/concepts/scope.md`.

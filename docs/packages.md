# Packages — PlutoniumBuild (Phase-1 status)

> Status: **Phase-1 landed.** `src/Plutonium.Core/` and
> `src/Plutonium.Helpers/` exist with landed code: workflow emitters,
> pipelines (CONDITION-only live outputs plus payload / `BUILD_ID` /
> `HAS_RELEASE`), full entries, and versioning UX are built —
> code is source of truth.

| Package | Path | Intent |
| ------- | ---- | ------ |
| Plutonium.Core | `src/Plutonium.Core/` (landed) | Target runner, plan-then-execute, CLI parsing (`--skip` / `--list` / `--help`), logging + secret masking, process result, path. Workflow emitters landed: `PlutoniumGitHubEmitter.cs` / `PlutoniumAzureEmitter.cs` (`Render` / `WriteWorkflow`, `PlutoniumHost.ExportJobOutput` output wiring) |
| Plutonium.Helpers | `src/Plutonium.Helpers/` (landed) | Full entries (`TestEntry` / `BuildEntry` / `PublishEntry` / `WorkflowConfigEntry` definitions, discovery, extensions, runner), `AppId`, run-context shapes (`IRunContext` / `AppRunContext`). Versioning UX landed (`PlutoniumBuildVerbs` `Version` / `Bump` / `BumpAndForget` / `StatusWatch`). Pipelines landed (`PlutoniumGithubPipeline` / `PlutoniumAzurePipeline` / `PlutoniumLocalPipeline`): `BuildPreSetupOutputs` emits CONDITION-only plus payload / `BUILD_ID` / `HAS_RELEASE` live outputs, `FinalizePreSetupAsync` wires via `ExportJobOutput`; legacy full-matrix `BuildEntryExports` is `[Obsolete]` |
| Future transports / pipelines | TBD | Reserved (e.g. other CI systems) — not started |

`Plutonium.Core` must not depend on `Nuke.Common`. `Plutonium.Helpers`
depends only on `Plutonium.Core`. See authoritative scope in
`docs/concepts/scope.md`.
